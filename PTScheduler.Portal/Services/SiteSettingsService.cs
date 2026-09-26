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
        public const string StorePaymentGateway = "store_payment_gateway";
        public const string AdminNotificationEmail = "admin_notification_email";

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
