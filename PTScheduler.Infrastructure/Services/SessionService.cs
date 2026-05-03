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
    INotificationPreferencesService notificationPrefs,
    ILogger<SessionService> logger) : ISessionService
{
    public async Task<List<SessionDto>> GetSessionsAsync(DateTime from, DateTime to, string? trainerUserId = null, int? clientId = null)
    {
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
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? u.Id);

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<List<SessionDto>> GetPastSessionsAsync(string? trainerUserId = null, int? clientId = null, int count = 50)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
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
                u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? u.Id);
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
            try { await SendCancellationEmailAsync(session, cancellationReason); }
            catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila o anulowaniu (SessionId={Id})", session.Id); }
        }
    }

    public async Task RescheduleAsync(int id, DateTime newStartTime)
    {
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
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id);

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<List<SessionDto>> GetUpcomingAsync(string? trainerUserId = null, int? clientId = null, int count = 10)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
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
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id);

        return sessions.Select(s => MapToDto(s, trainers)).ToList();
    }

    public async Task<List<SessionDto>> GetAwaitingPackageAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
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
                u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? u.Id);
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
        var html = BuildBookingHtml(clientName, trainerName, sessionType.Name, session.StartTime, sessionType.DurationMinutes);
        await emailService.SendAsync(clientUser.Email, clientName, "Potwierdzenie rezerwacji wizyty", html);
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
        var html = BuildCancellationHtml(clientName, trainerName, session.SessionType.Name, session.StartTime, reason);
        await emailService.SendAsync(clientUser.Email, clientName, "Anulowanie wizyty", html);
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
        var html = BuildClientCancelledHtml(clientName, trainerName, session.SessionType.Name, session.StartTime, reason);
        await emailService.SendAsync(trainer.Email, trainerName, $"Klient anulował wizytę — {session.StartTime:dd.MM.yyyy HH:mm}", html);
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
        var html = BuildRescheduleHtml(clientName, trainerName, sessionType?.Name ?? "", oldTime, session.StartTime);
        await emailService.SendAsync(clientUser.Email, clientName, "Zmiana terminu wizyty", html);
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

    private static string BuildClientCancelledHtml(string clientName, string trainerName, string sessionType, DateTime startTime, string? reason) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:#9333EA;border-radius:8px 8px 0 0;padding:24px;text-align:center">
            <h2 style="color:white;margin:0;font-size:20px">Klient anulował wizytę</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            <p style="color:#374151;font-size:15px">Cześć <strong>{trainerName}</strong>!</p>
            <p style="color:#374151;font-size:15px">Klient <strong>{clientName}</strong> anulował wizytę:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{sessionType}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Data i godzina</td><td style="padding:8px 0;font-size:14px;font-weight:600">{startTime:dddd\, dd MMMM yyyy} o {startTime:HH:mm}</td></tr>
              {(reason is not null ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Powód</td><td style=\"padding:8px 0;font-size:14px\">{reason}</td></tr>" : "")}
            </table>
          </div>
        </div>
        """;

    private static string BuildRescheduleHtml(string clientName, string trainerName, string sessionType, DateTime oldTime, DateTime newTime) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:#0284C7;border-radius:8px 8px 0 0;padding:24px;text-align:center">
            <h2 style="color:white;margin:0;font-size:20px">Zmiana terminu wizyty</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            <p style="color:#374151;font-size:15px">Cześć <strong>{clientName}</strong>!</p>
            <p style="color:#374151;font-size:15px">Twój trener <strong>{trainerName}</strong> zmienił termin wizyty:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{sessionType}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Stary termin</td><td style="padding:8px 0;font-size:14px;text-decoration:line-through;color:#9ca3af">{oldTime:dd.MM.yyyy HH:mm}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Nowy termin</td><td style="padding:8px 0;font-size:14px;font-weight:600;color:#16A34A">{newTime:dddd\, dd MMMM yyyy} o {newTime:HH:mm}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Trener</td><td style="padding:8px 0;font-size:14px;font-weight:600">{trainerName}</td></tr>
            </table>
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
