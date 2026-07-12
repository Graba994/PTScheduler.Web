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

                var templateService = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();
                var dateLabel = session.StartTime.ToString("dddd, d MMMM yyyy");
                if (dateLabel.Length > 0) dateLabel = char.ToUpper(dateLabel[0]) + dateLabel[1..];
                var vars = new Dictionary<string, string>
                {
                    ["ClientName"] = clientName,
                    ["TrainerName"] = trainerName,
                    ["SessionType"] = session.SessionType.Name,
                    ["SessionDate"] = dateLabel,
                    ["SessionTime"] = session.StartTime.ToString("HH:mm"),
                    ["Duration"] = session.SessionType.DurationMinutes.ToString()
                };
                var (subject, html) = await templateService.RenderAsync("session-reminder", vars);

                await emailService.SendAsync(clientUser.Email, clientName, subject, html);

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
}
