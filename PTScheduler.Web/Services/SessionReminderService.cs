using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Background service that sends a one-shot 24h reminder for upcoming Scheduled
/// sessions. Runs every hour. Persistent dedup via Session.ReminderSentAt
/// (survives restarts). Honours per-client opt-out via NotificationPreferences.
/// </summary>
public class SessionReminderService(IServiceScopeFactory scopeFactory, ILogger<SessionReminderService> logger) : BackgroundService
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Small initial delay so startup migrations / DI finish first.
        await Task.Delay(TimeSpan.FromMinutes(2), ct);

        while (!ct.IsCancellationRequested)
        {
            try { await SendRemindersAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Session reminder loop failed"); }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }

    private async Task SendRemindersAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        if (!await emailService.IsEnabledAsync())
        {
            logger.LogDebug("SMTP disabled — skipping reminders cycle.");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var prefs = scope.ServiceProvider.GetRequiredService<INotificationPreferencesService>();

        // 23-25h window with 1h cycle gives every session at least one chance to be picked up.
        // Persistent dedup via ReminderSentAt prevents double-sends if cycles overlap.
        var windowStart = DateTime.Now.AddHours(23);
        var windowEnd   = DateTime.Now.AddHours(25);

        var sessions = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= windowStart
                     && s.StartTime <  windowEnd
                     && s.Status   == SessionStatus.Scheduled
                     && s.ReminderSentAt == null)
            .ToListAsync();

        if (sessions.Count == 0) return;

        logger.LogInformation("Reminder cycle: {Count} candidate session(s) in 24h window.", sessions.Count);

        foreach (var session in sessions)
        {
            try
            {
                var clientUser = await userManager.FindByIdAsync(session.Client.ApplicationUserId);
                if (clientUser?.Email is null)
                {
                    logger.LogWarning("Session {Id}: client has no email — marking sent to avoid retry storm.", session.Id);
                    session.ReminderSentAt = DateTime.UtcNow;
                    continue;
                }

                // Honour client opt-out.
                if (!await prefs.IsEnabledAsync(clientUser.Id, NotificationTypes.SessionReminders))
                {
                    logger.LogInformation("Session {Id}: client {Email} opted out of reminders.", session.Id, clientUser.Email);
                    session.ReminderSentAt = DateTime.UtcNow;  // mark anyway so we don't re-check every hour
                    continue;
                }

                var trainer = await userManager.FindByIdAsync(session.TrainerUserId);
                var trainerName = ResolveName(trainer);

                var clientName = $"{session.Client.FirstName} {session.Client.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(clientName)) clientName = clientUser.Email!;

                var html = BuildReminderHtml(
                    clientName: clientName,
                    trainerName: trainerName,
                    sessionType: session.SessionType.Name,
                    startTime: session.StartTime,
                    durationMin: session.SessionType.DurationMinutes);

                await emailService.SendAsync(clientUser.Email, clientName, "Przypomnienie — jutro masz trening", html);

                session.ReminderSentAt = DateTime.UtcNow;
                logger.LogInformation("Sent 24h reminder for session {Id} to {Email}", session.Id, clientUser.Email);
            }
            catch (Exception ex)
            {
                // Leave ReminderSentAt null so next cycle retries (possibly succeeding once SMTP is fixed).
                logger.LogError(ex, "Failed to send reminder for session {Id}", session.Id);
            }
        }

        // Persist all the marker updates from this cycle in one round trip.
        await db.SaveChangesAsync();
    }

    private static string ResolveName(ApplicationUser? u)
    {
        if (u is null) return "Trener";
        var n = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrEmpty(n) ? (u.Email ?? "Trener") : n;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Pl) + s[1..];

    private static string BuildReminderHtml(string clientName, string trainerName, string sessionType, DateTime startTime, int durationMin)
    {
        var dateLabel = Capitalize(startTime.ToString("dddd, d MMMM yyyy", Pl));
        var timeLabel = startTime.ToString("HH:mm", Pl);

        return $@"<div style=""font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px"">
  <div style=""background:#0284C7;border-radius:8px 8px 0 0;padding:24px;text-align:center"">
    <h2 style=""color:white;margin:0;font-size:20px"">Przypomnienie — jutro trening 💪</h2>
  </div>
  <div style=""border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px"">
    <p style=""color:#374151;font-size:15px"">Cześć <strong>{clientName}</strong>!</p>
    <p style=""color:#374151;font-size:15px"">Przypominamy o jutrzejszym treningu. Do zobaczenia!</p>

    <div style=""background:#f3f4f6;border-radius:8px;padding:16px;margin:20px 0"">
      <p style=""margin:6px 0""><strong>📅 Data:</strong> {dateLabel}</p>
      <p style=""margin:6px 0""><strong>🕒 Godzina:</strong> {timeLabel}</p>
      <p style=""margin:6px 0""><strong>⏱️ Czas trwania:</strong> {durationMin} min</p>
      <p style=""margin:6px 0""><strong>💪 Trener:</strong> {trainerName}</p>
      <p style=""margin:6px 0""><strong>🏋️ Typ:</strong> {sessionType}</p>
    </div>

    <p style=""color:#6b7280;font-size:13px"">
      Jeśli nie możesz dotrzeć — <strong>daj znać trenerowi</strong> lub anuluj sesję w aplikacji.
    </p>

    <p style=""color:#9ca3af;font-size:11px;margin-top:24px;padding-top:16px;border-top:1px solid #f3f4f6;text-align:center"">
      Wiadomość automatyczna. Możesz wyłączyć przypomnienia w ustawieniach konta.
    </p>
  </div>
</div>";
    }
}
