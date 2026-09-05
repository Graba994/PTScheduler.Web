using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
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
        var smsService = scope.ServiceProvider.GetRequiredService<ISmsService>();
        var entitlements = scope.ServiceProvider.GetRequiredService<EntitlementService>();

        var emailEnabled = entitlements.IsAllowed("EmailReminders") && await emailService.IsEnabledAsync();
        var smsEnabled = entitlements.IsAllowed("SmsReminders") && await smsService.IsEnabledAsync();
        if (!emailEnabled && !smsEnabled)
        {
            logger.LogDebug("Email and SMS reminders both disabled — skipping reminders cycle.");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var prefs = scope.ServiceProvider.GetRequiredService<INotificationPreferencesService>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var maxSmsPerMonth = entitlements.Limit("MaxSmsPerMonth");

        // 23-25h window with 1h cycle gives every session at least one chance to be picked up.
        // Persistent dedup via ReminderSentAt prevents double-sends if cycles overlap.
        //
        // Okno liczymy zegarem ściennym, bo StartTime jest zegarem ściennym.
        // DateTime.Now dawałoby czas maszyny (w kontenerze: UTC), przez co okno
        // przesuwało się o offset strefy i przypomnienia szły o złej porze.
        var clock = scope.ServiceProvider.GetRequiredService<IAppClock>();
        var windowStart = clock.LocalNow.AddHours(23);
        var windowEnd   = clock.LocalNow.AddHours(25);

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

        var templateService = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();

        foreach (var session in sessions)
        {
            // Każdą sesję zapisujemy osobno, ZARAZ po jej obsłużeniu. Wcześniej
            // jeden zbiorczy SaveChanges na końcu cyklu oznaczał, że pojedynczy
            // błąd zapisu cofał WSZYSTKIE znaczniki tej partii — i już wysłane
            // maile leciały ponownie w kolejnym cyklu.
            var channelFailed = false;
            try
            {
                var clientUser = await userManager.FindByIdAsync(session.Client.ApplicationUserId);
                var optedIn = await prefs.IsEnabledAsync(session.Client.ApplicationUserId, NotificationTypes.SessionReminders);

                // Dostępność kanału dla TEJ sesji: kanał włączony w planie/konfiguracji,
                // klient ma kontakt i nie zrezygnował z przypomnień.
                var emailApplicable = emailEnabled && optedIn && clientUser?.Email is not null;
                var smsApplicable   = smsEnabled && optedIn && !string.IsNullOrWhiteSpace(session.Client.Phone);

                var trainerName = ResolveName(await userManager.FindByIdAsync(session.TrainerUserId));
                var clientName = $"{session.Client.FirstName} {session.Client.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(clientName)) clientName = clientUser?.Email ?? "Klient";

                var dateLabel = session.StartTime.ToString("dddd, d MMMM yyyy");
                if (dateLabel.Length > 0) dateLabel = char.ToUpper(dateLabel[0]) + dateLabel[1..];

                // ── E-MAIL ──────────────────────────────────────────────────
                // Kanał niedotyczący (wyłączony / brak adresu / rezygnacja) od razu
                // oznaczamy jako obsłużony, żeby nie blokował domknięcia sesji.
                if (session.ReminderEmailSentAt is null)
                {
                    if (!emailApplicable)
                    {
                        session.ReminderEmailSentAt = DateTime.UtcNow;
                    }
                    else
                    {
                        try
                        {
                            var meetRow = !string.IsNullOrWhiteSpace(session.MeetingUrl)
                                ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Google Meet</td><td style=\"padding:8px 0;font-size:14px\"><a href=\"{session.MeetingUrl}\">{session.MeetingUrl}</a></td></tr>"
                                : "";
                            var vars = new Dictionary<string, string>
                            {
                                ["ClientName"] = clientName,
                                ["TrainerName"] = trainerName,
                                ["SessionType"] = session.SessionType.Name,
                                ["SessionDate"] = dateLabel,
                                ["SessionTime"] = session.StartTime.ToString("HH:mm"),
                                ["Duration"] = session.SessionType.DurationMinutes.ToString(),
                                ["MeetingUrl"] = session.MeetingUrl ?? "",
                                ["MeetingRow"] = meetRow
                            };
                            var (subject, html) = await templateService.RenderAsync("session-reminder", vars);
                            await emailService.SendAsync(clientUser!.Email!, clientName, subject, html);
                            session.ReminderEmailSentAt = DateTime.UtcNow;
                            logger.LogInformation("Sent 24h email reminder for session {Id} to {Email}", session.Id, clientUser.Email);
                        }
                        catch (Exception ex)
                        {
                            channelFailed = true;
                            logger.LogWarning(ex, "Session {Id}: email reminder failed.", session.Id);
                            await SafeAuditAsync(auditLog, "EmailReminderFailed", session.Id, ex.Message, AuditSeverity.Warning);
                        }
                    }
                }

                // ── SMS ─────────────────────────────────────────────────────
                if (session.ReminderSmsSentAt is null)
                {
                    if (!smsApplicable)
                    {
                        session.ReminderSmsSentAt = DateTime.UtcNow;
                    }
                    else
                    {
                        var smsText = $"Przypomnienie: {dateLabel} o {session.StartTime:HH:mm} masz trening ({session.SessionType.Name}) u {trainerName}.";
                        var result = await smsService.SendReminderAsync(session.Client.Phone!, smsText, maxSmsPerMonth);
                        if (result.Success)
                        {
                            session.ReminderSmsSentAt = DateTime.UtcNow;
                            logger.LogInformation("Sent 24h SMS reminder for session {Id}", session.Id);
                        }
                        else if (result.QuotaExceeded)
                        {
                            // Limit miesięczny to nie jest błąd przejściowy — nie ma sensu
                            // ponawiać w kolejnych cyklach. Oznaczamy kanał jako obsłużony.
                            session.ReminderSmsSentAt = DateTime.UtcNow;
                            logger.LogWarning("Session {Id}: SMS reminder skipped — monthly quota exceeded.", session.Id);
                            await SafeAuditAsync(auditLog, "SmsReminderQuotaExceeded", session.Id,
                                $"Limit SMS/miesiąc wyczerpany (max {maxSmsPerMonth}).", AuditSeverity.Warning);
                        }
                        else
                        {
                            channelFailed = true;
                            logger.LogWarning("Session {Id}: SMS reminder failed — {Error}", session.Id, result.Error);
                            await SafeAuditAsync(auditLog, "SmsReminderFailed", session.Id, result.Error, AuditSeverity.Warning);
                        }
                    }
                }

                // ── Domknięcie sesji ────────────────────────────────────────
                if (channelFailed)
                {
                    session.ReminderAttempts++;
                    if (ReminderPolicy.ShouldGiveUp(session.ReminderAttempts))
                    {
                        // Poddajemy się — oznaczamy wszystko jako obsłużone, żeby nie
                        // ponawiać w nieskończoność, i zgłaszamy to do audytu.
                        session.ReminderEmailSentAt ??= DateTime.UtcNow;
                        session.ReminderSmsSentAt ??= DateTime.UtcNow;
                        session.ReminderSentAt = DateTime.UtcNow;
                        logger.LogError("Session {Id}: giving up on reminder after {Attempts} attempts.", session.Id, session.ReminderAttempts);
                        await SafeAuditAsync(auditLog, "ReminderGaveUp", session.Id,
                            $"Porzucono przypomnienie po {session.ReminderAttempts} próbach.", AuditSeverity.Error);
                    }
                }
                else if (session.ReminderEmailSentAt is not null && session.ReminderSmsSentAt is not null)
                {
                    // Oba kanały rozstrzygnięte — sesja w pełni obsłużona.
                    session.ReminderSentAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                // Nieoczekiwany błąd (np. odczyt preferencji/klienta). Znaczniki
                // już wysłanych kanałów są zachowane, więc nie zdublują się przy
                // ponowieniu. Zwiększamy licznik, żeby też podlegało limitowi prób.
                session.ReminderAttempts++;
                if (ReminderPolicy.ShouldGiveUp(session.ReminderAttempts))
                {
                    session.ReminderEmailSentAt ??= DateTime.UtcNow;
                    session.ReminderSmsSentAt ??= DateTime.UtcNow;
                    session.ReminderSentAt = DateTime.UtcNow;
                }
                logger.LogError(ex, "Failed to process reminder for session {Id}", session.Id);
                await SafeAuditAsync(auditLog, "ReminderCycleFailed", session.Id, ex.Message, AuditSeverity.Error);
            }

            // Zapis po każdej sesji — sukces jednego kanału jest utrwalony od razu
            // i żaden późniejszy błąd (innej sesji, audytu) go nie cofnie.
            try { await db.SaveChangesAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Failed to persist reminder markers for session {Id}", session.Id); }
        }
    }

    private static async Task SafeAuditAsync(IAuditLogService auditLog, string action, int sessionId, string? details, AuditSeverity severity)
    {
        try
        {
            await auditLog.LogAsync("system", "system", "System", action, "Session", sessionId.ToString(), details, severity);
        }
        catch { /* audyt to najlepszy-wysiłek; nie może wywrócić cyklu przypomnień */ }
    }

    private static string ResolveName(ApplicationUser? u)
    {
        if (u is null) return "Trener";
        var n = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrEmpty(n) ? (u.Email ?? "Trener") : n;
    }
}
