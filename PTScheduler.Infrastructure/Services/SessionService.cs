using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;

namespace PTScheduler.Infrastructure.Services;

public class SessionService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ISessionPackageService packageService) : ISessionService
{
    public async Task<List<SessionDto>> GetSessionsAsync(DateTime from, DateTime to, string? trainerUserId = null)
    {
        var query = db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= from && s.StartTime < to);

        if (trainerUserId is not null)
        {
            var trainer = await userManager.FindByIdAsync(trainerUserId);
            if (trainer is not null)
            {
                var subordinateIds = await userManager.Users
                    .Where(u => u.SupervisorId == trainerUserId)
                    .Select(u => u.Id)
                    .ToListAsync();

                var visibleTrainerIds = subordinateIds.Append(trainerUserId).ToList();
                query = query.Where(s => visibleTrainerIds.Contains(s.TrainerUserId));
            }
        }

        var sessions = await query.OrderBy(s => s.StartTime).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await userManager.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? u.Id);

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<SessionDto?> GetSessionAsync(int id)
    {
        var session = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (session is null) return null;

        var trainer = await userManager.FindByIdAsync(session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? session.TrainerUserId;
        return MapToDto(session, new Dictionary<string, string> { [session.TrainerUserId] = trainerName });
    }

    public async Task<SessionDto> CreateSessionAsync(CreateSessionDto dto)
    {
        var sessionType = await db.SessionTypes.FindAsync(dto.SessionTypeId)
            ?? throw new InvalidOperationException("Session type not found.");

        var session = new Session
        {
            ClientId = dto.ClientId,
            SessionTypeId = dto.SessionTypeId,
            TrainerUserId = dto.TrainerUserId,
            StartTime = dto.StartTime,
            Status = SessionStatus.Scheduled,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        return (await GetSessionAsync(session.Id))!;
    }

    public async Task UpdateStatusAsync(int id, SessionStatus status, string? cancellationReason = null)
    {
        var session = await db.Sessions.FindAsync(id)
            ?? throw new InvalidOperationException("Session not found.");

        if (status == SessionStatus.Cancelled)
        {
            if (session.StartTime <= DateTime.Now.AddHours(24))
                throw new InvalidOperationException("Nie można anulować wizyty na mniej niż 24h przed jej rozpoczęciem.");

            session.CancelledAt = DateTime.UtcNow;
            session.CancellationReason = cancellationReason;
        }

        session.Status = status;
        await db.SaveChangesAsync();

        if (status == SessionStatus.Completed && session.PackageId.HasValue)
            await packageService.DeductCreditAsync(session.PackageId.Value);
    }

    public async Task<List<SessionTypeDto>> GetSessionTypesAsync() =>
        await db.SessionTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.DurationMinutes)
            .Select(t => new SessionTypeDto
            {
                Id = t.Id,
                Name = t.Name,
                DurationMinutes = t.DurationMinutes,
                IsGroup = t.IsGroup
            })
            .ToListAsync();

    public async Task<List<ClientSummaryDto>> GetClientsAsync()
    {
        var clients = await db.Clients
            .ToListAsync();

        var userIds = clients.Select(c => c.ApplicationUserId).ToList();
        var users = await userManager.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return clients.Select(c =>
        {
            var user = users.GetValueOrDefault(c.ApplicationUserId);
            var fullName = $"{c.FirstName} {c.LastName}".Trim();
            return new ClientSummaryDto
            {
                Id = c.Id,
                ApplicationUserId = c.ApplicationUserId,
                FullName = string.IsNullOrEmpty(fullName) ? (user?.Email ?? "Klient") : fullName,
                Email = user?.Email ?? string.Empty
            };
        }).OrderBy(c => c.FullName).ToList();
    }

    public async Task<List<SessionDto>> GetClientSessionsAsync(int clientId, int count = 20)
    {
        var sessions = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.ClientId == clientId)
            .OrderByDescending(s => s.StartTime)
            .Take(count)
            .ToListAsync();

        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await userManager.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id);

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<List<SessionDto>> GetUpcomingAsync(string? trainerUserId = null, int? clientId = null, int count = 10)
    {
        var now = DateTime.UtcNow;
        var query = db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= now && s.Status == SessionStatus.Scheduled);

        if (trainerUserId is not null)
            query = query.Where(s => s.TrainerUserId == trainerUserId);

        if (clientId.HasValue)
            query = query.Where(s => s.ClientId == clientId.Value);

        var sessions = await query.OrderBy(s => s.StartTime).Take(count).ToListAsync();

        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await userManager.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id);

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    private static SessionDto MapToDto(Session s, Dictionary<string, string> trainers) => new()
    {
        Id = s.Id,
        ClientId = s.ClientId,
        ClientName = $"{s.Client.FirstName} {s.Client.LastName}".Trim().NullIfEmpty() ?? s.Client.ApplicationUserId,
        ClientEmail = string.Empty,
        SessionTypeId = s.SessionTypeId,
        SessionTypeName = s.SessionType.Name,
        DurationMinutes = s.SessionType.DurationMinutes,
        TrainerUserId = s.TrainerUserId,
        TrainerName = trainers.GetValueOrDefault(s.TrainerUserId, s.TrainerUserId),
        StartTime = s.StartTime,
        Status = s.Status,
        Notes = s.Notes,
        CancellationReason = s.CancellationReason
    };
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
