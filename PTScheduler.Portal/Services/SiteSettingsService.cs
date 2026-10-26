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
        /// <summary>Szablon strony głównej: „nowa” (domyślnie) albo „klasyczna”.</summary>
        public const string HomeTemplate = "home_template";
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
        /// <summary>Ile aplikacji dziennie może się uruchomić samo (bez kodu zaproszenia); nadmiar czeka w kolejce.</summary>
        public const string RegistrationDailyLimit = "registration_daily_limit";
        /// <summary>Ile aplikacji w planie darmowym (bez karty) startuje dziennie automatycznie; reszta czeka w kolejce.</summary>
        public const string RegistrationFreeDailyLimit = "registration_free_daily_limit";
        /// <summary>Płatność przy publikacji z kreatora: „auto” (Stripe, a bez niego bramka sklepu), „stripe”, „autopay”, „payu”, „przelewy24”.</summary>
        public const string RegistrationGateway = "registration_gateway";
        /// <summary>Kwota płatności weryfikacyjnej przez Autopay / PayU / Przelewy24 (domyślnie 1 zł).</summary>
        public const string RegistrationVerifyAmount = "registration_verify_amount";
        /// <summary>E-maile powitalne w dniu 1, 3 i 7 po uruchomieniu aplikacji („false” wyłącza).</summary>
        public const string OnboardingEmails = "onboarding_emails";
        /// <summary>Numer administratora na SMS-y o nowych zgłoszeniach ze sklepu.</summary>
        public const string AdminNotificationPhone = "admin_notification_phone";
        /// <summary>Publiczne demo na hantly.pl: „true” — przycisk „Wypróbuj demo” i wejście /demo.</summary>
        public const string DemoEnabled = "demo_enabled";
        /// <summary>Id konta testowego, które służy jako demo.</summary>
        public const string DemoTenantId = "demo_tenant_id";
        /// <summary>Scenariusz danych demo wczytywany przy każdym resecie.</summary>
        public const string DemoScenario = "demo_scenario";
        /// <summary>Godzina nocnego resetu (czas polski, 0–23; „off” — bez resetu).</summary>
        public const string DemoResetHour = "demo_reset_hour";
        /// <summary>Kiedy ostatnio przywrócono dane demo (UTC, ISO).</summary>
        public const string DemoLastReset = "demo_last_reset";
        /// <summary>"false" wyłącza e-maile o nowych zgłoszeniach (domyślnie włączone, gdy jest adres).</summary>
        public const string NotifyTicketsEmail = "notify_tickets_email";
        /// <summary>"true" włącza SMS-y o nowych zgłoszeniach (przez konto SMS platformy).</summary>
        public const string NotifyTicketsSms = "notify_tickets_sms";
        public const string NotifyBugsEmail = "notify_bugs_email";
        /// <summary>Poczta w Panelu: adres, na który piszą ludzie (np. kontakt@hantly.pl).</summary>
        public const string InboxAddress = "inbox_address";
        /// <summary>Tajna część adresu webhooka odbioru poczty (Brevo Inbound Parsing).</summary>
        public const string InboxSecret = "inbox_secret";
        /// <summary>Klucz API Brevo (zaszyfrowany) — pobieranie załączników i zakładanie webhooka.</summary>
        public const string InboxBrevoApiKey = "inbox_brevo_api_key";
        /// <summary>"false" wyłącza e-mail do administratora o nowej wiadomości.</summary>
        public const string InboxNotifyEmail = "inbox_notify_email";
        /// <summary>"true" włącza SMS do administratora o nowej wiadomości.</summary>
        public const string InboxNotifySms = "inbox_notify_sms";
        /// <summary>Kiedy przyszła ostatnia wiadomość (UTC, ISO) — stan odbioru w ustawieniach.</summary>
        public const string InboxLastReceivedAt = "inbox_last_received_at";
        /// <summary>Dane sprzedawcy w nagłówku PDF oferty trenera (nazwa, adres, NIP, konto — wiele linii).</summary>
        public const string OfferSellerDetails = "offer_seller_details";

        // Asystent-maskotka (domyślnie „Hantly”) i AI przez OpenRouter
        public const string AssistantEnabled = "assistant_enabled";
        public const string AssistantName = "assistant_name";
        /// <summary>„bounce” | „float” | „wave” | „none”.</summary>
        public const string AssistantAnimation = "assistant_animation";
        /// <summary>Ruchliwość animowanego hantelka: calm, normal, lively (jak często robi sztuczki).</summary>
        public const string AssistantMotion = "assistant_motion";
        /// <summary>Dawniej: włączone efekty ruchu po przecinku (czytane tylko, gdy nie ma <see cref="AssistantEffectsOff"/>).</summary>
        public const string AssistantEffects = "assistant_effects";
        /// <summary>Wyłączone efekty ruchu po przecinku — nowe efekty są więc od razu włączone.</summary>
        public const string AssistantEffectsOff = "assistant_effects_off";
        /// <summary>Układ widżetu asystenta w aplikacji trenera: bubble, card, pill, peek.</summary>
        public const string AssistantLayout = "assistant_layout";
        /// <summary>Masterprompt: osobowość i zasady pisania asystenta ({NAZWA} = imię maskotki).</summary>
        public const string AssistantPrompt = "assistant_prompt";
        /// <summary>Prompt podsumowań postępów czytanych przez klientów trenera.</summary>
        public const string AssistantClientPrompt = "assistant_client_prompt";
        public const string AiEnabled = "ai_enabled";
        /// <summary>Klucz OpenRouter (zaszyfrowany Data Protection).</summary>
        public const string AiApiKey = "ai_api_key";
        public const string AiModel = "ai_model";
        /// <summary>"true" — w aplikacji trenera „Zaproponuj plan” może dopracować propozycję przez AI (domyślnie wyłączone).</summary>
        public const string AiPlanGenerator = "ai_plan_generator";
        public const string AiMonthlyBudgetUsd = "ai_monthly_budget_usd";
        /// <summary>Ceny awaryjne (USD za 1 mln tokenów) — gdy OpenRouter nie poda kosztu w odpowiedzi.</summary>
        public const string AiPriceInputPerM = "ai_price_input_per_m";
        public const string AiPriceOutputPerM = "ai_price_output_per_m";

        // Agent Portalu — szczeniak (domyślnie „Leo”) dla administratora platformy
        public const string LeoEnabled = "leo_enabled";
        public const string LeoName = "leo_name";
        public const string LeoAnimation = "leo_animation";
        /// <summary>Masterprompt Leo ({NAZWA} = imię).</summary>
        public const string LeoPrompt = "leo_prompt";
        /// <summary>"true" — poranny raport e-mailem na adres powiadomień administratora.</summary>
        public const string LeoReportEnabled = "leo_report_enabled";
        public const string LeoReportHour = "leo_report_hour";
        /// <summary>Dzień ostatniego porannego raportu (yyyy-MM-dd) — chroni przed podwójną wysyłką.</summary>
        public const string LeoReportSentOn = "leo_report_sent_on";
        /// <summary>Ostatnie wnioski „Wygeneruj” (JSON: tekst, data, czy z AI) — ankieta, spotkania, opinie.</summary>
        public const string LeoInsightSurvey = "leo_insight_survey";
        public const string LeoInsightMeetings = "leo_insight_meetings";
        public const string LeoInsightFeedback = "leo_insight_feedback";
        /// <summary>„Głos klientów” — wnioski z ankiety, spotkań, ocen i zgłoszeń razem (JSON jak wyżej).</summary>
        public const string LeoInsightVoice = "leo_insight_voice";
        /// <summary>"true" — tygodniowy raport biznesowy w poniedziałek (o godzinie porannego raportu).</summary>
        public const string LeoWeeklyEnabled = "leo_weekly_enabled";
        /// <summary>Poniedziałek ostatniego raportu tygodniowego (yyyy-MM-dd).</summary>
        public const string LeoWeeklySentOn = "leo_weekly_sent_on";

        // Dane do dokumentów prawnych (regulamin, polityka prywatności, umowa powierzenia)
        public const string BrandName = "brand_name";
        /// <summary>„true” — każdy administrator Portalu musi mieć włączoną weryfikację dwuetapową.</summary>
        public const string RequireAdmin2fa = "require_admin_2fa";
        /// <summary>Adres, dla którego odzyskiwanie dostępu (Portal__RecoverAdmin) już zadziałało — drugi raz nie resetujemy.</summary>
        public const string RecoverAdminUsed = "recover_admin_used";
        /// <summary>Chwila (UTC, ISO 8601), od której pasek liczy nieudane logowania — „Oznacz jako sprawdzone” w logach logowań.</summary>
        public const string LoginFailuresAckAt = "login_failures_ack_at";
        public const string LegalCompany = "legal_company";
        public const string LegalAddress = "legal_address";
        public const string LegalNip = "legal_nip";
        public const string LegalEmail = "legal_email";
        /// <summary>Dostawca serwerów wpisywany do listy podmiotów przetwarzających.</summary>
        public const string LegalHosting = "legal_hosting";
        /// <summary>Dostawca wysyłki e-maili (SMTP) do listy podmiotów przetwarzających.</summary>
        public const string LegalMail = "legal_mail";

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

        // Księgowość platformy (działalność nierejestrowana → firma)
        /// <summary>„unregistered” | „business”; puste — księgowość jeszcze nieskonfigurowana.</summary>
        public const string FinanceMode = "finance_mode";
        /// <summary>Od kiedy prowadzona jest działalność nierejestrowana (yyyy-MM-dd).</summary>
        public const string FinanceUnregisteredSince = "finance_unregistered_since";
        /// <summary>Data rozpoczęcia firmy z CEIDG (yyyy-MM-dd) — od tego dnia dokumenty wystawia firma.</summary>
        public const string FinanceBusinessSince = "finance_business_since";
        /// <summary>Od której daty wpłaty trafiają do ewidencji (yyyy-MM-dd).</summary>
        public const string FinanceStart = "finance_start";
        public const string FinanceSellerName = "finance_seller_name";
        public const string FinanceSellerAddress = "finance_seller_address";
        public const string FinanceSellerEmail = "finance_seller_email";
        public const string FinanceSellerTaxId = "finance_seller_tax_id";
        public const string FinanceSellerRegon = "finance_seller_regon";
        public const string FinanceSellerBank = "finance_seller_bank";
        /// <summary>Firma: „zw” (zwolnienie podmiotowe z VAT) albo „vat” (czynny podatnik VAT, 23%).</summary>
        public const string FinanceVat = "finance_vat";
        /// <summary>Własna adnotacja o podstawie zwolnienia z VAT (puste — tekst domyślny dla trybu).</summary>
        public const string FinanceVatNote = "finance_vat_note";
        /// <summary>„true” — dokument sprzedaży powstaje sam po każdej wpłacie.</summary>
        public const string FinanceAutoIssue = "finance_auto_issue";
        /// <summary>„true” — dokument idzie e-mailem (PDF) do nabywcy.</summary>
        public const string FinanceEmailDocs = "finance_email_docs";
        /// <summary>„local” (numeracja i PDF w Hantly) albo „fakturownia”.</summary>
        public const string FinanceProvider = "finance_provider";
        /// <summary>Subdomena konta Fakturowni (np. „hantly” dla hantly.fakturownia.pl).</summary>
        public const string FinanceFakturowniaDomain = "finance_fakturownia_domain";
        /// <summary>Token API Fakturowni (zaszyfrowany Data Protection).</summary>
        public const string FinanceFakturowniaToken = "finance_fakturownia_token";
        /// <summary>„true” — dokument czeka na zatwierdzenie, zanim trafi do Fakturowni (domyślnie tak).</summary>
        public const string FinanceFakturowniaApprove = "finance_fakturownia_approve";
        /// <summary>„true” — nowe dokumenty wystawione w Fakturowni pobieramy same (bez dopisywania do ewidencji).</summary>
        public const string FinanceFakturowniaAutoImport = "finance_fakturownia_auto_import";
        /// <summary>Do którego dnia pobrano już dokumenty z Fakturowni (yyyy-MM-dd).</summary>
        public const string FinanceFakturowniaImportedTo = "finance_fakturownia_imported_to";
        /// <summary>Własny limit kwartalny działalności nierejestrowanej (puste — 225% płacy minimalnej).</summary>
        public const string FinanceQuarterLimit = "finance_quarter_limit";
        /// <summary>Limit zwolnienia podmiotowego z VAT na rok (firma, „zw”).</summary>
        public const string FinanceVatLimit = "finance_vat_limit";
        /// <summary>Ile procent dochodu odkładać na podatek (szacunek).</summary>
        public const string FinanceTaxReservePct = "finance_tax_reserve_pct";
        public const string FinancePrefixReceipt = "finance_prefix_receipt";
        public const string FinancePrefixInvoice = "finance_prefix_invoice";
        public const string FinancePrefixCorrection = "finance_prefix_correction";
        /// <summary>Ostatnie powiadomienie o limicie (np. „2026-Q4-80”) — jeden e-mail na próg w kwartale.</summary>
        public const string FinanceLimitAlertSent = "finance_limit_alert_sent";
        /// <summary>Kiedy ostatnio pobrano paczkę dla księgowej / eksport (UTC, ISO).</summary>
        public const string FinanceLastExportAt = "finance_last_export_at";

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
        [Keys.MainDomain] = "hantly.pl",
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
        [Keys.AssistantEnabled] = "true",
        [Keys.AssistantName] = "Hantly",
        [Keys.AssistantAnimation] = "live",
        [Keys.AssistantLayout] = "bubble",
        [Keys.AssistantMotion] = "normal",
        [Keys.AiEnabled] = "false",
        [Keys.AiModel] = "mistralai/mistral-large",
        [Keys.AiMonthlyBudgetUsd] = "5",
        [Keys.AiPriceInputPerM] = "2",
        [Keys.AiPriceOutputPerM] = "10",
        [Keys.LeoEnabled] = "true",
        [Keys.LeoName] = "Leo",
        [Keys.LeoAnimation] = "wave",
        [Keys.LeoReportEnabled] = "true",
        [Keys.LeoReportHour] = "7",
        [Keys.LeoWeeklyEnabled] = "true",
        [Keys.FinanceAutoIssue] = "true",
        [Keys.FinanceEmailDocs] = "true",
        [Keys.FinanceProvider] = "local",
        [Keys.FinanceFakturowniaApprove] = "true",
        [Keys.FinanceVat] = "zw",
        [Keys.FinanceTaxReservePct] = "12",
        [Keys.FinancePrefixReceipt] = "RACH",
        [Keys.FinancePrefixInvoice] = "FV",
        [Keys.FinancePrefixCorrection] = "KOR",
    };

    public async Task<string> GetAsync(string key)
    {
        await using var db = dbFactory.CreateDbContext();
        var setting = await db.SiteSettings.FindAsync(key);
        return setting?.Value ?? Defaults.GetValueOrDefault(key, "");
    }

    /// <summary>Wartość zapisana w bazie albo null, gdy nikt jej nie ustawiał (bez wartości domyślnej).</summary>
    public async Task<string?> GetStoredAsync(string key)
    {
        await using var db = dbFactory.CreateDbContext();
        return (await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value;
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
