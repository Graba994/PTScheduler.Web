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
    IWebRootPathProvider webRoot,
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

    public Task SendAsync(string toAddress, string toName, string subject, string htmlBody) =>
        SendAsync(toAddress, toName, subject, htmlBody, null);

    public async Task SendAsync(string toAddress, string toName, string subject, string htmlBody, IReadOnlyList<Application.Interfaces.EmailInlineImage>? images)
    {
        // Klienci z danych demo (konta testowe) — nic nie wysyłamy na zmyślone adresy.
        if (Application.Demo.DemoScenarios.IsDemoEmail(toAddress)) return;
        var s = await ResolveAsync();
        if (s is null) return;
        images = await WithLogoAsync(htmlBody, images);

        if (s.Provider == "Platform")
        {
            await platformEmail.SendAsync(toAddress, toName, subject, Domain.Rules.PlatformMailNote.Append(htmlBody, s.FromName, s.ReplyTo), s.FromName, s.ReplyTo, images);
            return;
        }

        var message = NewMessage(s);
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;
        message.Body = Body(htmlBody, images);

        try
        {
            await SendMessageAsync(s, message);
            // Po wcześniejszym błędzie zapisujemy, że skrzynka znowu działa (przy zwykłej pracy — bez zapisu).
            if (s.LastErrorAt is not null && (s.LastOkAt is null || s.LastOkAt < s.LastErrorAt))
                await settingsService.RecordOwnResultAsync(true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await settingsService.RecordOwnResultAsync(false, Friendly(ex));
            // Własna skrzynka nie działa — mail wychodzi awaryjnie przez serwer platformy (klient nie traci przypomnienia).
            if (await platformEmail.GetStatusAsync() is null) throw;
            logger.LogWarning(ex, "Własna skrzynka nie wysłała maila do {To} — wysyłam przez serwer platformy.", toAddress);
            await platformEmail.SendAsync(toAddress, toName, subject, Domain.Rules.PlatformMailNote.Append(htmlBody, s.FromName, s.FromAddress), s.FromName, s.FromAddress, images);
        }
    }

    /// <summary>Szablon z logo (<c>cid:brand-logo</c>) — dokładamy PNG logo do wiadomości.</summary>
    private async Task<IReadOnlyList<Application.Interfaces.EmailInlineImage>?> WithLogoAsync(string html, IReadOnlyList<Application.Interfaces.EmailInlineImage>? images)
    {
        if (!html.Contains(EmailLogo.Src, StringComparison.Ordinal) || images?.Any(i => i.ContentId == EmailLogo.ContentId) == true) return images;
        try
        {
            if (EmailLogo.Png(webRoot.WebRootPath, (await brandingService.GetAsync()).LogoPath) is not { } png) return images;
            return [.. images ?? [], new Application.Interfaces.EmailInlineImage(EmailLogo.ContentId, png, "image/png")];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Nie udało się dołączyć logo do maila.");
            return images;
        }
    }

    // HTML z obrazkami w środku wiadomości (multipart/related) — Gmail i Outlook pokazują je od razu.
    public static MimeEntity Body(string html, IReadOnlyList<Application.Interfaces.EmailInlineImage>? images)
    {
        if (images is not { Count: > 0 }) return new TextPart("html") { Text = html };
        var builder = new BodyBuilder { HtmlBody = html };
        foreach (var img in images)
        {
            var part = builder.LinkedResources.Add(img.ContentId, img.Data, ContentType.Parse(img.ContentType));
            part.ContentId = img.ContentId;
        }
        return builder.ToMessageBody();
    }

    public async Task<(bool Success, string? Error)> TestOwnAsync(Application.DTOs.EmailSettingsDto form, string testAddress)
    {
        if (string.IsNullOrWhiteSpace(form.SmtpHost) || string.IsNullOrWhiteSpace(form.FromAddress))
            return (false, "Uzupełnij serwer SMTP i adres nadawcy.");
        try
        {
            // Puste hasło w formularzu = zostaje zapisane wcześniej.
            var s = form;
            if (string.IsNullOrWhiteSpace(form.Password))
            {
                var saved = await settingsService.GetAsync();
                s = new Application.DTOs.EmailSettingsDto
                {
                    IsEnabled = true, Provider = form.Provider, SmtpHost = form.SmtpHost, SmtpPort = form.SmtpPort, UseTls = form.UseTls,
                    Login = form.Login, Password = saved.Password, FromAddress = form.FromAddress, FromName = form.FromName, ReplyTo = form.ReplyTo
                };
            }
            var branding = await brandingService.GetAsync();
            var message = NewMessage(s);
            message.To.Add(new MailboxAddress(testAddress, testAddress));
            message.Subject = "Test połączenia — PTScheduler";
            message.Body = new TextPart("html") { Text = TestEmailHtml(s.FromName, ThemeColor(branding.ThemeName)) };
            await SendMessageAsync(s, message);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Test własnej skrzynki nie powiódł się");
            return (false, Friendly(ex));
        }
    }

    /// <summary>Najczęstsze błędy SMTP po ludzku (zostawiamy też oryginalny komunikat).</summary>
    public static string Friendly(Exception ex) => ex switch
    {
        AuthenticationException => $"Serwer odrzucił login lub hasło. Przy Gmailu i Microsoft 365 użyj hasła aplikacji, nie hasła do konta. ({ex.Message})",
        SslHandshakeException => $"Nie udało się nawiązać szyfrowanego połączenia — sprawdź port (587 lub 465) i przełącznik TLS. ({ex.Message})",
        System.Net.Sockets.SocketException or TimeoutException => $"Brak połączenia z serwerem — sprawdź adres serwera i port. ({ex.Message})",
        SmtpCommandException sce when sce.StatusCode is SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed
            => $"Serwer nie pozwala wysyłać z tego adresu nadawcy — użyj adresu skrzynki albo dodaj alias. ({ex.Message})",
        NotSupportedException when ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            => $"Serwer nie przyjmuje logowania na tym porcie — wybierz port 587 z TLS albo 465, albo zostaw login pusty, jeśli serwer go nie wymaga. ({ex.Message})",
        _ => ex.Message
    };

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
        using var client = new SmtpClient { Timeout = 20000 };
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
