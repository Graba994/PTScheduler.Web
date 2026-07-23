using System.Net;
using System.Net.Mail;

namespace PTScheduler.Portal.Services;

public class EmailService(SiteSettingsService settings, ILogger<EmailService> logger)
{
    public async Task<(bool Success, string? Error)> SendAsync(string toEmail, string subject, string htmlBody)
    {
        try
        {
            var s = await settings.GetAllAsync(
                SiteSettingsService.Keys.SmtpHost,
                SiteSettingsService.Keys.SmtpPort,
                SiteSettingsService.Keys.SmtpUser,
                SiteSettingsService.Keys.SmtpPass,
                SiteSettingsService.Keys.SmtpFrom,
                SiteSettingsService.Keys.SmtpFromName,
                SiteSettingsService.Keys.SmtpSsl);

            var host = s[SiteSettingsService.Keys.SmtpHost];
            if (string.IsNullOrWhiteSpace(host))
                return (false, "SMTP nie skonfigurowany — wejdź w /panel/email");

            var port = int.TryParse(s[SiteSettingsService.Keys.SmtpPort], out var p) ? p : 587;
            var user = s[SiteSettingsService.Keys.SmtpUser];
            var pass = s[SiteSettingsService.Keys.SmtpPass];
            var from = string.IsNullOrWhiteSpace(s[SiteSettingsService.Keys.SmtpFrom]) ? user : s[SiteSettingsService.Keys.SmtpFrom];
            var fromName = string.IsNullOrWhiteSpace(s[SiteSettingsService.Keys.SmtpFromName]) ? "PTScheduler" : s[SiteSettingsService.Keys.SmtpFromName];
            var ssl = s[SiteSettingsService.Keys.SmtpSsl] == "true";

            using var smtp = new SmtpClient(host)
            {
                Port = port,
                Credentials = new NetworkCredential(user, pass),
                EnableSsl = ssl,
                Timeout = 15000
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(from, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);

            await smtp.SendMailAsync(mail);
            logger.LogInformation("Email sent to {To}: {Subject}", toEmail, subject);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email send failed to {To}", toEmail);
            return (false, ex.Message);
        }
    }

    public string WelcomeEmailBody(string trainerName, string domain, string port, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #6366f1;">🎉 Witaj w PTScheduler!</h1>
            </div>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoja instancja PTScheduler została utworzona i jest gotowa do konfiguracji.</p>

            <div style="background: #f9fafb; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 12px;"><strong>Plan:</strong> {WebUtility.HtmlEncode(planName)}</div>
                <div style="margin-bottom: 12px;"><strong>Twoja domena:</strong> <a href="https://{domain}" style="color: #6366f1;">{domain}</a></div>
                <div><strong>Dostęp lokalny:</strong> <code>http://192.168.0.220:{port}</code></div>
            </div>

            <h2 style="color: #374151;">Co dalej?</h2>
            <ol style="line-height: 1.8;">
                <li>Otwórz swoją instancję pod powyższym linkiem</li>
                <li>Przejdź przez kreator konfiguracji (Setup)</li>
                <li>Utwórz konto administratora (Twoje główne konto)</li>
                <li>Dodaj klientów, ustaw grafik, konfiguruj branding</li>
            </ol>

            <p style="text-align: center; margin: 30px 0;">
                <a href="https://{domain}" style="background: #6366f1; color: white; padding: 12px 30px; border-radius: 8px; text-decoration: none; font-weight: 600;">
                    Otwórz moją instancję
                </a>
            </p>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                PTScheduler — Platforma dla trenerów personalnych
            </p>
        </body>
        </html>
        """;

    public string PendingPaymentEmailBody(string trainerName, string planName, decimal price) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #6366f1;">Witaj w PTScheduler!</h1>
            </div>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Dzięki za rejestrację. Wybrałeś plan <strong>{WebUtility.HtmlEncode(planName)}</strong> ({price:0} zł/mies.).</p>

            <div style="background: #fef3c7; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <strong>⏳ Twoja rejestracja czeka na opłacenie.</strong>
                <p style="margin: 10px 0 0;">Skontaktujemy się z Tobą w ciągu 24h z linkiem do płatności. Po opłaceniu Twoja instancja zostanie uruchomiona automatycznie.</p>
            </div>

            <p>W międzyczasie możesz zapoznać się z:</p>
            <ul style="line-height: 1.8;">
                <li>Dokumentacją: <a href="https://ptscheduler.pl/docs" style="color: #6366f1;">ptscheduler.pl/docs</a></li>
                <li>Przykładowym demo: <a href="https://demo.ptscheduler.pl" style="color: #6366f1;">demo.ptscheduler.pl</a></li>
            </ul>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Odpowiadaj na tego maila jeśli masz pytania.<br>
                PTScheduler
            </p>
        </body>
        </html>
        """;
}
