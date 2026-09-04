using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SessionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IEmailService emailService,
    IEmailTemplateService emailTemplateService,
    INotificationPreferencesService notificationPrefs,
    IGoogleMeetService googleMeetService,
    IAppClock clock,
    ILogger<SessionService> logger) : ISessionService
{
    public async Task<List<SessionDto>> GetSessionsAsync(DateTime from, DateTime to, string? trainerUserId = null, int? clientId = null)
    {
        // StartTime jest zegarem ściennym (kolumna timestamp without time zone),
        // więc granice zakresu też muszą nim być. Normalizacja Kind, nie konwersja —
        // wartość godziny pozostaje nietknięta.
        from = DateTime.SpecifyKind(from, DateTimeKind.Unspecified);
        to   = DateTime.SpecifyKind(to,   DateTimeKind.Unspecified);
        await using var db = dbFactory.CreateDbContext();
        var query = db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= from && s.StartTime < to);

        if (trainerUserId is not null)
        {
            var subordinateIds = await db.Users
                .Where(u => u.SupervisorId == trainerUserId)
                .Select(u => u.Id)
                .ToListAsync();

            var visibleTrainerIds = subordinateIds.Append(trainerUserId).ToList();
            query = query.Where(s => visibleTrainerIds.Contains(s.TrainerUserId));
        }

        if (clientId.HasValue)
            query = query.Where(s => s.ClientId == clientId.Value);

        var sessions = await query.OrderBy(s => s.StartTime).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? "Trener");

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<List<SessionDto>> GetPastSessionsAsync(string? trainerUserId = null, int? clientId = null, int count = 50)
    {
        await using var db = dbFactory.CreateDbContext();
        // Zegar ścienny — StartTime nim jest. UtcNow dawałoby granicę przesuniętą
        // o offset strefy, więc sesja sprzed godziny mogła jeszcze uchodzić za przyszłą.
        var now = clock.LocalNow;
        var query = db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime < now || s.Status != SessionStatus.Scheduled);

        if (trainerUserId is not null)
        {
            var subordinateIds = await db.Users
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
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? "Trener");
        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<SessionDto?> GetSessionAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (session is null) return null;

        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? session.TrainerUserId;
        return MapToDto(session, new Dictionary<string, string> { [session.TrainerUserId] = trainerName });
    }

    public async Task<SessionDto> CreateSessionAsync(CreateSessionDto dto, bool allowAwaitingPackage = true)
    {
        // Godzina przychodzi z <input type="datetime-local"> jako zegar ścienny.
        // Zapisujemy ją bez konwersji — 14:00 wpisane przez trenera to 14:00
        // w studiu, niezależnie od strefy kontenera.
        dto.StartTime = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Unspecified);
        await using var db = dbFactory.CreateDbContext();
        var sessionType = await db.SessionTypes.FindAsync(dto.SessionTypeId)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");

        var package = await db.SessionPackages
            .Where(p => p.ClientId == dto.ClientId
                     && p.SessionTypeId == dto.SessionTypeId
                     && p.Status == PackageStatus.Active
                     && p.UsedSessions < p.TotalSessions)
            .OrderBy(p => p.ExpiresAt ?? DateTime.MaxValue)
            .FirstOrDefaultAsync();

        if (package is null && !allowAwaitingPackage)
            throw new InvalidOperationException("Nie masz aktywnego pakietu dla tego rodzaju sesji.");

        var session = new Session
        {
            ClientId      = dto.ClientId,
            SessionTypeId = dto.SessionTypeId,
            TrainerUserId = dto.TrainerUserId,
            StartTime     = dto.StartTime,
            Status        = package is not null ? SessionStatus.Scheduled : SessionStatus.AwaitingPackage,
            PackageId     = package?.Id,
            Notes         = dto.Notes,
            CreatedAt     = DateTime.UtcNow
        };

        db.Sessions.Add(session);

        if (package is not null)
        {
            package.UsedSessions++;
            if (package.UsedSessions >= package.TotalSessions)
                package.Status = PackageStatus.Depleted;
        }

        await db.SaveChangesAsync();

        try
        {
            if (googleMeetService.IsConfigured)
            {
                var client = await db.Clients.FindAsync(session.ClientId);
                var clientUser = client is not null
                    ? await db.Users.FirstOrDefaultAsync(u => u.Id == client.ApplicationUserId)
                    : null;
                var result = await googleMeetService.CreateMeetingAsync(
                    $"{sessionType.Name} — {client?.FirstName} {client?.LastName}".Trim(),
                    $"Sesja treningowa: {sessionType.Name}, {sessionType.DurationMinutes} min",
                    session.StartTime,
                    sessionType.DurationMinutes,
                    clientUser?.Email);
                if (result is not null)
                {
                    session.MeetingUrl = result.MeetingUrl;
                    session.CalendarEventId = result.CalendarEventId;
                    await db.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd tworzenia Google Meet (SessionId={Id})", session.Id); }

        try { await SendBookingConfirmationAsync(session, sessionType); }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila potwierdzającego rezerwację (SessionId={Id})", session.Id); }
        return (await GetSessionAsync(session.Id))!;
    }

    public async Task UpdateStatusAsync(int id, SessionStatus status, string? cancellationReason = null, string? completionNotes = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Session not found.");

        if (status == SessionStatus.Cancelled)
        {
            session.CancelledAt = DateTime.UtcNow;
            session.CancellationReason = cancellationReason;

            if (session.PackageId.HasValue)
            {
                var pkg = await db.SessionPackages.FindAsync(session.PackageId.Value);
                if (pkg is not null && pkg.Status != PackageStatus.Cancelled)
                {
                    if (pkg.UsedSessions > 0) pkg.UsedSessions--;
                    if (pkg.Status == PackageStatus.Depleted && pkg.UsedSessions < pkg.TotalSessions)
                        pkg.Status = PackageStatus.Active;
                }
            }
        }

        if (status == SessionStatus.Completed && completionNotes is not null)
            session.Notes = completionNotes;

        session.Status = status;
        await db.SaveChangesAsync();

        if (status == SessionStatus.Cancelled)
        {
            try { if (session.CalendarEventId is not null) await googleMeetService.DeleteMeetingAsync(session.CalendarEventId); }
            catch (Exception ex) { logger.LogWarning(ex, "Błąd usuwania Google Meet (SessionId={Id})", session.Id); }

            try { await SendCancellationEmailAsync(session, cancellationReason); }
            catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila o anulowaniu (SessionId={Id})", session.Id); }
        }
    }

    public async Task RescheduleAsync(int id, DateTime newStartTime)
    {
        newStartTime = DateTime.SpecifyKind(newStartTime, DateTimeKind.Unspecified);
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Sesja nie została znaleziona.");
        var oldTime = session.StartTime;
        session.StartTime = newStartTime;
        await db.SaveChangesAsync();
        try { await SendRescheduleEmailAsync(session, oldTime); }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila o zmianie terminu (SessionId={Id})", session.Id); }
    }

    public async Task RestoreAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions.FindAsync(id)
            ?? throw new InvalidOperationException("Sesja nie została znaleziona.");

        var wasStatus = session.Status;
        if (wasStatus != SessionStatus.Cancelled && wasStatus != SessionStatus.NoShow)
            throw new InvalidOperationException("Można przywrócić tylko anulowane lub nieobecne wizyty.");

        session.CancelledAt = null;
        session.CancellationReason = null;

        if (wasStatus == SessionStatus.Cancelled && session.PackageId.HasValue)
        {
            var pkg = await db.SessionPackages.FindAsync(session.PackageId.Value);
            if (pkg is not null && pkg.Status != PackageStatus.Cancelled
                && (pkg.Status == PackageStatus.Active || pkg.Status == PackageStatus.Depleted))
            {
                pkg.UsedSessions++;
                if (pkg.UsedSessions >= pkg.TotalSessions && pkg.Status == PackageStatus.Active)
                    pkg.Status = PackageStatus.Depleted;
                session.Status = SessionStatus.Scheduled;
            }
            else
            {
                session.PackageId = null;
                session.Status = SessionStatus.AwaitingPackage;
            }
        }
        else
        {
            session.Status = session.PackageId.HasValue ? SessionStatus.Scheduled : SessionStatus.AwaitingPackage;
        }

        await db.SaveChangesAsync();
    }

    public async Task ClientCancelSessionAsync(int id, string clientUserId, string? reason = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Sesja nie została znaleziona.");

        session.Status = SessionStatus.Cancelled;
        session.CancelledAt = DateTime.UtcNow;
        session.CancellationReason = reason;

        if (session.PackageId.HasValue)
        {
            var pkg = await db.SessionPackages.FindAsync(session.PackageId.Value);
            if (pkg is not null && pkg.Status != PackageStatus.Cancelled)
            {
                if (pkg.UsedSessions > 0) pkg.UsedSessions--;
                if (pkg.Status == PackageStatus.Depleted && pkg.UsedSessions < pkg.TotalSessions)
                    pkg.Status = PackageStatus.Active;
            }
        }

        await db.SaveChangesAsync();

        try { await SendClientCancelledToTrainerAsync(session, reason); }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila o anulowaniu przez klienta (SessionId={Id})", session.Id); }
    }

    public async Task<List<SessionTypeDto>> GetSessionTypesAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.SessionTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.DurationMinutes)
            .Select(t => new SessionTypeDto
            {
                Id = t.Id,
                Name = t.Name,
                DurationMinutes = t.DurationMinutes,
                IsGroup = t.IsGroup,
                MaxParticipants = t.MaxParticipants,
                IsActive = t.IsActive
            })
            .ToListAsync();
    }

    public async Task<List<ClientSummaryDto>> GetClientsAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var clients = await db.Clients
            .AsNoTracking()
            .Where(c => trainerUserId == null || c.TrainerUserId == trainerUserId)
            .ToListAsync();

        var userIds = clients.Select(c => c.ApplicationUserId).ToList();
        var users = await db.Users
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
        await using var db = dbFactory.CreateDbContext();
        var sessions = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.ClientId == clientId)
            .OrderByDescending(s => s.StartTime)
            .Take(count)
            .ToListAsync();

        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? "Trener");

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<List<SessionDto>> GetUpcomingAsync(string? trainerUserId = null, int? clientId = null, int count = 10)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = clock.LocalNow;   // zegar ścienny — porównywany ze StartTime
        var query = db.Sessions
            .AsNoTracking()
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
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? "Trener");

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<List<SessionDto>> GetAwaitingPackageAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = clock.LocalNow;   // zegar ścienny — porównywany ze StartTime
        var query = db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.Status == SessionStatus.AwaitingPackage && s.StartTime >= now);

        if (trainerUserId is not null)
            query = query.Where(s => s.TrainerUserId == trainerUserId);

        var sessions = await query.OrderBy(s => s.StartTime).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? "Trener");
        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    private async Task SendBookingConfirmationAsync(Session session, SessionType sessionType)
    {
        if (!await emailService.IsEnabledAsync()) return;
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(session.ClientId);
        if (client is null) return;
        if (!await notificationPrefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.SessionBooked)) return;
        var clientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == client.ApplicationUserId);
        if (clientUser?.Email is null) return;

        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? "Trener";
        var clientName = $"{client.FirstName} {client.LastName}".Trim().NullIfEmpty() ?? clientUser.Email;
        var meetRow = !string.IsNullOrWhiteSpace(session.MeetingUrl)
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Google Meet</td><td style=\"padding:8px 0;font-size:14px\"><a href=\"{session.MeetingUrl}\">{session.MeetingUrl}</a></td></tr>"
            : "";
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["TrainerName"] = trainerName,
            ["SessionType"] = sessionType.Name,
            ["SessionDate"] = session.StartTime.ToString("dddd, dd MMMM yyyy"),
            ["SessionTime"] = session.StartTime.ToString("HH:mm"),
            ["Duration"] = sessionType.DurationMinutes.ToString(),
            ["MeetingUrl"] = session.MeetingUrl ?? "",
            ["MeetingRow"] = meetRow
        };
        var (subject, html) = await emailTemplateService.RenderAsync("session-booked", vars);
        await emailService.SendAsync(clientUser.Email, clientName, subject, html);
    }

    private async Task SendCancellationEmailAsync(Session session, string? reason)
    {
        if (!await emailService.IsEnabledAsync()) return;
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(session.ClientId);
        if (client is null) return;
        if (!await notificationPrefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.SessionCancelledByTrainer)) return;
        var clientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == session.Client.ApplicationUserId);
        if (clientUser?.Email is null) return;

        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? "Trener";
        var clientName = $"{session.Client.FirstName} {session.Client.LastName}".Trim().NullIfEmpty() ?? clientUser.Email;
        var reasonRow = reason is not null
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Powód</td><td style=\"padding:8px 0;font-size:14px\">{reason}</td></tr>"
            : "";
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["TrainerName"] = trainerName,
            ["SessionType"] = session.SessionType.Name,
            ["SessionDate"] = session.StartTime.ToString("dddd, dd MMMM yyyy"),
            ["SessionTime"] = session.StartTime.ToString("HH:mm"),
            ["Reason"] = reason ?? "",
            ["ReasonRow"] = reasonRow
        };
        var (subject, html) = await emailTemplateService.RenderAsync("session-cancelled", vars);
        await emailService.SendAsync(clientUser.Email, clientName, subject, html);
    }

    private async Task SendClientCancelledToTrainerAsync(Session session, string? reason)
    {
        if (!await emailService.IsEnabledAsync()) return;
        if (!await notificationPrefs.IsEnabledAsync(session.TrainerUserId, NotificationTypes.ClientCancelledSession)) return;
        await using var db = dbFactory.CreateDbContext();
        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        if (trainer?.Email is null) return;
        var client = await db.Clients.FindAsync(session.ClientId);
        var clientName = client is not null ? $"{client.FirstName} {client.LastName}".Trim() : "Klient";
        var trainerName = $"{trainer.FirstName} {trainer.LastName}".Trim() is { Length: > 0 } n ? n : trainer.Email;
        var reasonRow = reason is not null
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Powód</td><td style=\"padding:8px 0;font-size:14px\">{reason}</td></tr>"
            : "";
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["TrainerName"] = trainerName,
            ["SessionType"] = session.SessionType.Name,
            ["SessionDate"] = session.StartTime.ToString("dd.MM.yyyy"),
            ["SessionTime"] = session.StartTime.ToString("HH:mm"),
            ["Reason"] = reason ?? "",
            ["ReasonRow"] = reasonRow
        };
        var (subject, html) = await emailTemplateService.RenderAsync("session-cancelled-by-client", vars);
        await emailService.SendAsync(trainer.Email, trainerName, subject, html);
    }

    private async Task SendRescheduleEmailAsync(Session session, DateTime oldTime)
    {
        if (!await emailService.IsEnabledAsync()) return;
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(session.ClientId);
        if (client is null) return;
        if (!await notificationPrefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.SessionRescheduled)) return;
        var clientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == client.ApplicationUserId);
        if (clientUser?.Email is null) return;
        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim() is { Length: > 0 } tn ? tn : trainer?.Email ?? "Trener";
        var clientName = $"{client.FirstName} {client.LastName}".Trim() is { Length: > 0 } cn ? cn : clientUser.Email;
        var sessionType = await db.SessionTypes.FindAsync(session.SessionTypeId);
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["TrainerName"] = trainerName,
            ["SessionType"] = sessionType?.Name ?? "",
            ["OldDate"] = oldTime.ToString("dd.MM.yyyy"),
            ["OldTime"] = oldTime.ToString("HH:mm"),
            ["NewDate"] = session.StartTime.ToString("dddd, dd MMMM yyyy"),
            ["NewTime"] = session.StartTime.ToString("HH:mm")
        };
        var (subject, html) = await emailTemplateService.RenderAsync("session-rescheduled", vars);
        await emailService.SendAsync(clientUser.Email, clientName, subject, html);
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
        TrainerName = trainers.GetValueOrDefault(s.TrainerUserId, "Trener"),
        StartTime = s.StartTime,
        Status = s.Status,
        Notes = s.Notes,
        CancellationReason = s.CancellationReason,
        MeetingUrl = s.MeetingUrl
    };
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
