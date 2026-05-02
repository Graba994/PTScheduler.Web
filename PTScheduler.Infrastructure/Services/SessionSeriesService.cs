using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SessionSeriesService(
    ApplicationDbContext db,
    ITrainerAvailabilityService availabilityService) : ISessionSeriesService
{
    public async Task<SeriesPreviewDto> PreviewAsync(CreateSessionSeriesDto dto)
    {
        var dates = GenerateDates(dto);
        var sessionType = await db.SessionTypes.FindAsync(dto.SessionTypeId)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");

        var conflictDates = new List<DateTime>();
        var scheduledDates = new List<DateTime>();

        foreach (var d in dates)
        {
            var free = await availabilityService.IsSlotFreeAsync(dto.TrainerUserId, d, sessionType.DurationMinutes);
            if (free) scheduledDates.Add(d);
            else conflictDates.Add(d);
        }

        var credits = await GetAvailableCreditsAsync(dto.ClientId, dto.SessionTypeId, dto.PackageId);
        var willScheduled = Math.Min(scheduledDates.Count, credits);
        var willAwaiting  = scheduledDates.Count - willScheduled;

        return new SeriesPreviewDto
        {
            ScheduledDates        = scheduledDates,
            ConflictDates         = conflictDates,
            AvailableCredits      = credits,
            WillBeScheduled       = willScheduled,
            WillBeAwaitingPackage = willAwaiting
        };
    }

    public async Task<SessionSeriesDto> CreateAsync(CreateSessionSeriesDto dto, bool skipConflicts = true)
    {
        var sessionType = await db.SessionTypes.FindAsync(dto.SessionTypeId)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");

        var dates = GenerateDates(dto);
        var credits = await GetAvailableCreditsAsync(dto.ClientId, dto.SessionTypeId, dto.PackageId);

        var series = new SessionSeries
        {
            ClientId       = dto.ClientId,
            TrainerUserId  = dto.TrainerUserId,
            SessionTypeId  = dto.SessionTypeId,
            RecurrenceDays = string.Join(",", dto.RecurrenceDays.Select(d => (int)d)),
            StartTime      = dto.StartTime,
            StartDate      = dto.StartDate,
            EndDate        = dto.EndDate,
            Notes          = dto.Notes,
            CreatedByUserId = dto.TrainerUserId,
            CreatedAt      = DateTime.Now
        };
        db.SessionSeries.Add(series);
        await db.SaveChangesAsync();

        int creditsUsed = 0;
        foreach (var start in dates)
        {
            var free = await availabilityService.IsSlotFreeAsync(dto.TrainerUserId, start, sessionType.DurationMinutes);
            if (!free)
            {
                if (!skipConflicts)
                    throw new InvalidOperationException($"Termin {start:dd.MM.yyyy HH:mm} jest zajęty.");
                continue;
            }

            var status = creditsUsed < credits ? SessionStatus.Scheduled : SessionStatus.AwaitingPackage;
            creditsUsed++;

            db.Sessions.Add(new Session
            {
                ClientId      = dto.ClientId,
                TrainerUserId = dto.TrainerUserId,
                SessionTypeId = dto.SessionTypeId,
                StartTime     = start,
                Status        = status,
                Notes         = dto.Notes,
                PackageId     = dto.PackageId,
                SeriesId      = series.Id,
                CreatedAt     = DateTime.Now
            });
        }

        await db.SaveChangesAsync();

        return await BuildDtoAsync(series);
    }

    public async Task<List<SessionSeriesDto>> GetSeriesForClientAsync(int clientId)
    {
        var list = await db.SessionSeries
            .Include(s => s.SessionType)
            .Where(s => s.ClientId == clientId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var result = new List<SessionSeriesDto>();
        foreach (var s in list) result.Add(await BuildDtoAsync(s));
        return result;
    }

    public async Task<List<SessionSeriesDto>> GetSeriesForTrainerAsync(string trainerUserId)
    {
        var list = await db.SessionSeries
            .Include(s => s.SessionType)
            .Where(s => s.TrainerUserId == trainerUserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var result = new List<SessionSeriesDto>();
        foreach (var s in list) result.Add(await BuildDtoAsync(s));
        return result;
    }

    public async Task CancelSeriesAsync(int seriesId, bool cancelFutureSessions = true)
    {
        var series = await db.SessionSeries.FindAsync(seriesId)
            ?? throw new InvalidOperationException("Seria nie istnieje.");
        series.IsActive = false;

        if (cancelFutureSessions)
        {
            var now = DateTime.Now;
            var futureSessions = await db.Sessions
                .Where(s => s.SeriesId == seriesId
                            && s.StartTime >= now
                            && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.AwaitingPackage))
                .ToListAsync();
            foreach (var s in futureSessions)
            {
                s.Status = SessionStatus.Cancelled;
                s.CancelledAt = DateTime.Now;
            }
        }

        await db.SaveChangesAsync();
    }

    // ─── private helpers ───────────────────────────────────────────────────

    private static List<DateTime> GenerateDates(CreateSessionSeriesDto dto)
    {
        var dates = new List<DateTime>();
        var end   = dto.EndDate ?? dto.StartDate.AddYears(1);
        var day   = dto.StartDate;

        while (day <= end)
        {
            if (dto.RecurrenceDays.Contains(day.DayOfWeek))
                dates.Add(day.ToDateTime(dto.StartTime));
            day = day.AddDays(1);
        }

        return dates;
    }

    private async Task<int> GetAvailableCreditsAsync(int clientId, int sessionTypeId, int? packageId)
    {
        var query = db.SessionPackages
            .Where(p => p.ClientId == clientId
                        && p.SessionTypeId == sessionTypeId
                        && p.Status == PackageStatus.Active
                        && p.UsedSessions < p.TotalSessions);

        if (packageId.HasValue)
            query = query.Where(p => p.Id == packageId.Value);

        var packages = await query.ToListAsync();
        return packages.Sum(p => p.TotalSessions - p.UsedSessions);
    }

    private async Task<SessionSeriesDto> BuildDtoAsync(SessionSeries s)
    {
        var now = DateTime.Now;
        var sessionsCount = await db.Sessions.CountAsync(x => x.SeriesId == s.Id);
        var futureCount   = await db.Sessions.CountAsync(x => x.SeriesId == s.Id && x.StartTime >= now
                                && (x.Status == SessionStatus.Scheduled || x.Status == SessionStatus.AwaitingPackage));
        var client = await db.Clients.FindAsync(s.ClientId);
        var clientName = client is null ? "" : $"{client.FirstName} {client.LastName}".Trim();

        var days = s.RecurrenceDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => (DayOfWeek)int.Parse(x))
            .ToList();

        return new SessionSeriesDto
        {
            Id = s.Id,
            ClientId = s.ClientId,
            ClientName = clientName,
            TrainerUserId = s.TrainerUserId,
            SessionTypeId = s.SessionTypeId,
            SessionTypeName = s.SessionType?.Name ?? "",
            RecurrenceDays = days,
            StartTime = s.StartTime,
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            Notes = s.Notes,
            IsActive = s.IsActive,
            TotalSessionsCreated = sessionsCount,
            FutureSessionsCount  = futureCount
        };
    }
}
