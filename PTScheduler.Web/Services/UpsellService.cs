using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Podpowiedź dla właściciela studia: coś się kończy (SMS-y, miejsce na wideo, limit klientów)
/// albo delikatna propozycja wyższego planu. <see cref="Level"/>: critical — już się skończyło,
/// warn — niedługo się skończy, tip — spokojna podpowiedź (bez popupu).
/// </summary>
public sealed record UpsellNotice(string Key, string Level, string Icon, string Title, string Text, string CtaLabel, string CtaHref)
{
    public bool IsUrgent => Level is "critical" or "warn";
}

public sealed class UpsellService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    EntitlementService entitlements,
    ISmsService sms,
    IBunnyService bunny,
    IMemoryCache cache,
    ILogger<UpsellService> logger)
{
    public const string ShopHref = "/admin/sklep";
    public const string UpgradeHref = "/admin/upgrade";
    /// <summary>Tyle dni po uruchomieniu aplikacji nie podsuwamy wyższego planu.</summary>
    public const int FirstWeekDays = 7;

    // Funkcje, które warto podsunąć, gdy nie ma ich w planie — po jednej, zmieniają się co dzień.
    private static readonly (string Flag, string Icon, string Title, string Text)[] Tips =
    [
        ("SmsReminders", "bi-chat-dots", "Mniej nieobecności dzięki SMS-om", "Przypomnienie SMS dzień przed treningiem — klienci rzadziej zapominają o wizycie."),
        ("PaymentsEnabled", "bi-credit-card", "Płatności online od klientów", "BLIK i karta przy zakupie pakietu — pieniądze od razu na Twoim koncie, bez przypominania."),
        ("Coupons", "bi-tag", "Kupony na promocje", "Kod rabatowy na start albo na święta — prosty sposób na nowych klientów."),
        ("ReferralProgram", "bi-gift", "Klienci polecają Cię dalej", "Program poleceń sam nagradza klienta, gdy polecona osoba odbędzie pierwszy trening."),
        ("FinancialReports", "bi-graph-up-arrow", "Finanse w jednym miejscu", "Przychody, sprzedane pakiety i zaległości — bez arkusza w Excelu."),
        ("AdvancedAnalytics", "bi-bar-chart-line", "Statystyki studia", "Kto trenuje regularnie, kto odpuszcza i które pakiety sprzedają się najlepiej."),
        ("CustomEmailTemplates", "bi-envelope-heart", "E-maile Twoim językiem", "Własne teksty powitań i przypomnień, z imieniem klienta."),
        ("CoursesEnabled", "bi-mortarboard", "Sprzedawaj kursy online", "Programy i kursy wideo, które zarabiają także wtedy, gdy nie prowadzisz treningu."),
        ("PushNotifications", "bi-bell", "Powiadomienia na telefonie klienta", "Przypomnienia i wiadomości jak z każdej innej aplikacji."),
        ("RoleBasedAccess", "bi-people", "Zespół w studiu", "Dodaj trenerów i asystentów i zdecyduj, co każdy może zobaczyć."),
        ("IntegrationGoogleMeet", "bi-camera-video", "Treningi online z Google Meet", "Link do spotkania dołącza się do wizyty sam."),
    ];

    /// <summary>Podpowiedzi posortowane od najpilniejszej. Wynik trzymamy kilka minut, żeby nie pytać Portalu przy każdej stronie.</summary>
    public Task<List<UpsellNotice>> GetAsync() =>
        cache.GetOrCreateAsync("upsell:notices", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await BuildAsync();
        })!;

    private async Task<List<UpsellNotice>> BuildAsync()
    {
        var list = new List<UpsellNotice>();
        var plan = entitlements.Current;
        // Na najwyższym planie nie ma czego proponować — tylko sklep (SMS-y, miejsce na wideo).
        var canUpgrade = PlanCatalog.HasUpgrade(plan);

        await Safe(async () =>
        {
            if (!plan.SmsReminders || !await sms.IsEnabledAsync()) return;
            var status = await sms.GetCentralizedStatusAsync();
            int left, limit;
            if (status is not null)
            {
                if (!status.PlatformSmsEnabled || status.Unlimited) return;
                left = status.MonthlyLeft + (int)status.SmsCredits;
                limit = Math.Max(status.MonthlyLimit, 1);
            }
            else
            {
                if (plan.MaxSmsPerMonth is <= 0 or int.MaxValue) return;
                var (sent, max) = await sms.GetQuotaStatusAsync(plan.MaxSmsPerMonth);
                left = Math.Max(0, max - sent);
                limit = max;
            }
            if (left <= 0)
                list.Add(new("sms-out", "critical", "bi-chat-dots-fill", "SMS-y się skończyły",
                    "Przypomnienia SMS nie wychodzą do klientów. Dodaj więcej SMS-ów do abonamentu — działa od razu.",
                    "Więcej SMS-ów", ShopHref + "?dodatek=sms"));
            else if (left <= Math.Max(10, limit / 5))
                list.Add(new("sms-low", "warn", "bi-chat-dots-fill", $"Zostało {left} SMS-ów",
                    "Starczy na kilka dni przypomnień. Większy miesięczny limit SMS-ów dodasz do abonamentu jednym kliknięciem.",
                    "Więcej SMS-ów", ShopHref + "?dodatek=sms"));
        });

        await Safe(async () =>
        {
            if (plan.VideoProvider != "bunny" || plan.MaxVideoStorageGB is <= 0 or int.MaxValue) return;
            // Limit i zajęte miejsce liczy Portal (pakiet + dodatki + dokupione GB).
            var video = await bunny.GetStatusAsync();
            if (!video.Enabled || video.QuotaBytes is <= 0 or long.MaxValue) return;
            var pct = (int)(100 * video.UsedBytes / video.QuotaBytes);
            if (pct >= 95)
                list.Add(new("video-full", "critical", "bi-camera-reels-fill", $"Miejsce na wideo zajęte w {Math.Min(pct, 100)}%",
                    "Nowe filmy się nie zmieszczą. Dodaj miejsce do abonamentu — lekcje i instruktaże będą dalej działać.",
                    "Więcej miejsca", ShopHref + "?dodatek=miejsce"));
            else if (pct >= 80)
                list.Add(new("video-low", "warn", "bi-camera-reels-fill", $"Miejsce na wideo zajęte w {pct}%",
                    "Zostało niewiele miejsca na nowe filmy. Warto powiększyć je wcześniej.",
                    "Więcej miejsca", ShopHref + "?dodatek=miejsce"));
        });

        await Safe(async () =>
        {
            if (!canUpgrade || plan.MaxClients == int.MaxValue || plan.MaxClients <= 0) return;
            await using var db = dbFactory.CreateDbContext();
            // Tak samo jak limit przy dodawaniu klienta: liczą się wszyscy poza nieaktywnymi.
            var active = await db.Clients.CountAsync(c => c.Status != ClientStatus.Inactive);
            if (active > plan.MaxClients)
                list.Add(new("clients-over", "critical", "bi-people-fill", $"Klientów jest więcej niż w planie ({active}/{plan.MaxClients})",
                    $"Plan {plan.Name} obejmuje {plan.MaxClients} klientów. Nowe wizyty i rezerwacje online wrócą po wyborze wyższego planu albo oznaczeniu nieaktywnych klientów.",
                    "Zobacz plany", UpgradeHref));
            else if (active >= plan.MaxClients)
                list.Add(new("clients-full", "critical", "bi-people-fill", $"Limit klientów wykorzystany ({active}/{plan.MaxClients})",
                    "Nowi klienci nie zapiszą się, dopóki nie zwiększysz limitu w wyższym planie.",
                    "Zobacz plany", UpgradeHref));
            else if (active * 10 >= plan.MaxClients * 8)
                list.Add(new("clients-low", "warn", "bi-people-fill", $"Masz już {active} z {plan.MaxClients} klientów",
                    "Studio rośnie! Wyższy plan to więcej miejsca na klientów i nowe funkcje.",
                    "Zobacz plany", UpgradeHref));
        });

        // Spokojna podpowiedź planu — jedna, zmienia się co dzień. Przez pierwszy tydzień po starcie
        // żadnych propozycji: najpierw pierwsze kroki i pierwsi klienci (pilne sprawy powyżej i tak się pokażą).
        var firstWeek = false;
        await Safe(async () =>
        {
            await using var db = dbFactory.CreateDbContext();
            var started = await db.AppBrandings.AsNoTracking().Select(b => b.SetupCompletedAt).FirstOrDefaultAsync();
            firstWeek = started is { } at && at > DateTime.UtcNow.AddDays(-FirstWeekDays);
        });
        var locked = canUpgrade && !firstWeek ? Tips.Where(t => !entitlements.IsAllowed(t.Flag) && (t.Flag != "SmsReminders" || entitlements.Current.SmsAvailable)).ToList() : [];
        if (locked.Count > 0)
        {
            var t = locked[DateTime.UtcNow.DayOfYear % locked.Count];
            list.Add(new($"tip-{t.Flag}", "tip", t.Icon, t.Title, t.Text, "Zobacz, co daje wyższy plan", UpgradeHref));
        }

        return list.OrderBy(n => n.Level switch { "critical" => 0, "warn" => 1, _ => 2 }).ToList();
    }

    private async Task Safe(Func<Task> check)
    {
        try { await check(); }
        catch (Exception ex) { logger.LogDebug(ex, "Podpowiedź pominięta — brak danych."); }
    }
}
