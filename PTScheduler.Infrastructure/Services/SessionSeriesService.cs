using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SessionSeriesService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITrainerAvailabilityService availabilityService,
    IAppClock clock) : ISessionSeriesService
{
    public async Task<SeriesPreviewDto> PreviewAsync(CreateSessionSeriesDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
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

        var credits = await GetAvailableCreditsAsync(db, dto.ClientId, dto.SessionTypeId, dto.PackageId);
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
        await using var db = dbFactory.CreateDbContext();
        var sessionType = await db.SessionTypes.FindAsync(dto.SessionTypeId)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");

        var dates = GenerateDates(dto);

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
            CreatedAt      = DateTime.UtcNow
        };
        db.SessionSeries.Add(series);
        await db.SaveChangesAsync();

        // Load matching packages ordered by soonest expiry
        var packages = await db.SessionPackages
            .Where(p => p.ClientId == dto.ClientId
                     && p.SessionTypeId == dto.SessionTypeId
                     && p.Status == PackageStatus.Active
                     && p.UsedSessions < p.TotalSessions)
            .OrderBy(p => p.ExpiresAt ?? DateTime.MaxValue)
            .ToListAsync();

        // Use package pointer so we exhaust one before moving to next
        int pkgIdx = 0;

        foreach (var start in dates)
        {
            var free = await availabilityService.IsSlotFreeAsync(dto.TrainerUserId, start, sessionType.DurationMinutes);
            if (!free)
            {
                if (!skipConflicts)
                    throw new InvalidOperationException($"Termin {start:dd.MM.yyyy HH:mm} jest zajęty.");
                continue;
            }

            // Advance to a package that still has credits
            while (pkgIdx < packages.Count && packages[pkgIdx].UsedSessions >= packages[pkgIdx].TotalSessions)
                pkgIdx++;

            int? linkedPackageId = null;
            var status = SessionStatus.AwaitingPackage;

            if (pkgIdx < packages.Count)
            {
                var pkg = packages[pkgIdx];
                linkedPackageId = pkg.Id;
                pkg.UsedSessions++;
                if (pkg.UsedSessions >= pkg.TotalSessions)
                    pkg.Status = PackageStatus.Depleted;
                status = SessionStatus.Scheduled;
            }

            db.Sessions.Add(new Session
            {
                ClientId      = dto.ClientId,
                TrainerUserId = dto.TrainerUserId,
                SessionTypeId = dto.SessionTypeId,
                StartTime     = start,
                Status        = status,
                PackageId     = linkedPackageId,
                SeriesId      = series.Id,
                Notes         = dto.Notes,
                CreatedAt     = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        return (await BuildDtosAsync(db, [series], clock.LocalNow)).First();
    }

    public async Task<List<SessionSeriesDto>> GetSeriesForClientAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var list = await db.SessionSeries
            .Include(s => s.SessionType)
            .Where(s => s.ClientId == clientId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return await BuildDtosAsync(db, list, clock.LocalNow);
    }

    public async Task<List<SessionSeriesDto>> GetSeriesForTrainerAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var list = await db.SessionSeries
            .Include(s => s.SessionType)
            .Where(s => s.TrainerUserId == trainerUserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return await BuildDtosAsync(db, list, clock.LocalNow);
    }

    public async Task CancelSeriesAsync(int seriesId, bool cancelFutureSessions = true)
    {
        await using var db = dbFactory.CreateDbContext();
        var series = await db.SessionSeries.FindAsync(seriesId)
            ?? throw new InvalidOperationException("Seria nie istnieje.");
        series.IsActive = false;

        if (cancelFutureSessions)
        {
            var now = clock.LocalNow;   // zegar ścienny — porównywany ze StartTime
            var futureSessions = await db.Sessions
                .Where(s => s.SeriesId == seriesId
                            && s.StartTime >= now
                            && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.AwaitingPackage))
                .ToListAsync();
            foreach (var s in futureSessions)
            {
                if (s.PackageId.HasValue)
                {
                    var pkg = await db.SessionPackages.FindAsync(s.PackageId.Value);
                    if (pkg is not null && pkg.Status != PackageStatus.Cancelled)
                    {
                        if (pkg.UsedSessions > 0) pkg.UsedSessions--;
                        if (pkg.Status == PackageStatus.Depleted && pkg.UsedSessions < pkg.TotalSessions)
                            pkg.Status = PackageStatus.Active;
                    }
                }
                s.Status = SessionStatus.Cancelled;
                s.CancelledAt = DateTime.UtcNow;
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

    private static async Task<int> GetAvailableCreditsAsync(ApplicationDbContext db, int clientId, int sessionTypeId, int? packageId)
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

    // 'now' to zegar ścienny (clock.LocalNow), przekazywany przez metody
    // instancyjne — statyczna metoda nie ma dostępu do parametru clock.
    private static async Task<List<SessionSeriesDto>> BuildDtosAsync(ApplicationDbContext db, List<SessionSeries> seriesList, DateTime now)
    {
        if (seriesList.Count == 0) return [];

        var seriesIds = seriesList.Select(s => s.Id).ToList();
        var clientIds = seriesList.Select(s => s.ClientId).Distinct().ToList();

        var sessionCounts = await db.Sessions
            .Where(x => seriesIds.Contains(x.SeriesId ?? 0))
            .GroupBy(x => x.SeriesId)
            .Select(g => new { SeriesId = g.Key, Total = g.Count(),
                Future = g.Count(x => x.StartTime >= now && (x.Status == SessionStatus.Scheduled || x.Status == SessionStatus.AwaitingPackage)) })
            .ToDictionaryAsync(x => x.SeriesId ?? 0, x => (x.Total, x.Future));

        var clients = await db.Clients
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FirstName, c.LastName })
            .ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}".Trim());

        return seriesList.Select(s =>
        {
            sessionCounts.TryGetValue(s.Id, out var counts);
            clients.TryGetValue(s.ClientId, out var clientName);

            var days = s.RecurrenceDays
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => (DayOfWeek)int.Parse(x))
                .ToList();

            return new SessionSeriesDto
            {
                Id = s.Id,
                ClientId = s.ClientId,
                ClientName = clientName ?? "",
                TrainerUserId = s.TrainerUserId,
                SessionTypeId = s.SessionTypeId,
                SessionTypeName = s.SessionType?.Name ?? "",
                RecurrenceDays = days,
                StartTime = s.StartTime,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                Notes = s.Notes,
                IsActive = s.IsActive,
                TotalSessionsCreated = counts.Total,
                FutureSessionsCount  = counts.Future
            };
        }).ToList();
    }
}
