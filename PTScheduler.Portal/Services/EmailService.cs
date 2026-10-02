using System.Net;
using System.Net.Mail;

namespace PTScheduler.Portal.Services;

public class EmailService(SiteSettingsService settings, ILogger<EmailService> logger)
{
    /// <param name="fromNameOverride">Nazwa nadawcy (np. studio trenera); adres zostaje adresem platformy.</param>
    /// <param name="replyTo">Adres odpowiedzi — przy poczcie trenera jego własny e-mail.</param>
    public async Task<(bool Success, string? Error)> SendAsync(string toEmail, string subject, string htmlBody,
        string? fromNameOverride = null, string? replyTo = null, string? toName = null)
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
            var fromName = !string.IsNullOrWhiteSpace(fromNameOverride) ? fromNameOverride
                : string.IsNullOrWhiteSpace(s[SiteSettingsService.Keys.SmtpFromName]) ? "PTScheduler" : s[SiteSettingsService.Keys.SmtpFromName];
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
            mail.To.Add(string.IsNullOrWhiteSpace(toName) ? new MailAddress(toEmail) : new MailAddress(toEmail, toName));
            if (!string.IsNullOrWhiteSpace(replyTo)) mail.ReplyToList.Add(new MailAddress(replyTo));

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

    /// <summary>Koniec okresu próbnego z podpiętą kartą: kiedy i ile pobierzemy oraz jak zrezygnować.</summary>
    public string TrialEndingWithCardEmailBody(string trainerName, int daysLeft, decimal amount, bool yearly, string planName, string domain) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
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
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Masz pytania? Odpisz na tego maila.<br>PTScheduler</p>
        </body>
        </html>
        """;

    public string TrialWarningEmailBody(string trainerName, int daysLeft) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #f59e0b;">Twoj okres probny konczy sie za {daysLeft} dni</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoj darmowy okres probny PTScheduler konczy sie za <strong>{daysLeft} dni</strong>.</p>
            <p>Aby kontynuowac korzystanie z platformy, dodaj metode platnosci lub wybierz plan subskrypcji.</p>
            <p>Jesli nie podejmiesz zadnej akcji, Twoje konto zostanie automatycznie zawieszone po zakonczeniu okresu probnego.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                PTScheduler
            </p>
        </body>
        </html>
        """;

    public string PaymentReceivedEmailBody(string trainerName, decimal amount, string invoiceNumber) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #10b981;">Platnosc otrzymana</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Potwierdzamy otrzymanie platnosci.</p>
            <div style="background: #f0fdf4; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <div style="margin-bottom: 8px;"><strong>Kwota:</strong> {amount:0.00} PLN</div>
                <div><strong>Faktura:</strong> {WebUtility.HtmlEncode(invoiceNumber)}</div>
            </div>
            <p>Dziekujemy za korzystanie z PTScheduler!</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">PTScheduler</p>
        </body>
        </html>
        """;

    public string PaymentFailedEmailBody(string trainerName, decimal amount, string invoiceNumber) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
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
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">PTScheduler</p>
        </body>
        </html>
        """;

    public string SuspensionEmailBody(string trainerName, string reason) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #ef4444;">Konto zawieszone</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoje konto PTScheduler zostalo zawieszone.</p>
            <div style="background: #fef2f2; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <strong>Powod:</strong> {WebUtility.HtmlEncode(reason)}
            </div>
            <p>Aby przywrocic dostep, skontaktuj sie z nami lub odnow subskrypcje.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">PTScheduler</p>
        </body>
        </html>
        """;

    public string InactivityWarningEmailBody(string trainerName, int inactiveDays, int daysUntilSuspend) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #f59e0b;">Twoje konto jest nieaktywne</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Zauazylismy, ze Twoje konto PTScheduler nie bylo uzywane od <strong>{inactiveDays} dni</strong>.</p>
            <p>Jesli nie zalogujesz sie w ciagu <strong>{daysUntilSuspend} dni</strong>, Twoje konto zostanie automatycznie zawieszone.</p>
            <p>Wystarczy sie zalogowac, zeby konto pozostalo aktywne.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                PTScheduler
            </p>
        </body>
        </html>
        """;

    public string CleanupWarningEmailBody(string trainerName, int daysLeft) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #ef4444;">Twoje konto zostanie usuniete</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoje konto PTScheduler jest zawieszone i zostanie <strong>trwale usuniete za {daysLeft} dni</strong>.</p>
            <p>Po usunieciu wszystkie Twoje dane (klienci, sesje, pakiety, kursy) zostana bezpowrotnie skasowane.</p>
            <p>Aby temu zapobiec, skontaktuj sie z nami lub odnow subskrypcje.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                PTScheduler
            </p>
        </body>
        </html>
        """;

    public string CleanupDeletionEmailBody(string trainerName, int suspendedDays) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #ef4444;">Konto zostalo usuniete</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoje konto PTScheduler bylo zawieszone przez {suspendedDays} dni i zostalo trwale usuniete.</p>
            <p>Wszystkie dane zostaly skasowane. Jesli chcesz wrocic do PTScheduler, mozesz zalozyc nowe konto w kazdej chwili.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                PTScheduler — platforma dla trenerów personalnych
            </p>
        </body>
        </html>
        """;

