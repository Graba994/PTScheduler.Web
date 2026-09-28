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
    ICourseService courses,
    IMemoryCache cache,
    ILogger<UpsellService> logger)
{
    public const string ShopHref = "/admin/sklep";
    public const string UpgradeHref = "/admin/upgrade";

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
                    "Przypomnienia SMS nie wychodzą do klientów. Dokup pakiet — dokupione SMS-y nie wygasają.",
                    "Dokup SMS-y", ShopHref));
            else if (left <= Math.Max(10, limit / 5))
                list.Add(new("sms-low", "warn", "bi-chat-dots-fill", $"Zostało {left} SMS-ów",
                    "Starczy na kilka dni przypomnień. Dokup pakiet, zanim klienci przestaną je dostawać.",
                    "Dokup SMS-y", ShopHref));
        });

        await Safe(async () =>
        {
            if (!plan.CoursesEnabled || plan.MaxVideoStorageGB is <= 0 or int.MaxValue) return;
            var (_, used, maxBytes) = await entitlements.CheckVideoStorageAsync(courses);
            var cdn = plan.VideoProvider == "bunny" ? await bunny.GetCentralizedStatusAsync() : null;
            var total = maxBytes + (long)((cdn?.StorageCredits ?? 0) * 1024 * 1024 * 1024);
            if (total <= 0) return;
            var pct = (int)(100 * used / total);
            if (pct >= 95)
                list.Add(new("video-full", "critical", "bi-camera-reels-fill", $"Miejsce na wideo zajęte w {Math.Min(pct, 100)}%",
                    "Nowe filmy się nie zmieszczą. Dokup miejsce — lekcje i instruktaże będą dalej działać.",
                    "Więcej miejsca", ShopHref));
            else if (pct >= 80)
                list.Add(new("video-low", "warn", "bi-camera-reels-fill", $"Miejsce na wideo zajęte w {pct}%",
                    "Zostało niewiele miejsca na nowe filmy. Warto dokupić je wcześniej.",
                    "Więcej miejsca", ShopHref));
        });

        await Safe(async () =>
        {
            if (plan.MaxClients == int.MaxValue || plan.MaxClients <= 0) return;
            await using var db = dbFactory.CreateDbContext();
            var active = await db.Clients.CountAsync(c => c.Status == ClientStatus.Active);
            if (active >= plan.MaxClients)
                list.Add(new("clients-full", "critical", "bi-people-fill", $"Limit klientów wykorzystany ({active}/{plan.MaxClients})",
                    "Nowi klienci nie zapiszą się, dopóki nie zwiększysz limitu w wyższym planie.",
                    "Zobacz plany", UpgradeHref));
            else if (active * 10 >= plan.MaxClients * 8)
                list.Add(new("clients-low", "warn", "bi-people-fill", $"Masz już {active} z {plan.MaxClients} klientów",
                    "Studio rośnie! Wyższy plan to więcej miejsca na klientów i nowe funkcje.",
                    "Zobacz plany", UpgradeHref));
        });

        // Spokojna podpowiedź planu — jedna, zmienia się co dzień.
        var locked = Tips.Where(t => !entitlements.IsAllowed(t.Flag)).ToList();
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
