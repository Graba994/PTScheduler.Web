using System.Net;
using System.Net.Mail;

namespace PTScheduler.Portal.Services;

public class EmailService(SiteSettingsService settings, ILogger<EmailService> logger)
{
    /// <summary>Obrazek dołączony do wiadomości (w HTML: <c>src="cid:{ContentId}"</c>) — widać go bez pobierania z sieci.</summary>
    public sealed record InlineImage(string ContentId, byte[] Data, string ContentType);
    /// <summary>Załącznik e-maila (np. PDF dokumentu sprzedaży).</summary>
    public sealed record FileAttachment(string Name, byte[] Data, string ContentType);

    /// <summary>Znacznik w szablonach Portalu: przy wysyłce zamienia się w logo Hantly (obrazek w samej wiadomości).
    /// Maile trenerów przekazywane przez Portal go nie mają, więc nie dostają logo platformy.</summary>
    public const string LogoMarker = "<!--hantly-logo-->";
    private const string LogoCid = "hantly-logo";
    private static readonly Lazy<byte[]?> LogoPng = new(() =>
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var file = Path.Combine(root, "wwwroot", "brand", "hantly-logo.png");
            if (File.Exists(file)) return File.ReadAllBytes(file);
        }
        return null;
    });

    public static (string Html, IReadOnlyList<InlineImage>? Images) WithLogo(string html, IReadOnlyList<InlineImage>? images)
    {
        if (!html.Contains(LogoMarker, StringComparison.Ordinal)) return (html, images);
        if (LogoPng.Value is not { } png)
            return (html.Replace(LogoMarker, """<div style="text-align:center;margin:0 0 6px;font-weight:800;font-size:20px;color:#7c3aed;letter-spacing:-.01em">Hantly</div>"""), images);
        var img = $"""<div style="text-align:center;margin:0 0 6px"><img src="cid:{LogoCid}" width="132" height="43" alt="Hantly" style="display:inline-block;border:0;width:132px;height:auto"></div>""";
        return (html.Replace(LogoMarker, img), [.. images ?? [], new InlineImage(LogoCid, png, "image/png")]);
    }

    /// <param name="fromNameOverride">Nazwa nadawcy (np. studio trenera); adres zostaje adresem platformy.</param>
    /// <param name="replyTo">Adres odpowiedzi — przy poczcie trenera jego własny e-mail.</param>
    public async Task<(bool Success, string? Error)> SendAsync(string toEmail, string subject, string htmlBody,
        string? fromNameOverride = null, string? replyTo = null, string? toName = null, IReadOnlyList<InlineImage>? images = null,
        string? fromAddressOverride = null, IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyList<FileAttachment>? attachments = null)
    {
        try
        {
            (htmlBody, images) = WithLogo(htmlBody, images);
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
            var from = !string.IsNullOrWhiteSpace(fromAddressOverride) ? fromAddressOverride.Trim()
                : string.IsNullOrWhiteSpace(s[SiteSettingsService.Keys.SmtpFrom]) ? user : s[SiteSettingsService.Keys.SmtpFrom];
            var fromName = !string.IsNullOrWhiteSpace(fromNameOverride) ? fromNameOverride
                : string.IsNullOrWhiteSpace(s[SiteSettingsService.Keys.SmtpFromName]) ? "Hantly" : s[SiteSettingsService.Keys.SmtpFromName];
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
            if (images is { Count: > 0 })
            {
                // HTML z obrazkami w środku (multipart/related): nie zależy od publicznego adresu Portalu ani od blokady obrazków z sieci.
                mail.Body = "";
                var view = AlternateView.CreateAlternateViewFromString(htmlBody, System.Text.Encoding.UTF8, "text/html");
                foreach (var img in images)
                    view.LinkedResources.Add(new LinkedResource(new MemoryStream(img.Data), img.ContentType)
                    {
                        ContentId = img.ContentId,
                        TransferEncoding = System.Net.Mime.TransferEncoding.Base64
                    });
                mail.AlternateViews.Add(view);
            }
            foreach (var file in attachments ?? [])
                mail.Attachments.Add(new Attachment(new MemoryStream(file.Data), file.Name, file.ContentType));
            mail.To.Add(string.IsNullOrWhiteSpace(toName) ? new MailAddress(toEmail) : new MailAddress(toEmail, toName));
            if (!string.IsNullOrWhiteSpace(replyTo)) mail.ReplyToList.Add(new MailAddress(replyTo));
            // Np. In-Reply-To / References przy odpowiedzi z Poczty — wiadomość trafia do tego samego wątku u odbiorcy.
            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
                if (!string.IsNullOrWhiteSpace(value) && !value.Contains('\r') && !value.Contains('\n')) mail.Headers.Add(name, value);

            await smtp.SendMailAsync(mail);
            logger.LogInformation("Email sent to {To}: {Subject}", toEmail, subject);
            return (true, null);
        }
        catch (System.Net.Mail.SmtpFailedRecipientException ex)
        {
            // Serwer odbiorcy nie przyjął adresu (literówka, nieistniejąca skrzynka) — to nie awaria poczty.
            logger.LogWarning(ex, "Adres {To} odrzucony przez serwer poczty.", toEmail);
            return (false, RecipientRejected + ": " + ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email send failed to {To}", toEmail);
            return (false, ex.Message);
        }
    }

    /// <summary>Początek błędu, gdy serwer odbiorcy odrzucił adres (a nie gdy nasza poczta nie działa).</summary>
    public const string RecipientRejected = "Adres odrzucony";

    public string WelcomeEmailBody(string trainerName, string domain, string port, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #6366f1;">🎉 Witaj w Hantly!</h1>
            </div>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoja instancja Hantly została utworzona i jest gotowa do konfiguracji.</p>

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
                Hantly — Platforma dla trenerów personalnych
            </p>
        </body>
        </html>
        """;

    /// <summary>Koniec okresu próbnego z podpiętą kartą: kiedy i ile pobierzemy oraz jak zrezygnować.</summary>
    public string TrialEndingWithCardEmailBody(string trainerName, int daysLeft, decimal amount, bool yearly, string planName, string domain) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <h1 style="color: #7c3aed; font-size: 1.4rem;">Za {daysLeft} dni kończy się okres próbny</h1>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>nic nie musisz robić — aplikacja działa dalej. Po okresie próbnym pobierzemy z karty <strong>{amount:0.##} zł</strong>
               za plan {WebUtility.HtmlEncode(planName)} ({(yearly ? "za cały rok" : "za miesiąc")}).</p>
            <p>Chcesz zmienić plan, przejść na rozliczenie {(yearly ? "miesięczne" : "roczne (taniej)")} albo zrezygnować? W aplikacji:
               <strong>Zarządzanie → Plan → Zarządzaj subskrypcją</strong>. Rezygnacja działa od razu i bez dodatkowych pytań.</p>
            <p style="text-align: center; margin: 28px 0;">
                <a href="https://{domain}/admin/upgrade" style="background: #7c3aed; color: white; padding: 12px 26px; border-radius: 8px; text-decoration: none; font-weight: 600;">Zarządzaj subskrypcją</a>
            </p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Masz pytania? Odpisz na tego maila.<br>Hantly</p>
        </body>
        </html>
        """;

    public string TrialWarningEmailBody(string trainerName, int daysLeft) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #f59e0b;">Twoj okres probny konczy sie za {daysLeft} dni</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoj darmowy okres probny Hantly konczy sie za <strong>{daysLeft} dni</strong>.</p>
            <p>Aby kontynuowac korzystanie z platformy, dodaj metode platnosci lub wybierz plan subskrypcji.</p>
            <p>Jesli nie podejmiesz zadnej akcji, Twoje konto zostanie automatycznie zawieszone po zakonczeniu okresu probnego.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                Hantly
            </p>
        </body>
        </html>
        """;

    public string PaymentReceivedEmailBody(string trainerName, decimal amount, string invoiceNumber) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #10b981;">Platnosc otrzymana</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Potwierdzamy otrzymanie platnosci.</p>
            <div style="background: #f0fdf4; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 8px;"><strong>Kwota:</strong> {amount:0.00} PLN</div>
                <div><strong>Faktura:</strong> {WebUtility.HtmlEncode(invoiceNumber)}</div>
            </div>
            <p>Dziekujemy za korzystanie z Hantly!</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Hantly</p>
        </body>
        </html>
        """;

    public string PaymentFailedEmailBody(string trainerName, decimal amount, string invoiceNumber) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #ef4444;">Platnosc nieudana</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Nie udalo sie pobrac platnosci za Twoja subskrypcje PTScheduler.</p>
            <div style="background: #fef2f2; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 8px;"><strong>Kwota:</strong> {amount:0.00} PLN</div>
                <div><strong>Faktura:</strong> {WebUtility.HtmlEncode(invoiceNumber)}</div>
            </div>
            <p>Prosimy o aktualizacje metody platnosci. Jesli platnosc nie zostanie zrealizowana, Twoje konto moze zostac zawieszone.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Hantly</p>
        </body>
        </html>
        """;

    public string SuspensionEmailBody(string trainerName, string reason) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #ef4444;">Konto zawieszone</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoje konto Hantly zostalo zawieszone.</p>
            <div style="background: #fef2f2; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <strong>Powod:</strong> {WebUtility.HtmlEncode(reason)}
            </div>
            <p>Aby przywrocic dostep, skontaktuj sie z nami lub odnow subskrypcje.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Hantly</p>
        </body>
        </html>
        """;

    public string InactivityWarningEmailBody(string trainerName, int inactiveDays, int daysUntilSuspend) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #f59e0b;">Twoje konto jest nieaktywne</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Zauazylismy, ze Twoje konto Hantly nie bylo uzywane od <strong>{inactiveDays} dni</strong>.</p>
            <p>Jesli nie zalogujesz sie w ciagu <strong>{daysUntilSuspend} dni</strong>, Twoje konto zostanie automatycznie zawieszone.</p>
            <p>Wystarczy sie zalogowac, zeby konto pozostalo aktywne.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                Hantly
            </p>
        </body>
        </html>
        """;

    public string CleanupWarningEmailBody(string trainerName, int daysLeft) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #ef4444;">Twoje konto zostanie usuniete</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoje konto Hantly jest zawieszone i zostanie <strong>trwale usuniete za {daysLeft} dni</strong>.</p>
            <p>Po usunieciu wszystkie Twoje dane (klienci, sesje, pakiety, kursy) zostana bezpowrotnie skasowane.</p>
            <p>Aby temu zapobiec, skontaktuj sie z nami lub odnow subskrypcje.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                Hantly
            </p>
        </body>
        </html>
        """;

    public string CleanupDeletionEmailBody(string trainerName, int suspendedDays) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #ef4444;">Konto zostalo usuniete</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoje konto Hantly bylo zawieszone przez {suspendedDays} dni i zostalo trwale usuniete.</p>
            <p>Wszystkie dane zostaly skasowane. Jesli chcesz wrocic do Hantly, mozesz zalozyc nowe konto w kazdej chwili.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Hantly — platforma dla trenerów personalnych
            </p>
        </body>
        </html>
        """;

    public string GraceExtendedEmailBody(string trainerName, int graceDays) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #10b981;">Przedluzono okres ochrony konta</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Administrator przedluzyl okres ochrony Twojego konta o <strong>{graceDays} dni</strong>.</p>
            <p>W tym czasie Twoje dane sa bezpieczne i nie zostana usuniete. Prosimy o uregulowanie subskrypcji w tym okresie.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                Hantly
            </p>
        </body>
        </html>
        """;

    public string RegistrationPendingReviewEmailBody(string trainerName, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #6366f1;">Dziekujemy za rejestracje!</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoje zgloszenie w Hantly zostalo przyjete i oczekuje na weryfikacje.</p>

            <div style="background: #eff6ff; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <strong>Plan:</strong> {WebUtility.HtmlEncode(planName)}<br>
                <strong>Status:</strong> Oczekuje na akceptacje
            </div>

            <p>Nasz zespol zweryfikuje Twoje zgloszenie i skontaktuje sie z Toba najszybciej jak to mozliwe. Po akceptacji otrzymasz email z danymi dostepu do Twojej instancji.</p>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                Hantly
            </p>
        </body>
        </html>
        """;

    public string AdminNewRegistrationEmailBody(string trainerName, string email, string companyName, string planName, string phone, int tenantId) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #f59e0b;">Nowa rejestracja trenera</h1>
            </div>
            <p>Nowy trener zarejestrował się w Hantly i czeka na akceptację.</p>

            <div style="background: #fef3c7; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 8px;"><strong>Imie i nazwisko:</strong> {WebUtility.HtmlEncode(trainerName)}</div>
                <div style="margin-bottom: 8px;"><strong>Email:</strong> {WebUtility.HtmlEncode(email)}</div>
                <div style="margin-bottom: 8px;"><strong>Firma:</strong> {WebUtility.HtmlEncode(companyName)}</div>
                <div style="margin-bottom: 8px;"><strong>Telefon:</strong> {WebUtility.HtmlEncode(phone ?? "—")}</div>
                <div><strong>Plan:</strong> {WebUtility.HtmlEncode(planName)}</div>
            </div>

            <p style="text-align: center; margin: 30px 0;">
                <a href="/panel/tenants/{tenantId}" style="background: #6366f1; color: white; padding: 12px 30px; border-radius: 8px; text-decoration: none; font-weight: 600;">
                    Przejdz do akceptacji
                </a>
            </p>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Hantly Portal</p>
        </body>
        </html>
        """;

    /// <summary>Po publikacji z kreatora: aplikacja trenera działa (bez akceptacji admina).</summary>
    public string AppReadyEmailBody(string trainerName, string companyName, string appUrl, string loginEmail, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #7c3aed; margin-bottom: 4px;">Twoja aplikacja działa!</h1>
                <p style="color: #6b7280; margin-top: 0;">{WebUtility.HtmlEncode(companyName)}</p>
            </div>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>aplikacja jest gotowa — z kolorem, stroną główną i ofertą, które ustawiono w kreatorze. Klienci mogą się już zapisywać.</p>

            <div style="background: #f5f3ff; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 10px;"><strong>Adres aplikacji:</strong> <a href="{appUrl}" style="color: #7c3aed;">{WebUtility.HtmlEncode(appUrl)}</a></div>
                <div style="margin-bottom: 10px;"><strong>Login:</strong> {WebUtility.HtmlEncode(loginEmail)} (hasło ustawione w kreatorze)</div>
                <div><strong>Plan:</strong> {WebUtility.HtmlEncode(planName)}</div>
            </div>

            <h2 style="color: #374151; font-size: 1.1rem;">Trzy rzeczy na dobry start</h2>
            <ol style="line-height: 1.8;">
                <li>Utwórz profil trenera (Zarządzanie → Konta) i ustaw na nim godziny pracy — klienci zobaczą wolne terminy.</li>
                <li>Dodaj pierwszego klienta albo wyślij mu link do swojej strony.</li>
                <li>Zainstaluj aplikację na telefonie (w przeglądarce: „Dodaj do ekranu głównego”).</li>
            </ol>

            <p style="text-align: center; margin: 30px 0;">
                <a href="{appUrl}/Account/Login" style="background: #7c3aed; color: white; padding: 12px 30px; border-radius: 8px; text-decoration: none; font-weight: 600;">
                    Otwórz moją aplikację
                </a>
            </p>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                Hantly — platforma dla trenerów personalnych
            </p>
        </body>
        </html>
        """;

    /// <summary>Aplikacja zbudowana przez admina dla trenera: link do ustawienia hasła (/setup z wypełnionymi danymi).</summary>
    public string AppBuiltForYouEmailBody(string trainerName, string companyName, string appUrl, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #7c3aed; margin-bottom: 4px;">Twoja aplikacja czeka!</h1>
                <p style="color: #6b7280; margin-top: 0;">{WebUtility.HtmlEncode(companyName)}</p>
            </div>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>przygotowaliśmy dla Ciebie aplikację — z kolorem, stroną główną i ofertą. Został jeden krok: ustaw hasło i wejdź.</p>
            <p style="text-align: center; margin: 30px 0;">
                <a href="{appUrl}/setup" style="background: #7c3aed; color: white; padding: 12px 30px; border-radius: 8px; text-decoration: none; font-weight: 600;">Ustaw hasło i wejdź</a>
            </p>
            <p style="color: #6b7280; font-size: 0.9rem;">Adres aplikacji: <a href="{appUrl}">{WebUtility.HtmlEncode(appUrl)}</a> · plan {WebUtility.HtmlEncode(planName)}</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Masz pytania? Odpisz na tego maila.<br>Hantly</p>
        </body>
        </html>
        """;

    /// <summary>Zgłoszenie z kreatora czeka w kolejce (limit, tryb akceptacji albo chwilowy problem serwera).</summary>
    public string AppQueuedEmailBody(string trainerName, string companyName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <h1 style="color: #7c3aed; font-size: 1.4rem;">Twoja aplikacja jest w kolejce</h1>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>aplikacja <strong>{WebUtility.HtmlEncode(companyName)}</strong> jest zbudowana i czeka na uruchomienie. Wszystkie ustawienia z kreatora są zapisane —
               nic nie trzeba wpisywać od nowa. Gdy tylko ją uruchomimy, dostaniesz e-mail z adresem. Zwykle trwa to kilka godzin.</p>
            <p>Okres próbny liczymy od dnia uruchomienia aplikacji.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Masz pytania? Odpisz na tego maila.<br>Hantly</p>
        </body>
        </html>
        """;

    /// <summary>
    /// Firmowa wiadomość od zespołu platformy (szkice Leo: ryzyko odejścia, zgłoszenia, opinie): logo, biała karta
    /// z paskiem w kolorze marki, treść z akapitami i listami, stały podpis zespołu i stopka. Podpis z treści
    /// („Pozdrawiamy, Zespół Hantly”) usuwamy — dodaje go szablon, więc każda wiadomość kończy się tak samo.
    /// </summary>
    public static string BrandedMessage(string body, string brand = "Hantly")
    {
        var b = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(brand) ? "Hantly" : brand.Trim());
        var blocks = StripSignature(body.Replace("\r", "").Trim(), brand)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var html = new System.Text.StringBuilder();
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries);
            if (lines.All(l => l.StartsWith("- ") || l.StartsWith("• ") || l.StartsWith("* ")))
            {
                html.Append("""<ul style="margin:0 0 14px;padding-left:20px">""");
                foreach (var l in lines) html.Append($"""<li style="margin:0 0 6px">{Inline(l[2..])}</li>""");
                html.Append("</ul>");
            }
            else html.Append($"""<p style="margin:0 0 14px">{string.Join("<br>", lines.Select(Inline))}</p>""");
        }
        // Zajawka w skrzynce: pierwsze zdania po powitaniu.
        var lead = (blocks.Length > 1 ? blocks[1] : blocks.FirstOrDefault() ?? "").Replace("\n", " ");
        var preheader = WebUtility.HtmlEncode(lead.Length > 110 ? lead[..110] + "…" : lead);
        return $"""
            <!DOCTYPE html>
            <html lang="pl">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"></head>
            <body style="margin:0;padding:0;background:#f4f2fb;-webkit-text-size-adjust:100%">
            <span style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent">{preheader}</span>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f2fb">
            <tr><td align="center" style="padding:28px 12px 32px">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px">
                <tr><td align="center" style="padding:0 0 18px">{LogoMarker}</td></tr>
                <tr><td style="background:#ffffff;border-radius:18px;overflow:hidden;border:1px solid #ebe7f6">
                  <div style="height:5px;background:#7c3aed;background-image:linear-gradient(90deg,#7c3aed,#a855f7)"></div>
                  <div style="padding:30px 32px 6px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,sans-serif;font-size:15px;line-height:1.6;color:#1f2937">
                    {html}
                  </div>
                  <div style="padding:6px 32px 28px">
                    <table role="presentation" cellpadding="0" cellspacing="0" style="border-top:1px solid #efecf7;width:100%">
                      <tr>
                        <td style="padding-top:16px;width:44px;vertical-align:top">
                          <div style="width:40px;height:40px;border-radius:12px;background:#ede9fe;color:#7c3aed;font:800 18px/40px -apple-system,'Segoe UI',Arial,sans-serif;text-align:center">{b[..1]}</div>
                        </td>
                        <td style="padding:16px 0 0 12px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,sans-serif;font-size:14px;line-height:1.45;color:#1f2937">
                          <b>Zespół {b}</b><br>
                          <span style="color:#6b6680">Odpisz na tę wiadomość — odpowiadamy zwykle w ciągu jednego dnia roboczego.</span>
                        </td>
                      </tr>
                    </table>
                  </div>
                </td></tr>
                <tr><td align="center" style="padding:18px 12px 0;font-family:-apple-system,'Segoe UI',Arial,sans-serif;font-size:12px;line-height:1.5;color:#8b8799">
                  {b} · aplikacja dla trenerów personalnych · <a href="https://hantly.pl" style="color:#7c3aed;text-decoration:none">hantly.pl</a><br>
                  Dostajesz tę wiadomość, bo prowadzisz studio w {b}.
                </td></tr>
              </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    /// <summary>Tekst akapitu: bezpieczny HTML i klikalne adresy (https://…, www.…).</summary>
    private static string Inline(string text) =>
        System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlEncode(text), @"\b(https?://[^\s<]+|www\.[^\s<]+)",
            m =>
            {
                var url = m.Value.TrimEnd('.', ',', ')', ';');
                var rest = m.Value[url.Length..];
                var href = url.StartsWith("www.") ? "https://" + url : url;
                return $"""<a href="{href}" style="color:#7c3aed">{url}</a>{rest}""";
            });

    /// <summary>Usuwa końcowy podpis zespołu („Pozdrawiamy,\nZespół Hantly”) — szablon dodaje własny.</summary>
    public static string StripSignature(string body, string brand = "Hantly")
    {
        var lines = body.Replace("\r", "").TrimEnd().Split('\n').ToList();
        bool IsSign(string l)
        {
            var t = l.Trim().TrimEnd(',', '.', '!').ToLowerInvariant();
            return t.Length == 0 || t is "pozdrawiamy" or "pozdrawiam" or "serdecznie pozdrawiamy" or "z pozdrowieniami" or "dziękujemy" or "do usłyszenia"
                || t == $"zespół {brand.ToLowerInvariant()}" || t == "zespół hantly" || t == $"— zespół {brand.ToLowerInvariant()}";
        }
        var cut = lines.Count;
        while (cut > 0 && IsSign(lines[cut - 1])) cut--;
        // Odcinamy tylko, gdy na końcu był podpis zespołu (samo „Dziękujemy!” w treści zostaje).
        var tail = string.Join(" ", lines.Skip(cut)).ToLowerInvariant();
        return tail.Contains("zespół") ? string.Join("\n", lines.Take(cut)).TrimEnd() : body.TrimEnd();
    }

    /// <summary>Podgląd w panelu: logo jako obrazek w samym HTML (data:), jak w skrzynce odbiorcy.</summary>
    public static string ForPreview(string html) =>
        LogoPng.Value is { } png
            ? html.Replace(LogoMarker, $"""<img src="data:image/png;base64,{Convert.ToBase64String(png)}" width="132" alt="Hantly" style="display:inline-block;border:0;width:132px;height:auto">""")
            : html.Replace(LogoMarker, """<div style="font-weight:800;font-size:20px;color:#7c3aed">Hantly</div>""");

    private static string Shell(string inner) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 560px; margin: 0 auto; padding: 20px; color: #1f2937; line-height: 1.55;">
            <!--hantly-logo-->
        {inner}
            <p style="color: #6b7280; font-size: 0.875rem; margin-top: 28px;">Coś nie działa albo masz pytanie? Odpisz na tego maila — czytamy wszystko.<br>Hantly</p>
        </body>
        </html>
        """;

    private static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;

    private static string Btn(string url, string text) =>
        $"""<p><a href="{url}" style="display: inline-block; background: #7c3aed; color: #fff; padding: 11px 20px; border-radius: 10px; text-decoration: none; font-weight: 600;">{WebUtility.HtmlEncode(text)}</a></p>""";

    /// <summary>E-maile powitalne po uruchomieniu aplikacji: dzień 1 (start), 3 (klienci), 7 (podsumowanie i polecenia).</summary>
    public (string Subject, string Body) OnboardingEmail(int stage, string name, string company, string appUrl,
        int? clients, int? sessions, string referralLink)
    {
        var n = WebUtility.HtmlEncode(name);
        var app = WebUtility.HtmlEncode(appUrl);
        var invite = WebUtility.HtmlEncode($"Cześć! Od teraz zapisujesz się na treningi u mnie przez aplikację: {appUrl} — wybierasz termin, płacisz online i dostajesz przypomnienie. Do zobaczenia!");
        switch (stage)
        {
            case 1:
                return ("Twoja aplikacja działa — 3 rzeczy na dziś", Shell($"""
                    <h1 style="color: #7c3aed; font-size: 1.35rem;">{WebUtility.HtmlEncode(company)} jest online 🎉</h1>
                    <p>Cześć {n},</p>
                    <p>Twoja aplikacja działa pod <a href="{app}" style="color: #7c3aed;">{app}</a>. Żeby pierwsi klienci mogli się zapisać, zrób dziś trzy rzeczy — każda zajmuje kilka minut:</p>
                    <ol>
                        <li><b>Ustaw godziny pracy</b> — w menu „Godziny pracy”. Klienci widzą tylko wolne terminy.</li>
                        <li><b>Sprawdź cennik</b> — oferta z kreatora już jest, popraw nazwy i ceny, jeśli trzeba.</li>
                        <li><b>Zaproś 3 pierwszych klientów</b> — wyślij im link do aplikacji (gotowy tekst niżej).</li>
                    </ol>
                    {Btn(appUrl + "/Account/Login", "Otwórz aplikację")}
                    <p style="background: #f5f3ff; border-radius: 10px; padding: 12px 14px; font-size: 0.92rem;">{invite}</p>
                    """));
            case 2 when (clients ?? 0) < 3:
                return ("Jak zaprosić klientów w 2 minuty", Shell($"""
                    <h1 style="color: #7c3aed; font-size: 1.35rem;">Zaproś pierwszych klientów</h1>
                    <p>Cześć {n},</p>
                    <p>{(clients is > 0 ? $"Masz już {clients} {Plural(clients.Value, "klienta", "klientów", "klientów")} w aplikacji — świetny start." : "W aplikacji nie ma jeszcze klientów.")}
                       Najszybciej działa krótka wiadomość na WhatsAppie albo SMS-em do osób, z którymi już trenujesz. Skopiuj i wyślij:</p>
                    <p style="background: #f5f3ff; border-radius: 10px; padding: 12px 14px; font-size: 0.92rem;">{invite}</p>
                    <p>Klientów możesz też dodać ręcznie w aplikacji.</p>
                    {Btn(appUrl + "/Account/Login", "Przejdź do aplikacji")}
                    """));
            case 2:
                return ($"Masz już {clients} {Plural(clients ?? 0, "klienta", "klientów", "klientów")} — co dalej", Shell($"""
                    <h1 style="color: #7c3aed; font-size: 1.35rem;">Dobra robota, {n}!</h1>
                    <p>Masz już <b>{clients} {Plural(clients ?? 0, "klienta", "klientów", "klientów")}</b>{(sessions is > 0 ? $" i <b>{sessions} {Plural(sessions.Value, "trening", "treningi", "treningów")}</b>" : "")} w aplikacji. Dwie rzeczy, które oszczędzają najwięcej czasu:</p>
                    <ul>
                        <li><b>Płatności online</b> — klienci płacą BLIK-iem przy zapisie, nie musisz pilnować przelewów.</li>
                        <li><b>Pakiety treningów</b> — klient płaci z góry za kilka treningów, a aplikacja sama odlicza wejścia.</li>
                    </ul>
                    {Btn(appUrl + "/Account/Login", "Ustaw płatności")}
                    """));
            default:
                return ("Tydzień z aplikacją — podsumowanie", Shell($"""
                    <h1 style="color: #7c3aed; font-size: 1.35rem;">Pierwszy tydzień za Tobą</h1>
                    <p>Cześć {n},</p>
                    <p>{(clients is null
                        ? "Jak minął pierwszy tydzień z aplikacją?"
                        : $"W aplikacji {WebUtility.HtmlEncode(company)} masz <b>{clients} {Plural(clients.Value, "klienta", "klientów", "klientów")}</b> i <b>{sessions ?? 0} {Plural(sessions ?? 0, "trening", "treningi", "treningów")}</b>.")}
                       {((clients ?? 0) >= 3 ? "Tak trzymaj!" : "Jeśli coś Cię blokuje, odpisz — pomożemy ustawić wszystko razem.")}</p>
                    <h2 style="font-size: 1.05rem; margin-top: 22px;">Znasz kogoś, kto też prowadzi treningi?</h2>
                    <p>Wyślij swój link. Polecona osoba dostanie <b>30 dni dłużej za darmo</b>, a Ty <b>miesiąc gratis</b> po jej pierwszej płatności.</p>
                    <p style="background: #f5f3ff; border-radius: 10px; padding: 12px 14px; font-size: 0.92rem; word-break: break-all;"><a href="{WebUtility.HtmlEncode(referralLink)}" style="color: #7c3aed;">{WebUtility.HtmlEncode(referralLink)}</a></p>
                    """));
        }
    }

    /// <summary>Nowe zgłoszenie błędu z aplikacji trenera — do administratora.</summary>
    public string AdminBugReportEmailBody(int id, string tenant, string author, string? page, string comment, bool hasScreenshot, string link) => Shell($"""
        <h1 style="color: #dc2626; font-size: 1.3rem;">Zgłoszenie błędu #{id}</h1>
        <p><b>{WebUtility.HtmlEncode(tenant)}</b> · {WebUtility.HtmlEncode(author)}{(string.IsNullOrWhiteSpace(page) ? "" : $" · <code>{WebUtility.HtmlEncode(page)}</code>")}</p>
        <p style="background: #fef2f2; border-radius: 10px; padding: 12px 14px; white-space: pre-wrap;">{WebUtility.HtmlEncode(comment)}</p>
        <p style="color: #6b7280; font-size: 0.9rem;">{(hasScreenshot ? "Zrzut ekranu i szczegóły przeglądarki są w Panelu." : "Bez zrzutu ekranu — szczegóły przeglądarki są w Panelu.")}</p>
        {Btn(link, "Otwórz zgłoszenie")}
        """);

    /// <summary>„Wyślij mi link” ze strony głównej: wraca do kreatora z wpisaną nazwą studia.</summary>
    public string LaterLinkEmailBody(string? company, string link) => Shell($"""
        <h1 style="color: #7c3aed; font-size: 1.35rem;">Twoja aplikacja czeka</h1>
        <p>Cześć! Oto link do kreatora ze strony hantly.pl. Zbudowanie aplikacji zajmie kilka minut: nazwa{(string.IsNullOrWhiteSpace(company) ? "" : $" (<b>{WebUtility.HtmlEncode(company.Trim())}</b>)")}, wygląd, oferta i gotowe. Okres próbny jest za darmo.</p>
        {Btn(link, "Zbuduj swoją aplikację")}
        <p style="color: #6b7280; font-size: 0.9rem;">Ta wiadomość nie jest dla Ciebie? Wystarczy ją zignorować — więcej nie napiszemy.</p>
        """);

    /// <summary>Do osoby, która zgłosiła błąd, gdy administrator oznaczy go jako naprawiony.</summary>
    public string BugFixedEmailBody(string? name, string comment, string? note)
    {
        var quote = comment.Length > 300 ? comment[..297] + "…" : comment;
        return Shell($"""
            <h1 style="color: #059669; font-size: 1.3rem;">Naprawione — dzięki za zgłoszenie</h1>
            <p>{(string.IsNullOrWhiteSpace(name) ? "Cześć!" : $"Cześć {WebUtility.HtmlEncode(name.Trim())},")}</p>
            <p>błąd, który zgłoszono z aplikacji, jest już naprawiony. Poprawka trafi do aplikacji z najbliższą aktualizacją.</p>
            <p style="background: #f3f4f6; border-radius: 10px; padding: 12px 14px; white-space: pre-wrap; color: #4b5563;">„{WebUtility.HtmlEncode(quote)}”</p>
            {(string.IsNullOrWhiteSpace(note) ? "" : $"<p><b>Od nas:</b> {WebUtility.HtmlEncode(note)}</p>")}
            """);
    }

    /// <summary>Zaproszenie z Panelu (kod zaproszenia): link do kreatora z kodem i dłuższym okresem próbnym.</summary>
    public string InviteEmailBody(string? name, string code, string link, int extraDays) => Shell($"""
        <h1 style="color: #7c3aed; font-size: 1.35rem;">Zbuduj swoją aplikację w minutę</h1>
        <p>{(string.IsNullOrWhiteSpace(name) ? "Cześć!" : $"Cześć {WebUtility.HtmlEncode(name.Trim())},")}</p>
        <p>po naszej rozmowie przesyłam zaproszenie do aplikacji dla trenerów: zapisy klientów, płatności BLIK-iem,
           przypomnienia i plany treningowe pod Twoją nazwą i logo.{(extraDays > 0 ? $" Z tym zaproszeniem masz <b>{extraDays} dni dłużej za darmo</b>." : "")}</p>
        {Btn(link, "Zbuduj swoją aplikację")}
        <p style="color: #6b7280; font-size: 0.9rem;">Twój kod zaproszenia: <b style="font-family: monospace; color: #1f2937;">{WebUtility.HtmlEncode(code)}</b> — jest już wpisany w linku.</p>
        """);

    /// <summary>Link logowania do panelu trenera na stronie Hantly (gdy trener nie pamięta hasła z kreatora).</summary>
    public string TrainerLoginLinkEmailBody(string link) => Shell($"""
        <h1 style="color: #7c3aed; font-size: 1.35rem;">Twój link do panelu Hantly</h1>
        <p>Cześć! Kliknij przycisk, żeby wejść do swojego panelu: plan, płatności i stan aplikacji w jednym miejscu.</p>
        {Btn(link, "Wejdź do panelu")}
        <p style="color: #6b7280; font-size: 0.9rem;">Link działa raz, przez 30 minut. Jeśli to nie Ty prosisz o logowanie — zignoruj tę wiadomość.</p>
        """);

    public string ReferralRewardEmailBody(string name, string referredCompany, string referralLink) => Shell($"""
        <h1 style="color: #059669; font-size: 1.35rem;">Masz miesiąc gratis 🎁</h1>
        <p>Cześć {WebUtility.HtmlEncode(name)},</p>
        <p>Aplikacja {WebUtility.HtmlEncode(referredCompany)} z Twojego polecenia ma już opłacony abonament. Dziękujemy!
           Miesiąc gratis odliczymy od Twojej najbliższej płatności.</p>
        <p>Twój link do polecania: <a href="{WebUtility.HtmlEncode(referralLink)}" style="color: #7c3aed;">{WebUtility.HtmlEncode(referralLink)}</a></p>
        """);

    public string CustomDomainActiveEmailBody(string trainerName, string domain) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 560px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <h1 style="color: #059669; font-size: 1.4rem;">{WebUtility.HtmlEncode(domain)} działa!</h1>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoja domena wskazuje już naszą platformę i ma certyfikat HTTPS. Klienci mogą wchodzić pod
               <a href="https://{domain}" style="color: #7c3aed;">https://{domain}</a> — poprzedni adres też dalej działa.</p>
            <p style="color: #6b7280; font-size: 0.875rem;">Hantly</p>
        </body>
        </html>
        """;

    public string CustomDomainFailedEmailBody(string trainerName, string domain, string platformHost, string? serverIp) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 560px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <h1 style="color: #d97706; font-size: 1.4rem;">Nie widzimy ustawień DNS dla {WebUtility.HtmlEncode(domain)}</h1>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>przez 7 dni domena nie wskazała naszego serwera, więc przestaliśmy czekać. Aplikacja działa normalnie pod
               <a href="https://{platformHost}">{WebUtility.HtmlEncode(platformHost)}</a>.</p>
            <p>Żeby podłączyć domenę, dodaj u jej dostawcy rekord {(serverIp is null ? "" : $"<b>A</b> dla {WebUtility.HtmlEncode(domain)} → <b>{serverIp}</b> oraz ")}<b>CNAME</b> dla www.{WebUtility.HtmlEncode(domain)} → <b>{WebUtility.HtmlEncode(platformHost)}</b>,
               a potem odpisz na tego maila — podłączymy ją od razu.</p>
            <p style="color: #6b7280; font-size: 0.875rem;">Hantly</p>
        </body>
        </html>
        """;

    /// <summary>Kod potwierdzający adres e-mail przed publikacją aplikacji z kreatora.</summary>
    public string VerificationCodeEmailBody(string code) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 520px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <h1 style="color: #7c3aed; font-size: 1.3rem;">Twój kod do publikacji aplikacji</h1>
            <p>Wpisz ten kod w kreatorze, żeby potwierdzić adres e-mail:</p>
            <p style="font-size: 2rem; font-weight: 800; letter-spacing: .3em; text-align: center; background: #f5f3ff; padding: 16px; border-radius: 12px;">{code}</p>
            <p style="color: #6b7280; font-size: 0.875rem;">Kod jest ważny 10 minut. Jeśli to nie Ty — zignoruj tę wiadomość.</p>
        </body>
        </html>
        """;

    /// <summary>Dla admina platformy: nowy trener sam uruchomił aplikację z kreatora.</summary>
    public string AdminAppLaunchedEmailBody(string trainerName, string email, string companyName, string planName, string? phone, string appUrl, bool ok, string details) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <h1 style="color: {(ok ? "#10b981" : "#dc2626")}; font-size: 1.4rem;">{(ok ? "Nowy trener uruchomił aplikację" : "Rejestracja z kreatora — uruchomienie się nie udało")}</h1>
            <div style="background: #f9fafb; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 8px;"><strong>Trener:</strong> {WebUtility.HtmlEncode(trainerName)}</div>
                <div style="margin-bottom: 8px;"><strong>E-mail:</strong> {WebUtility.HtmlEncode(email)}</div>
                <div style="margin-bottom: 8px;"><strong>Studio:</strong> {WebUtility.HtmlEncode(companyName)}</div>
                <div style="margin-bottom: 8px;"><strong>Telefon:</strong> {WebUtility.HtmlEncode(phone ?? "—")}</div>
                <div style="margin-bottom: 8px;"><strong>Plan:</strong> {WebUtility.HtmlEncode(planName)}</div>
                <div><strong>Adres:</strong> {WebUtility.HtmlEncode(appUrl)}</div>
            </div>
            <pre style="white-space: pre-wrap; background: #f3f4f6; padding: 12px; border-radius: 8px; font-size: 12px;">{WebUtility.HtmlEncode(details)}</pre>
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Hantly Portal</p>
        </body>
        </html>
        """;

    public string TenantApprovedEmailBody(string trainerName, string domain, string port, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #10b981;">Twoja aplikacja działa!</h1>
            </div>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>rejestracja w Hantly została zaakceptowana — aplikacja jest gotowa, z ustawieniami z kreatora.</p>

            <div style="background: #f0fdf4; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 12px;"><strong>Plan:</strong> {WebUtility.HtmlEncode(planName)}</div>
                <div style="margin-bottom: 12px;"><strong>Twoja domena:</strong> <a href="https://{domain}" style="color: #6366f1;">{domain}</a></div>
                <div><strong>Login:</strong> e-mail podany przy rejestracji</div>
            </div>

            <h2 style="color: #374151;">Co dalej?</h2>
            <ol style="line-height: 1.8;">
                <li>Otwórz aplikację i zaloguj się e-mailem i hasłem z rejestracji (jeśli aplikacja poprosi o hasło — ustaw je; reszta danych jest już wpisana).</li>
                <li>Utwórz profil trenera (Zarządzanie → Konta) i ustaw na nim godziny pracy.</li>
                <li>Dodaj pierwszego klienta albo wyślij mu link do swojej strony.</li>
            </ol>

            <p style="text-align: center; margin: 30px 0;">
                <a href="https://{domain}" style="background: #10b981; color: white; padding: 12px 30px; border-radius: 8px; text-decoration: none; font-weight: 600;">
                    Otwórz moją aplikację
                </a>
            </p>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                Hantly — Platforma dla trenerow personalnych
            </p>
        </body>
        </html>
        """;

    public string NewServiceOrderAdminEmailBody(string trainerName, string companyName, string serviceName, decimal price, int orderId, string? notes) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #6366f1;">Nowe zamowienie uslugi</h1>
            </div>
            <p>Trener zlozyl nowe zamowienie w sklepie uslug.</p>

            <div style="background: #eff6ff; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 8px;"><strong>Usluga:</strong> {WebUtility.HtmlEncode(serviceName)}</div>
                <div style="margin-bottom: 8px;"><strong>Trener:</strong> {WebUtility.HtmlEncode(trainerName)}</div>
                <div style="margin-bottom: 8px;"><strong>Firma:</strong> {WebUtility.HtmlEncode(companyName)}</div>
                <div style="margin-bottom: 8px;"><strong>Kwota:</strong> {price:0.00} PLN</div>
                <div><strong>Nr zamowienia:</strong> #{orderId}</div>
                {(string.IsNullOrWhiteSpace(notes) ? "" : $"<div style=\"margin-top: 8px;\"><strong>Notatka:</strong> {WebUtility.HtmlEncode(notes)}</div>")}
            </div>

            <p style="text-align: center; margin: 30px 0;">
                <a href="/panel/orders" style="background: #6366f1; color: white; padding: 12px 30px; border-radius: 8px; text-decoration: none; font-weight: 600;">
                    Przejdz do zamowien
                </a>
            </p>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Hantly Portal</p>
        </body>
        </html>
        """;

    public string OrderStatusChangedEmailBody(string trainerName, string serviceName, string newStatusLabel, string? adminNotes) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #6366f1;">Zmiana statusu zamowienia</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Status Twojego zamowienia zostal zmieniony.</p>

            <div style="background: #f0fdf4; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 8px;"><strong>Usluga:</strong> {WebUtility.HtmlEncode(serviceName)}</div>
                <div><strong>Nowy status:</strong> {WebUtility.HtmlEncode(newStatusLabel)}</div>
                {(string.IsNullOrWhiteSpace(adminNotes) ? "" : $"<div style=\"margin-top: 8px;\"><strong>Uwagi:</strong> {WebUtility.HtmlEncode(adminNotes)}</div>")}
            </div>

            <p>Jesli masz pytania, skontaktuj sie z administratorem platformy.</p>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Hantly</p>
        </body>
        </html>
        """;

    public string PendingPaymentEmailBody(string trainerName, string planName, decimal price) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <!--hantly-logo-->
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #6366f1;">Witaj w Hantly!</h1>
            </div>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Dzięki za rejestrację. Twój plan: <strong>{WebUtility.HtmlEncode(planName)}</strong> ({price:0} zł/mies.).</p>

            <div style="background: #fef3c7; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <strong>⏳ Twoja rejestracja czeka na opłacenie.</strong>
                <p style="margin: 10px 0 0;">Skontaktujemy się z Tobą w ciągu 24h z linkiem do płatności. Po opłaceniu Twoja instancja zostanie uruchomiona automatycznie.</p>
            </div>

            <p>W międzyczasie możesz zapoznać się z:</p>
            <ul style="line-height: 1.8;">
                <li>Dokumentacją: <a href="https://hantly.pl" style="color: #6366f1;">hantly.pl</a></li>
                <li>Przykładowym demo: <a href="https://hantly.pl/#szablony" style="color: #6366f1;">hantly.pl</a></li>
            </ul>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Odpowiadaj na tego maila jeśli masz pytania.<br>
                Hantly
            </p>
        </body>
        </html>
        """;
}
