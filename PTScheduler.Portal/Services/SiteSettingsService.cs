using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public class SiteSettingsService(IDbContextFactory<PortalDbContext> dbFactory)
{
    public static class Keys
    {
        public const string HeroTitle = "hero_title";
        public const string HeroSubtitle = "hero_subtitle";
        public const string HeroBadge = "hero_badge";
        public const string HeroCta = "hero_cta";
        public const string HeroCtaUrl = "hero_cta_url";
        public const string SectionTitle = "section_title";
        public const string CtaTitle = "cta_title";
        public const string CtaSubtitle = "cta_subtitle";
        public const string CtaButton = "cta_button";
        public const string SmtpHost = "smtp_host";
        public const string SmtpPort = "smtp_port";
        public const string SmtpUser = "smtp_user";
        public const string SmtpPass = "smtp_pass";
        public const string SmtpFrom = "smtp_from";
        public const string SmtpFromName = "smtp_from_name";
        public const string SmtpSsl = "smtp_ssl";
        public const string MainDomain = "main_domain";
        public const string NpmUrl = "npm_url";
        public const string NpmEmail = "npm_email";
        public const string NpmPassword = "npm_password";
        public const string NpmToken = "npm_token";
        public const string NpmAutoRegister = "npm_auto_register";
        /// <summary>„false” — nowe hosty bez certyfikatu (domyślnie Portal zakłada Let's Encrypt i wymusza HTTPS).</summary>
        public const string NpmAutoSsl = "npm_auto_ssl";
        public const string StripeSecretKey = "stripe_secret_key";
        public const string StripePublishableKey = "stripe_publishable_key";
        public const string StripeWebhookSecret = "stripe_webhook_secret";
        public const string StripeSuccessUrl = "stripe_success_url";
        public const string StripeCancelUrl = "stripe_cancel_url";
        public const string BackupDir = "backup_dir";
        public const string BackupSchedule = "backup_schedule"; // "daily" | "off"
        public const string BackupRetentionDays = "backup_retention_days";
        public const string GithubToken = "github_token";
        public const string GithubOwner = "github_owner";
        public const string GithubRepo = "github_repo";
        public const string GithubBranch = "github_branch";
        public const string FeaturedTrainers = "featured_trainers";
        public const string PayuPosId = "payu_pos_id";
        public const string PayuClientSecret = "payu_client_secret";
        public const string PayuSecondKey = "payu_second_key";
        public const string PayuSandbox = "payu_sandbox";
        public const string P24MerchantId = "p24_merchant_id";
        public const string P24PosId = "p24_pos_id";
        public const string P24ApiKey = "p24_api_key";
        public const string P24Crc = "p24_crc";
        public const string P24Sandbox = "p24_sandbox";
        public const string AutopayServiceId = "autopay_service_id";
        public const string AutopaySharedKey = "autopay_shared_key";
        public const string AutopaySandbox = "autopay_sandbox";
        public const string StorePaymentGateway = "store_payment_gateway";
        public const string AdminNotificationEmail = "admin_notification_email";
        /// <summary>„auto” (domyślnie) — aplikacja trenera startuje od razu po rejestracji; „review” — czeka na akceptację.</summary>
        public const string RegistrationMode = "registration_mode";
        /// <summary>Numer administratora na SMS-y o nowych zgłoszeniach ze sklepu.</summary>
        public const string AdminNotificationPhone = "admin_notification_phone";
        /// <summary>"false" wyłącza e-maile o nowych zgłoszeniach (domyślnie włączone, gdy jest adres).</summary>
        public const string NotifyTicketsEmail = "notify_tickets_email";
        /// <summary>"true" włącza SMS-y o nowych zgłoszeniach (przez konto SMS platformy).</summary>
        public const string NotifyTicketsSms = "notify_tickets_sms";
        /// <summary>Dane sprzedawcy w nagłówku PDF oferty trenera (nazwa, adres, NIP, konto — wiele linii).</summary>
        public const string OfferSellerDetails = "offer_seller_details";

        // Alarm przy fali błędów w aplikacjach trenerów
        public const string ErrorAlertEnabled = "error_alert_enabled";
        /// <summary>Ile błędów w oknie czasu uruchamia alarm.</summary>
        public const string ErrorAlertThreshold = "error_alert_threshold";
        public const string ErrorAlertWindowMinutes = "error_alert_window_minutes";
        /// <summary>Po alarmie dla danej instancji kolejny najwcześniej po tylu minutach.</summary>
        public const string ErrorAlertCooldownMinutes = "error_alert_cooldown_minutes";
        public const string ErrorAlertEmail = "error_alert_email";
        public const string ErrorAlertSms = "error_alert_sms";
        // Kopie zapasowe: test odtworzenia i kopia poza serwerem
        /// <summary>„weekly” — co niedzielę Portal odtwarza najnowsze kopie do tymczasowej bazy; „off” — bez testu.</summary>
        public const string BackupVerify = "backup_verify";
        /// <summary>„off” | „sftp” | „gdrive” — dokąd wysyłać kopie poza serwer.</summary>
        public const string BackupOffsiteTarget = "backup_offsite_target";
        public const string BackupOffsiteRetentionDays = "backup_offsite_retention_days";
        /// <summary>Hasło szyfrowania kopii wysyłanych poza serwer (zaszyfrowane Data Protection).</summary>
        public const string BackupOffsitePassword = "backup_offsite_password";
        public const string BackupSftpHost = "backup_sftp_host";
        public const string BackupSftpPort = "backup_sftp_port";
        public const string BackupSftpUser = "backup_sftp_user";
        public const string BackupSftpPassword = "backup_sftp_password";
        public const string BackupSftpPrivateKey = "backup_sftp_private_key";
        public const string BackupSftpDir = "backup_sftp_dir";
        /// <summary>Odcisk klucza serwera SFTP zapamiętany przy pierwszym połączeniu — inny odcisk = odmowa.</summary>
        public const string BackupSftpFingerprint = "backup_sftp_fingerprint";
        public const string BackupGdriveRefreshToken = "backup_gdrive_refresh_token";
        public const string BackupGdriveEmail = "backup_gdrive_email";
        public const string BackupGdriveFolderId = "backup_gdrive_folder_id";

        // Automatyczne rachunki trenerów
        /// <summary>„true” — Portal sam wystawia miesięczne rachunki i przypomina o płatności.</summary>
        public const string BillingAutoEnabled = "billing_auto_enabled";
        /// <summary>Dzień miesiąca wystawienia rachunku (1–28).</summary>
        public const string BillingDay = "billing_day";
        /// <summary>Termin płatności w dniach od wystawienia.</summary>
        public const string BillingDueDays = "billing_due_days";
        /// <summary>„false” — bez automatycznych przypomnień o zaległościach.</summary>
        public const string BillingReminders = "billing_reminders";

        // Update tracking
        public const string LastTenantBuildCommit = "last_tenant_build_commit";
        public const string LastTenantBuildTime = "last_tenant_build_time";

        // Centralized SMS (SMSAPI.pl platform account)
        public const string PlatformSmsApiToken = "platform_sms_api_token";
        public const string PlatformSmsSenderName = "platform_sms_sender_name";

        // Centralized Bunny CDN (platform account)
        public const string PlatformBunnyApiKey = "platform_bunny_api_key";
        public const string PlatformBunnyLibraryId = "platform_bunny_library_id";
        public const string PlatformBunnyCdnHostname = "platform_bunny_cdn_hostname";
        /// <summary>Klucz konta Bunny (Account → API) — Portal zakłada nim osobną bibliotekę dla każdej instancji.</summary>
        public const string PlatformBunnyAccountKey = "platform_bunny_account_key";

        // Wspólny SMTP platformy dla instancji trenerów (nadawca = nazwa studia, odpowiedzi do trenera)
        public const string ShareSmtpWithTenants = "share_smtp_with_tenants";
        public const string TenantMailDailyLimit = "tenant_mail_daily_limit";

        // Klient OAuth Google platformy — kalendarz i Meet trenerów bez ich własnej konfiguracji.
        public const string PlatformGoogleClientId = "platform_google_client_id";
        public const string PlatformGoogleClientSecret = "platform_google_client_secret";
        public const string PlatformGoogleRedirectUri = "platform_google_redirect_uri";

        // Guardian (upgrade orchestrator)
        public const string GuardianUrl = "guardian_url";
        public const string GuardianSecret = "guardian_secret";
        public const string TenantInternalSecret = "tenant_internal_secret";
    }

    /// <summary>Domyślny nagłówek strony głównej (wyświetlany w wersji z wyróżnieniem).</summary>
    public const string DefaultHeroTitle = "Mniej papierologii. Więcej treningów.";

    private static readonly Dictionary<string, string> Defaults = new()
    {
        [Keys.MainDomain] = "ptscheduler.pl",
        [Keys.NpmAutoRegister] = "true",
        [Keys.BackupDir] = "/opt/ptscheduler/backups",
        [Keys.BackupSchedule] = "daily",
        [Keys.BackupRetentionDays] = "14",
        [Keys.BackupVerify] = "weekly",
        [Keys.BackupOffsiteTarget] = "off",
        [Keys.BackupOffsiteRetentionDays] = "30",
        [Keys.BackupSftpPort] = "22",
        [Keys.BackupSftpDir] = "ptscheduler-backups",
        [Keys.HeroBadge] = "Dla trenerów personalnych i małych studiów",
        [Keys.HeroTitle] = DefaultHeroTitle,
        [Keys.HeroSubtitle] = "Grafik i rezerwacje online, karnety, płatności, czat z klientem i plany treningowe — w jednej aplikacji pod Twoją marką. Klienci instalują ją na telefonie jak zwykłą apkę.",
        [Keys.HeroCta] = "Zacznij za darmo",
        [Keys.HeroCtaUrl] = "/register",
        [Keys.SectionTitle] = "Wszystko, czego potrzebuje trener — w jednym miejscu",
        [Keys.CtaTitle] = "Oddaj papierologię aplikacji. Wróć do trenowania.",
        [Keys.CtaSubtitle] = "Załóż konto w 5 minut i zaproś pierwszych klientów jeszcze dziś. Plan startowy jest za darmo.",
        [Keys.CtaButton] = "Zacznij za darmo",
        [Keys.SmtpPort] = "587",
        [Keys.SmtpSsl] = "true",
        [Keys.GithubOwner] = "graba994",
        [Keys.GithubRepo] = "ptscheduler.web",
        [Keys.GithubBranch] = "master",
        [Keys.FeaturedTrainers] = "[]",
    };

    public async Task<string> GetAsync(string key)
    {
        await using var db = dbFactory.CreateDbContext();
        var setting = await db.SiteSettings.FindAsync(key);
        return setting?.Value ?? Defaults.GetValueOrDefault(key, "");
    }

    public async Task<Dictionary<string, string>> GetAllAsync(params string[] keys)
    {
        await using var db = dbFactory.CreateDbContext();
        var settings = await db.SiteSettings
            .AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        foreach (var key in keys)
        {
            if (!settings.ContainsKey(key))
                settings[key] = Defaults.GetValueOrDefault(key, "");
        }
        return settings;
    }

    public async Task SetAsync(string key, string value)
    {
        await using var db = dbFactory.CreateDbContext();
        var existing = await db.SiteSettings.FindAsync(key);
        if (existing is not null)
            existing.Value = value;
        else
            db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        await db.SaveChangesAsync();
    }

    public async Task SetManyAsync(Dictionary<string, string> values)
    {
        await using var db = dbFactory.CreateDbContext();
        foreach (var (key, value) in values)
        {
            var existing = await db.SiteSettings.FindAsync(key);
            if (existing is not null)
                existing.Value = value;
            else
                db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        }
        await db.SaveChangesAsync();
    }
}
