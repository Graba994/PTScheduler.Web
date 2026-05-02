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
    ISessionPackageService packageService,
    IEmailService emailService) : ISessionService
{
    public async Task<List<SessionDto>> GetSessionsAsync(DateTime from, DateTime to, string? trainerUserId = null, int? clientId = null)
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

        if (clientId.HasValue)
            query = query.Where(s => s.ClientId == clientId.Value);

        var sessions = await query.OrderBy(s => s.StartTime).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await userManager.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? u.Id);

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<List<SessionDto>> GetPastSessionsAsync(string? trainerUserId = null, int? clientId = null, int count = 50)
    {
        var now = DateTime.Now;
        var query = db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime < now || s.Status != SessionStatus.Scheduled);

        if (trainerUserId is not null)
        {
            var subordinateIds = await userManager.Users
                .Where(u => u.SupervisorId == trainerUserId)
                .Select(u => u.Id)
                .ToListAsync();
            var visibleIds = subordinateIds.Append(trainerUserId).ToList();
            query = query.Where(s => visibleIds.Contains(s.TrainerUserId));
        }

        if (clientId.HasValue)
            query = query.Where(s => s.ClientId == clientId.Value);

        var sessions = await query.OrderByDescending(s => s.StartTime).Take(count).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await userManager.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? u.Id);
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

        try { await SendBookingConfirmationAsync(session, sessionType); } catch { }

        return (await GetSessionAsync(session.Id))!;
    }

    public async Task UpdateStatusAsync(int id, SessionStatus status, string? cancellationReason = null)
    {
        var session = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Session not found.");

        if (status == SessionStatus.Cancelled)
        {
            session.CancelledAt = DateTime.Now;
            session.CancellationReason = cancellationReason;
        }

        session.Status = status;
        await db.SaveChangesAsync();

        if ((status == SessionStatus.Completed || status == SessionStatus.NoShow) && session.PackageId.HasValue)
            await packageService.DeductCreditAsync(session.PackageId.Value);

        if (status == SessionStatus.Cancelled)
            try { await SendCancellationEmailAsync(session, cancellationReason); } catch { }
    }

    public async Task RescheduleAsync(int id, DateTime newStartTime)
    {
        var session = await db.Sessions.FindAsync(id)
            ?? throw new InvalidOperationException("Sesja nie została znaleziona.");
        session.StartTime = newStartTime;
        await db.SaveChangesAsync();
    }

    public async Task RestoreAsync(int id)
    {
        var session = await db.Sessions.FindAsync(id)
            ?? throw new InvalidOperationException("Sesja nie została znaleziona.");
        if (session.Status != SessionStatus.Cancelled && session.Status != SessionStatus.NoShow)
            throw new InvalidOperationException("Można przywrócić tylko anulowane lub nieobecne wizyty.");
        session.Status = SessionStatus.Scheduled;
        session.CancelledAt = null;
        session.CancellationReason = null;
        await db.SaveChangesAsync();
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
        var now = DateTime.Now;
        var query = db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= now
                        && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.AwaitingPackage));

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

    public async Task<List<SessionDto>> GetAwaitingPackageAsync(string? trainerUserId = null)
    {
        var now = DateTime.Now;
        var query = db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.Status == SessionStatus.AwaitingPackage && s.StartTime >= now);

        if (trainerUserId is not null)
            query = query.Where(s => s.TrainerUserId == trainerUserId);

        var sessions = await query.OrderBy(s => s.StartTime).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await userManager.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? u.Id);
        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    private async Task SendBookingConfirmationAsync(Session session, SessionType sessionType)
    {
        if (!await emailService.IsEnabledAsync()) return;
        var client = await db.Clients.FindAsync(session.ClientId);
        if (client is null) return;
        var clientUser = await userManager.FindByIdAsync(client.ApplicationUserId);
        if (clientUser?.Email is null) return;

        var trainer = await userManager.FindByIdAsync(session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? "Trener";
        var clientName = $"{client.FirstName} {client.LastName}".Trim().NullIfEmpty() ?? clientUser.Email;
        var html = BuildBookingHtml(clientName, trainerName, sessionType.Name, session.StartTime, sessionType.DurationMinutes);
        await emailService.SendAsync(clientUser.Email, clientName, "Potwierdzenie rezerwacji wizyty", html);
    }

    private async Task SendCancellationEmailAsync(Session session, string? reason)
    {
        if (!await emailService.IsEnabledAsync()) return;
        var clientUser = await userManager.FindByIdAsync(session.Client.ApplicationUserId);
        if (clientUser?.Email is null) return;

        var trainer = await userManager.FindByIdAsync(session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? "Trener";
        var clientName = $"{session.Client.FirstName} {session.Client.LastName}".Trim().NullIfEmpty() ?? clientUser.Email;
        var html = BuildCancellationHtml(clientName, trainerName, session.SessionType.Name, session.StartTime, reason);
        await emailService.SendAsync(clientUser.Email, clientName, "Anulowanie wizyty", html);
    }

    private static string BuildBookingHtml(string clientName, string trainerName, string sessionType, DateTime startTime, int durationMin) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:#0284C7;border-radius:8px 8px 0 0;padding:24px;text-align:center">
            <h2 style="color:white;margin:0;font-size:20px">Potwierdzenie rezerwacji</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            <p style="color:#374151;font-size:15px">Cześć <strong>{clientName}</strong>!</p>
            <p style="color:#374151;font-size:15px">Twoja wizyta została zarezerwowana. Szczegóły:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{sessionType}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Data i godzina</td><td style="padding:8px 0;font-size:14px;font-weight:600">{startTime:dddd\, dd MMMM yyyy} o {startTime:HH:mm}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Czas trwania</td><td style="padding:8px 0;font-size:14px;font-weight:600">{durationMin} minut</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Trener</td><td style="padding:8px 0;font-size:14px;font-weight:600">{trainerName}</td></tr>
            </table>
            <p style="color:#9ca3af;font-size:12px;margin-top:24px;padding-top:16px;border-top:1px solid #f3f4f6;text-align:center">
              Wiadomość automatyczna — nie odpowiadaj na ten email.
            </p>
          </div>
        </div>
        """;

    private static string BuildCancellationHtml(string clientName, string trainerName, string sessionType, DateTime startTime, string? reason) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:#DC2626;border-radius:8px 8px 0 0;padding:24px;text-align:center">
            <h2 style="color:white;margin:0;font-size:20px">Wizyta anulowana</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            <p style="color:#374151;font-size:15px">Cześć <strong>{clientName}</strong>!</p>
            <p style="color:#374151;font-size:15px">Twoja wizyta została anulowana. Szczegóły:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{sessionType}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Data i godzina</td><td style="padding:8px 0;font-size:14px;font-weight:600">{startTime:dddd\, dd MMMM yyyy} o {startTime:HH:mm}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Trener</td><td style="padding:8px 0;font-size:14px;font-weight:600">{trainerName}</td></tr>
              {(reason is not null ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Powód</td><td style=\"padding:8px 0;font-size:14px\">{reason}</td></tr>" : "")}
            </table>
            <p style="color:#9ca3af;font-size:12px;margin-top:24px;padding-top:16px;border-top:1px solid #f3f4f6;text-align:center">
              Wiadomość automatyczna — nie odpowiadaj na ten email.
            </p>
          </div>
        </div>
        """;

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
