using System.Net;
using System.Net.Mail;

namespace PTScheduler.Guardian.Services;

/// <summary>
/// Pilnuje Portalu od strony danych:
/// <list type="bullet">
/// <item>gdy najnowsza kopia bazy Portalu jest starsza niż GUARDIAN_BACKUP_MAX_AGE_HOURS (domyślnie 26 h) — robi własną,</item>
/// <item>gdy Portal nie odpowiada dłużej niż GUARDIAN_ALERT_AFTER_MINUTES (domyślnie 10 min) — wysyła e-mail do administratora,
/// a po powrocie Portalu drugi, że już działa.</item>
/// </list>
/// Dane SMTP i adres administratora czyta z bazy Portalu (to, co ustawiono w Portalu), a zapasowo z konfiguracji kontenera Portalu.
/// </summary>
public sealed class BackupKeeper(UpgradeOrchestrator orchestrator, HealthWatcher health, ILogger<BackupKeeper> logger) : BackgroundService
{
    private readonly int _maxAgeHours = int.TryParse(Environment.GetEnvironmentVariable("GUARDIAN_BACKUP_MAX_AGE_HOURS"), out var h) && h > 0 ? h : 26;
    private readonly int _alertAfterMinutes = int.TryParse(Environment.GetEnvironmentVariable("GUARDIAN_ALERT_AFTER_MINUTES"), out var m) && m > 0 ? m : 10;

    public int MaxAgeHours => _maxAgeHours;
    public DateTime? LastAttemptAt { get; private set; }
    public string? LastResult { get; private set; }
    public bool LastOk { get; private set; } = true;
    public DateTime? DownAlertSentAt { get; private set; }
    public string? LastAlertError { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await CheckBackupAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Sprawdzenie kopii Portalu nie powiodło się."); }
            try { await CheckPortalAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Alarm o Portalu nie powiódł się."); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task CheckBackupAsync()
    {
        var newest = orchestrator.ListPortalBackups().Where(f => f.Source != "wgrana").Select(f => (DateTime?)f.CreatedAt).FirstOrDefault();
        var age = newest is null ? (TimeSpan?)null : DateTime.UtcNow - newest.Value;
        if (age < TimeSpan.FromHours(_maxAgeHours)) return;
        // Po nieudanej próbie nie męczymy bazy co 5 minut — ponawiamy co godzinę.
        if (LastAttemptAt is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(LastOk ? 6 : 1)) return;

        LastAttemptAt = DateTime.UtcNow;
        var reason = age is null ? "brak jakiejkolwiek kopii bazy Portalu" : $"Portal nie zrobił kopii od {age.Value.TotalHours:0} h";
        var (ok, message, _) = await orchestrator.CreatePortalBackupAsync("guardian", reason);
        LastOk = ok;
        LastResult = $"{reason} — {message}";
        orchestrator.PruneGuardianBackups();
        if (!ok) await SendAsync("Guardian: nie udało się zrobić kopii bazy Portalu",
            $"<p>{WebUtility.HtmlEncode(reason)}.</p><p>Guardian próbował zrobić własną kopię, ale: <b>{WebUtility.HtmlEncode(message)}</b></p>");
    }

    private async Task CheckPortalAsync()
    {
        if (!health.PortalHealthy && health.DownSinceUtc is { } since && DownAlertSentAt is null
            && DateTime.UtcNow - since > TimeSpan.FromMinutes(_alertAfterMinutes)
            && orchestrator.ActiveJobId is null)
        {
            var minutes = (int)(DateTime.UtcNow - since).TotalMinutes;
            var db = await orchestrator.PortalDbCredsAsync() is null ? "Baza Portalu też nie działa." : "Baza Portalu działa.";
            if (await SendAsync($"Portal nie odpowiada od {minutes} min",
                    $"<p>Portal nie odpowiada od {minutes} min (ostatni błąd: {WebUtility.HtmlEncode(health.LastError ?? "brak")}). {db}</p>" +
                    "<p>Aplikacje trenerów działają niezależnie. Otwórz panel Guardiana (port 9090): najpierw diagnostyka i „Przywróć poprzedni Portal”, " +
                    "a jeśli zepsuta jest baza — „Kopie Portalu” → Przywróć.</p>"))
                DownAlertSentAt = DateTime.UtcNow;
        }
        else if (health.PortalHealthy && DownAlertSentAt is not null)
        {
            await SendAsync("Portal znów działa", "<p>Portal odpowiada poprawnie.</p>");
            DownAlertSentAt = null;
        }
    }

    public async Task<bool> SendAsync(string subject, string html)
    {
        try
        {
            var s = await orchestrator.ReadPortalSettingsAsync("smtp_host", "smtp_port", "smtp_user", "smtp_pass", "smtp_from", "smtp_ssl", "admin_notification_email");
            var env = await orchestrator.PortalContainerEnvAsync();
            string Pick(string key, string envKey) => s.GetValueOrDefault(key) is { Length: > 0 } v ? v : env.GetValueOrDefault(envKey) ?? "";
            var host = Pick("smtp_host", "Email__SmtpHost");
            var to = Pick("admin_notification_email", "Portal__ContactEmail");
            if (host.Length == 0 || to.Length == 0)
            {
                LastAlertError = "Brak SMTP albo adresu administratora (Portal → E-mail i Zgłoszenia → powiadomienia).";
                logger.LogWarning("Nie wysłano alarmu „{Subject}”: {Reason}", subject, LastAlertError);
                return false;
            }
            var user = Pick("smtp_user", "Email__SmtpUser");
            var from = Pick("smtp_from", "Email__FromAddress") is { Length: > 0 } f ? f : user;
            using var smtp = new SmtpClient(host)
            {
                Port = int.TryParse(Pick("smtp_port", "Email__SmtpPort"), out var port) ? port : 587,
                Credentials = new NetworkCredential(user, Pick("smtp_pass", "Email__SmtpPassword")),
                EnableSsl = s.GetValueOrDefault("smtp_ssl", "true") != "false",
                Timeout = 15000
            };
            using var mail = new MailMessage
            {
                From = new MailAddress(from, "PTScheduler Guardian"),
                Subject = "[Guardian] " + subject,
                Body = $"<div style=\"font-family:-apple-system,Segoe UI,Roboto,sans-serif;max-width:600px;color:#0f172a\"><h2 style=\"color:#DC2626\">{WebUtility.HtmlEncode(subject)}</h2>{html}</div>",
                IsBodyHtml = true
            };
            mail.To.Add(to);
            await smtp.SendMailAsync(mail);
            LastAlertError = null;
            logger.LogInformation("Wysłano alarm „{Subject}” do {To}.", subject, to);
            return true;
        }
        catch (Exception ex)
        {
            LastAlertError = ex.Message;
            logger.LogWarning(ex, "Nie wysłano alarmu „{Subject}”.", subject);
            return false;
        }
    }
}