    public string GraceExtendedEmailBody(string trainerName, int graceDays) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #10b981;">Przedluzono okres ochrony konta</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Administrator przedluzyl okres ochrony Twojego konta o <strong>{graceDays} dni</strong>.</p>
            <p>W tym czasie Twoje dane sa bezpieczne i nie zostana usuniete. Prosimy o uregulowanie subskrypcji w tym okresie.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                PTScheduler
            </p>
        </body>
        </html>
        """;

    public string RegistrationPendingReviewEmailBody(string trainerName, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #6366f1;">Dziekujemy za rejestracje!</h1>
            </div>
            <p>Czesc {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoje zgloszenie w PTScheduler zostalo przyjete i oczekuje na weryfikacje.</p>

            <div style="background: #eff6ff; border-radius: 12px; padding: 20px; margin: 20px 0;">
                <strong>Plan:</strong> {WebUtility.HtmlEncode(planName)}<br>
                <strong>Status:</strong> Oczekuje na akceptacje
            </div>

            <p>Nasz zespol zweryfikuje Twoje zgloszenie i skontaktuje sie z Toba najszybciej jak to mozliwe. Po akceptacji otrzymasz email z danymi dostepu do Twojej instancji.</p>

            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">
                Masz pytania? Odpisz na tego maila.<br>
                PTScheduler
            </p>
        </body>
        </html>
        """;

    public string AdminNewRegistrationEmailBody(string trainerName, string email, string companyName, string planName, string phone, int tenantId) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #f59e0b;">Nowa rejestracja trenera</h1>
            </div>
            <p>Nowy trener zarejestrował się w PTScheduler i czeka na akceptację.</p>

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
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">PTScheduler Portal</p>
        </body>
        </html>
        """;

    /// <summary>Po publikacji z kreatora: aplikacja trenera działa (bez akceptacji admina).</summary>
    public string AppReadyEmailBody(string trainerName, string companyName, string appUrl, string loginEmail, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
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
                PTScheduler — platforma dla trenerów personalnych
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
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Masz pytania? Odpisz na tego maila.<br>PTScheduler</p>
        </body>
        </html>
        """;

    /// <summary>Zgłoszenie z kreatora czeka w kolejce (limit, tryb akceptacji albo chwilowy problem serwera).</summary>
    public string AppQueuedEmailBody(string trainerName, string companyName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <h1 style="color: #7c3aed; font-size: 1.4rem;">Twoja aplikacja jest w kolejce</h1>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>aplikacja <strong>{WebUtility.HtmlEncode(companyName)}</strong> jest zbudowana i czeka na uruchomienie. Wszystkie ustawienia z kreatora są zapisane —
               nic nie trzeba wpisywać od nowa. Gdy tylko ją uruchomimy, dostaniesz e-mail z adresem. Zwykle trwa to kilka godzin.</p>
            <p>Okres próbny liczymy od dnia uruchomienia aplikacji.</p>
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">Masz pytania? Odpisz na tego maila.<br>PTScheduler</p>
        </body>
        </html>
        """;

    public string CustomDomainActiveEmailBody(string trainerName, string domain) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 560px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <h1 style="color: #059669; font-size: 1.4rem;">{WebUtility.HtmlEncode(domain)} działa!</h1>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>Twoja domena wskazuje już naszą platformę i ma certyfikat HTTPS. Klienci mogą wchodzić pod
               <a href="https://{domain}" style="color: #7c3aed;">https://{domain}</a> — poprzedni adres też dalej działa.</p>
            <p style="color: #6b7280; font-size: 0.875rem;">PTScheduler</p>
        </body>
        </html>
        """;

    public string CustomDomainFailedEmailBody(string trainerName, string domain, string platformHost, string? serverIp) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 560px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <h1 style="color: #d97706; font-size: 1.4rem;">Nie widzimy ustawień DNS dla {WebUtility.HtmlEncode(domain)}</h1>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>przez 7 dni domena nie wskazała naszego serwera, więc przestaliśmy czekać. Aplikacja działa normalnie pod
               <a href="https://{platformHost}">{WebUtility.HtmlEncode(platformHost)}</a>.</p>
            <p>Żeby podłączyć domenę, dodaj u jej dostawcy rekord {(serverIp is null ? "" : $"<b>A</b> dla {WebUtility.HtmlEncode(domain)} → <b>{serverIp}</b> oraz ")}<b>CNAME</b> dla www.{WebUtility.HtmlEncode(domain)} → <b>{WebUtility.HtmlEncode(platformHost)}</b>,
               a potem odpisz na tego maila — podłączymy ją od razu.</p>
            <p style="color: #6b7280; font-size: 0.875rem;">PTScheduler</p>
        </body>
        </html>
        """;

    /// <summary>Kod potwierdzający adres e-mail przed publikacją aplikacji z kreatora.</summary>
    public string VerificationCodeEmailBody(string code) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 520px; margin: 0 auto; padding: 20px; color: #1f2937;">
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
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">PTScheduler Portal</p>
        </body>
        </html>
        """;

    public string TenantApprovedEmailBody(string trainerName, string domain, string port, string planName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
            <div style="text-align: center; padding: 20px 0;">
                <h1 style="color: #10b981;">Twoja aplikacja działa!</h1>
            </div>
            <p>Cześć {WebUtility.HtmlEncode(trainerName)},</p>
            <p>rejestracja w PTScheduler została zaakceptowana — aplikacja jest gotowa, z ustawieniami z kreatora.</p>

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
                PTScheduler — Platforma dla trenerow personalnych
            </p>
        </body>
        </html>
        """;

    public string NewServiceOrderAdminEmailBody(string trainerName, string companyName, string serviceName, decimal price, int orderId, string? notes) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
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
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">PTScheduler Portal</p>
        </body>
        </html>
        """;

    public string OrderStatusChangedEmailBody(string trainerName, string serviceName, string newStatusLabel, string? adminNotes) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #1f2937;">
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
            <p style="color: #6b7280; font-size: 0.875rem; text-align: center;">PTScheduler</p>
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
