using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

public class SmtpEmailService(
    IEmailSettingsService settingsService,
    IBrandingService brandingService,
    PlatformEmailProvider platformEmail,
    ILogger<SmtpEmailService> logger) : IEmailService
{
    private static bool OwnConfigured(Application.DTOs.EmailSettingsDto s) =>
        s.IsEnabled && !string.IsNullOrWhiteSpace(s.SmtpHost) && !string.IsNullOrWhiteSpace(s.FromAddress);

    /// <summary>
    /// Ustawienia wysyłki: własny SMTP trenera, jeśli go skonfigurował, a w instancji
    /// zarządzanej — serwer platformy z nazwą studia jako nadawcą i odpowiedziami do trenera.
    /// </summary>
    private async Task<Application.DTOs.EmailSettingsDto?> ResolveAsync()
    {
        var s = await settingsService.GetAsync();
        if (OwnConfigured(s)) return s;

        var platform = await platformEmail.GetStatusAsync();
        if (platform is null) return null;

        var fromName = s.FromName;
        if (string.IsNullOrWhiteSpace(fromName) || fromName == "PTScheduler")
        {
            try { fromName = (await brandingService.GetAsync()).CompanyName ?? "PTScheduler"; } catch { fromName = "PTScheduler"; }
        }
        // Wysyłkę robi Portal (bez hasła SMTP w instancji) — tu tylko nazwa nadawcy i adres odpowiedzi.
        return new Application.DTOs.EmailSettingsDto
        {
            IsEnabled = true,
            Provider = "Platform",
            FromName = fromName,
            ReplyTo = s.ReplyTo
        };
    }

    public async Task<bool> IsEnabledAsync() => await ResolveAsync() is not null;

    public async Task<string> GetDeliveryModeAsync()
    {
        var s = await ResolveAsync();
        return s is null ? "none" : s.Provider == "Platform" ? "platform" : "own";
    }

    private static MimeMessage NewMessage(Application.DTOs.EmailSettingsDto s)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(s.FromName, s.FromAddress));
        if (!string.IsNullOrWhiteSpace(s.ReplyTo) && MailboxAddress.TryParse(s.ReplyTo, out var reply))
            message.ReplyTo.Add(reply);
        return message;
    }

    public async Task SendAsync(string toAddress, string toName, string subject, string htmlBody)
    {
        var s = await ResolveAsync();
        if (s is null) return;

        if (s.Provider == "Platform")
        {
            await platformEmail.SendAsync(toAddress, toName, subject, htmlBody, s.FromName, s.ReplyTo);
            return;
        }

        var message = NewMessage(s);
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        await SendMessageAsync(s, message);
    }

    /// <summary>Dzienny limit i wykorzystanie poczty platformy (null przy własnym SMTP).</summary>
    public async Task<(int Limit, int SentToday)?> GetPlatformQuotaAsync()
    {
        var st = await platformEmail.GetStatusAsync(refresh: true);
        return st is null ? null : (st.DailyLimit, st.SentToday);
    }

    public async Task<(bool Success, string? Error)> TestAsync(string testAddress)
    {
        var s = await ResolveAsync();
        if (s is null) return (false, "Wysyłka e-maili nie jest skonfigurowana.");
        try
        {
            var branding = await brandingService.GetAsync();
            var html = TestEmailHtml(s.FromName, ThemeColor(branding.ThemeName));
            if (s.Provider == "Platform")
            {
                await platformEmail.SendAsync(testAddress, testAddress, "Test połączenia — PTScheduler", html, s.FromName, s.ReplyTo);
                return (true, null);
            }

            var message = NewMessage(s);
            message.To.Add(new MailboxAddress(testAddress, testAddress));
            message.Subject = "Test połączenia — PTScheduler";
            message.Body = new TextPart("html") { Text = html };

            await SendMessageAsync(s, message);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email test failed");
            return (false, ex.Message);
        }
    }

    private static async Task SendMessageAsync(Application.DTOs.EmailSettingsDto s, MimeMessage message)
    {
        using var client = new SmtpClient();
        var ssl = !s.UseTls ? SecureSocketOptions.None
            : s.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(s.SmtpHost, s.SmtpPort, ssl);
        if (!string.IsNullOrWhiteSpace(s.Login))
            await client.AuthenticateAsync(s.Login, s.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static string ThemeColor(string theme) => theme switch
    {
        "forest"   => "#16A34A", "sunset"  => "#EA580C", "crimson" => "#DC2626",
        "lavender" => "#9333EA", "slate"   => "#475569", "rose"    => "#E11D48",
        "teal"     => "#0D9488", "amber"   => "#D97706", "indigo"  => "#4F46E5",
        _          => "#0284C7"
    };

    private static string TestEmailHtml(string fromName, string accent) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:{accent};border-radius:8px 8px 0 0;padding:24px;text-align:center">
            <h2 style="color:white;margin:0;font-size:20px">✓ Połączenie działa poprawnie</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            <p style="color:#374151;font-size:15px">
              Ten email potwierdza, że konfiguracja SMTP w aplikacji <strong>{fromName}</strong>
              jest poprawna. Możesz teraz wysyłać powiadomienia do swoich klientów.
            </p>
            <p style="color:#6b7280;font-size:13px;margin-top:24px;padding-top:16px;border-top:1px solid #f3f4f6">
              Wiadomość wygenerowana automatycznie przez PTScheduler.
            </p>
          </div>
        </div>
        """;
}
