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
    }

    private static readonly Dictionary<string, string> Defaults = new()
    {
        [Keys.MainDomain] = "ptscheduler.pl",
        [Keys.NpmAutoRegister] = "true",
        [Keys.BackupDir] = "/opt/ptscheduler/backups",
        [Keys.BackupSchedule] = "daily",
        [Keys.BackupRetentionDays] = "14",
        [Keys.HeroBadge] = "Platforma SaaS dla trenerów",
        [Keys.HeroTitle] = "Twoja instancja PTScheduler gotowa w 5 minut",
        [Keys.HeroSubtitle] = "Grafik, klienci, płatności online, kursy wideo, pakiety treningowe — wszystko pod Twoją domeną, w pełni konfigurowane. Bez programowania.",
        [Keys.HeroCta] = "Rozpocznij za darmo",
        [Keys.HeroCtaUrl] = "/register",
        [Keys.SectionTitle] = "Co dostajesz?",
        [Keys.CtaTitle] = "Gotowy, żeby zacząć?",
        [Keys.CtaSubtitle] = "Pierwszych 3 klientów za darmo. Bez karty kredytowej.",
        [Keys.CtaButton] = "Załóż darmowe konto",
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
