using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Agent Portalu (domyślnie szczeniak „Leo”) — podpowiedzi dla administratora platformy liczone regułami
/// z danych Portalu: awarie, zgłoszenia, zaległe rachunki, trenerzy zagrożeni odejściem, kolejka rejestracji,
/// kopie zapasowe, nowe opinie i ankiety, a także powody do radości (rejestracje, pierwsze płatności, aktywacje).
/// AI najwyżej przeredaguje tekst zapasowy; liczby zawsze pochodzą stąd.
/// </summary>
public class LeoService(IDbContextFactory<PortalDbContext> dbFactory, SiteSettingsService settings, AssistantService assistant, AiService ai,
    TenantMailRelay mailRelay)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <param name="Placeholders">Znaczniki podmieniane po stronie Portalu (nazwy trenerów nie trafiają do AI).</param>
    public sealed record Tip(string Key, string Kind, string Mood, int Priority, string Fallback,
        Dictionary<string, string> Facts, string? ActionLabel, string? ActionHref, Dictionary<string, string>? Placeholders = null)
    {
        public bool IsTodo => Mood == "remind";
        public bool IsPraise => Mood == "celebrate";
    }

    public async Task<List<Tip>> GetTipsAsync()
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var tips = new List<Tip>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var active = db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active);

        // Aplikacje trenerów, które nie odpowiadają.
        var down = await active.Where(t => t.IsHealthy == false)
            .Select(t => new { t.Slug, t.CompanyName, t.UnhealthySinceUtc }).ToListAsync();
        if (down.Count == 1)
        {
            var minutes = down[0].UnhealthySinceUtc is { } since ? Math.Max(1, (int)(now - since).TotalMinutes) : 0;
            tips.Add(new("down", "awaria", "remind", 95,
                minutes > 0 ? $"Aplikacja [trener] nie odpowiada od {Duration(minutes)}. Zajrzyj do kontenera." : "Aplikacja [trener] nie odpowiada. Zajrzyj do kontenera.",
                new() { ["trener"] = "[trener]", ["czas"] = minutes > 0 ? Duration(minutes) : "" }, "Trenerzy", "/panel/tenants",
                new() { ["[trener]"] = Display(down[0].CompanyName, down[0].Slug) }));
        }
        else if (down.Count > 1)
            tips.Add(new("down", "awaria", "remind", 95,
                $"{down.Count} {Plural(down.Count, "aplikacja trenera nie odpowiada", "aplikacje trenerów nie odpowiadają", "aplikacji trenerów nie odpowiada")}. Sprawdź serwer.",
                new() { ["liczba_aplikacji"] = down.Count.ToString() }, "Serwer", "/panel/resources"));

        // Zgłoszenia ze sklepu usług czekające na zespół.
        var tickets = await db.ServiceOrders.CountAsync(o => o.Status == ServiceOrderStatus.Pending
            || o.Status == ServiceOrderStatus.Accepted || o.Status == ServiceOrderStatus.InProgress);
        if (tickets > 0)
            tips.Add(new("tickets", "zgloszenia", "remind", 90,
                $"{tickets} {Plural(tickets, "zgłoszenie czeka", "zgłoszenia czekają", "zgłoszeń czeka")} na realizację.",
                new() { ["liczba_zgloszen"] = tickets.ToString() }, "Zgłoszenia", "/panel/orders"));

        // Rachunki po terminie.
        var overdue = await db.TenantBills.AsNoTracking().Where(b => b.Status == TenantBillStatus.Issued && b.DueDate < today)
            .Select(b => b.Amount).ToListAsync();
        if (overdue.Count > 0)
        {
            var sum = overdue.Sum().ToString("#,0.##", Pl) + " zł";
            tips.Add(new("overdue", "zalegle_rachunki", "remind", 85,
                $"{overdue.Count} {Plural(overdue.Count, "rachunek jest", "rachunki są", "rachunków jest")} po terminie — razem {sum}.",
                new() { ["liczba_rachunkow"] = overdue.Count.ToString(), ["kwota"] = sum }, "Rachunki", "/panel/billing"));
        }

        // Trenerzy zagrożeni odejściem (wysokie ryzyko).
        var high = (await GetRisksAsync()).Count(r => r.High);
        if (high > 0)
            tips.Add(new($"risk-{today:yyyy-MM-dd}", "ryzyko", "remind", 80,
                $"{high} {Plural(high, "trener może", "trenerów może", "trenerów może")} niedługo odejść. Warto się odezwać — lista z powodami czeka.",
                new() { ["liczba_trenerow"] = high.ToString() }, "Zobacz listę", "/panel/ryzyko"));

        // Rejestracje czekające w kolejce na uruchomienie.
        var queued = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Pending && t.QueuedReason != null);
        if (queued > 0)
            tips.Add(new("queued", "kolejka", "remind", 75,
                $"{queued} {Plural(queued, "rejestracja czeka", "rejestracje czekają", "rejestracji czeka")} w kolejce na uruchomienie.",
                new() { ["liczba_rejestracji"] = queued.ToString() }, "Trenerzy", "/panel/tenants"));

        // Nieudane kopie zapasowe z ostatniej doby.
        var dayAgo = now.AddDays(-1);
        var failedBackups = await db.BackupEntries.CountAsync(b => b.Status == BackupStatus.Failed && b.CreatedAt >= dayAgo);
        if (failedBackups > 0)
            tips.Add(new($"backups-{today:yyyy-MM-dd}", "kopie", "remind", 70,
                $"{failedBackups} {Plural(failedBackups, "kopia zapasowa się nie udała", "kopie zapasowe się nie udały", "kopii zapasowych się nie udało")} w ostatniej dobie.",
                new() { ["liczba_kopii"] = failedBackups.ToString() }, "Kopie", "/panel/backups"));

        // Powody do radości: nowe rejestracje, pierwsze płatności, aktywacje.
        var newTenants = await db.Tenants.AsNoTracking().Where(t => t.CreatedAt >= dayAgo && t.Status != TenantStatus.Destroyed)
            .OrderByDescending(t => t.CreatedAt).Select(t => new { t.Id, t.Slug, t.CompanyName }).ToListAsync();
        if (newTenants.Count == 1)
            tips.Add(new($"new-{newTenants[0].Id}", "nowa_rejestracja", "celebrate", 60,
                "Nowa rejestracja: [trener]! Witamy na pokładzie 🎉",
                new() { ["trener"] = "[trener]" }, "Zobacz", $"/panel/tenants/{newTenants[0].Id}",
                new() { ["[trener]"] = Display(newTenants[0].CompanyName, newTenants[0].Slug) }));
        else if (newTenants.Count > 1)
            tips.Add(new($"new-{today:yyyy-MM-dd}", "nowa_rejestracja", "celebrate", 60,
                $"{newTenants.Count} {Plural(newTenants.Count, "nowa rejestracja", "nowe rejestracje", "nowych rejestracji")} od wczoraj! 🎉",
                new() { ["liczba_rejestracji"] = newTenants.Count.ToString() }, "Trenerzy", "/panel/tenants"));

        var weekAgo = now.AddDays(-7);
        var firstPaid = await db.Tenants.AsNoTracking().Where(t => t.FirstPaidAt >= weekAgo)
            .OrderByDescending(t => t.FirstPaidAt).Select(t => new { t.Id, t.Slug, t.CompanyName }).Take(2).ToListAsync();
        foreach (var t in firstPaid)
            tips.Add(new($"paid-{t.Id}", "pierwsza_platnosc", "celebrate", 55,
                "Pierwsza płatność od [trener]! Hau, to się nazywa zaufanie 🎉",
                new() { ["trener"] = "[trener]" }, "Zobacz", $"/panel/tenants/{t.Id}",
                new() { ["[trener]"] = Display(t.CompanyName, t.Slug) }));

        var activated = await db.Tenants.AsNoTracking().Where(t => t.ActivatedAt >= weekAgo)
            .OrderByDescending(t => t.ActivatedAt).Select(t => new { t.Id, t.Slug, t.CompanyName }).Take(2).ToListAsync();
        foreach (var t in activated)
            tips.Add(new($"activated-{t.Id}", "aktywacja", "celebrate", 52,
                "Aktywacja: [trener] ma już klientów i pierwszy trening w aplikacji. 🎉",
                new() { ["trener"] = "[trener]" }, "Zobacz", $"/panel/tenants/{t.Id}",
                new() { ["[trener]"] = Display(t.CompanyName, t.Slug) }));

        // Do przejrzenia: nowe opinie, odpowiedzi w ankiecie, test pamięci nazwy po spotkaniach.
        var unread = await db.AppFeedbacks.CountAsync(f => !f.IsRead);
        if (unread > 0)
            tips.Add(new("feedback", "opinie", "think", 50,
                $"{unread} {Plural(unread, "nowa ocena aplikacji czeka", "nowe oceny aplikacji czekają", "nowych ocen aplikacji czeka")} na przeczytanie. Mogę je pogrupować i podsunąć odpowiedzi.",
                new() { ["liczba_ocen"] = unread.ToString() }, "Oceny", "/panel/feedback"));

        var surveys = await db.TrainerSurveyResponses.CountAsync(r => r.CreatedAt >= weekAgo);
        if (surveys > 0)
            tips.Add(new($"survey-{today:yyyy-MM-dd}", "ankieta", "think", 45,
                $"{surveys} {Plural(surveys, "nowa odpowiedź", "nowe odpowiedzi", "nowych odpowiedzi")} w ankiecie w tym tygodniu. Kliknij „Wygeneruj”, a podsumuję wnioski.",
                new() { ["liczba_odpowiedzi"] = surveys.ToString() }, "Ankieta", "/panel/ankieta"));

        var recall = await db.TrainerMeetings.CountAsync(m => m.NameRecalled == null && m.CreatedAt <= dayAgo && m.CreatedAt >= weekAgo);
        if (recall > 0)
            tips.Add(new($"recall-{today:yyyy-MM-dd}", "spotkania", "think", 40,
                $"Po {recall} {Plural(recall, "spotkaniu", "spotkaniach", "spotkaniach")} nie sprawdzono jeszcze, czy pamiętają nazwę. Dobry moment na krótki SMS.",
                new() { ["liczba_spotkan"] = recall.ToString() }, "Spotkania", "/panel/spotkania"));

        // Budżet AI blisko limitu.
        var s = await settings.GetAllAsync(K.AiEnabled, K.AiMonthlyBudgetUsd);
        if (s[K.AiEnabled] == "true" && decimal.TryParse(s[K.AiMonthlyBudgetUsd], NumberStyles.Number, CultureInfo.InvariantCulture, out var budget) && budget > 0)
        {
            var spent = await ai.SpentThisMonthUsdAsync();
            if (spent >= budget * 0.8m)
                tips.Add(new($"ai-budget-{now:yyyy-MM}", "budzet_ai", "think", 35,
                    $"Budżet AI: wydano {spent.ToString("0.##", Pl)} z {budget.ToString("0.##", Pl)} $ w tym miesiącu.",
                    new() { ["wydane_usd"] = spent.ToString("0.##", Pl), ["budzet_usd"] = budget.ToString("0.##", Pl) }, "Ustawienia AI", "/panel/asystent"));
        }

        // Poczta trenerów: błędy własnych skrzynek, limit naszej skrzynki, zmiany i stan.
        var mailTenants = await active.Select(t => new { t.Id, t.Slug, t.CompanyName, t.MailMode, t.MailOwnErrorAt, t.MailFallbacks }).ToListAsync();
        var sentToday = await mailRelay.SentTodayByTenantAsync();
        var mailChanges = (await db.TenantEvents.AsNoTracking()
                .Where(e => (e.EventType == TenantEventTypes.MailOwn || e.EventType == TenantEventTypes.MailPlatform) && e.OccurredAt >= weekAgo)
                .Select(e => new { e.TenantId, e.EventType, e.OccurredAt }).ToListAsync())
            .GroupBy(e => e.TenantId).Select(g => g.OrderByDescending(e => e.OccurredAt).First())
            .Select(e => new MailChange(e.TenantId, e.EventType == TenantEventTypes.MailOwn ? "own" : "platform", e.OccurredAt)).ToList();
        var mailTips = MailTips(
            mailTenants.Select(t => new MailTenant(t.Id, Display(t.CompanyName, t.Slug), t.MailMode, t.MailOwnErrorAt, t.MailFallbacks,
                sentToday.GetValueOrDefault(t.Id))).ToList(),
            mailChanges, await mailRelay.GetDailyLimitAsync(), now);
        tips.AddRange(mailTips.Where(t => t.Kind != MailStatusKind));

        if (tips.Count == 0)
        {
            var count = await active.CountAsync();
            tips.Add(new($"calm-{today:yyyy-MM-dd}", "spokoj", "happy", 5,
                $"Wszystko pod kontrolą: {count} {Plural(count, "aktywny trener", "aktywnych trenerów", "aktywnych trenerów")}, żadnych pilnych spraw. Merdam ogonem 🐾",
                new() { ["aktywni_trenerzy"] = count.ToString() }, null, null));
        }
        // Stan naszej skrzynki to informacja, nie sprawa do załatwienia — nie zastępuje „wszystko pod kontrolą”.
        tips.AddRange(mailTips.Where(t => t.Kind == MailStatusKind));
        return tips.OrderByDescending(t => t.Priority).ToList();
    }

    // ── Poczta trenerów ──

    /// <param name="Mode">„platform” (nasza skrzynka), „own” (własna skrzynka trenera), „none”; null — starsza wersja aplikacji.</param>
    /// <param name="SentToday">Ile maili poszło dziś przez naszą skrzynkę (także awaryjnie przy własnej).</param>
    public sealed record MailTenant(int Id, string Name, string? Mode, DateTime? OwnErrorAt, int? Fallbacks, int SentToday);
    public sealed record MailChange(int TenantId, string Mode, DateTime At);

    public const string MailStatusKind = "poczta_platformy";

    /// <summary>
    /// Podpowiedzi o poczcie: własne skrzynki z błędem (maile idą awaryjnie przez naszą), trenerzy blisko dziennego
    /// limitu naszej skrzynki, zmiany trybu z ostatniego tygodnia i stan — kto i ile wysyła przez naszą skrzynkę.
    /// </summary>
    public static List<Tip> MailTips(IReadOnlyList<MailTenant> tenants, IReadOnlyList<MailChange> changes, int dailyLimit, DateTime now)
    {
        var tips = new List<Tip>();
        var today = DateOnly.FromDateTime(now);
        var byId = tenants.ToDictionary(t => t.Id);
        Dictionary<string, string> Name(MailTenant t) => new() { ["[trener]"] = t.Name };

        // Własna skrzynka zgłasza błąd — maile idą awaryjnie przez naszą.
        var failing = tenants.Where(t => t.Mode == "own" && t.OwnErrorAt is { } at && now - at < TimeSpan.FromDays(3))
            .OrderByDescending(t => t.OwnErrorAt).ToList();
        if (failing.Count == 1)
        {
            var t = failing[0];
            var fallbacks = t.Fallbacks ?? 0;
            var facts = new Dictionary<string, string> { ["trener"] = "[trener]" };
            if (fallbacks > 0) facts["wyslane_awaryjnie"] = fallbacks.ToString();
            tips.Add(new($"mail-own-{t.Id}-{t.OwnErrorAt:yyyyMMddHH}", "poczta_blad", "remind", 78,
                $"Własna skrzynka [trener] zgłasza błąd — maile idą awaryjnie przez naszą skrzynkę" +
                (fallbacks > 0 ? $" (już {fallbacks})" : "") + ". Warto się odezwać, zanim limit się skończy.",
                facts, "Zobacz", $"/panel/tenants/{t.Id}", Name(t)));
        }
        else if (failing.Count > 1)
            tips.Add(new($"mail-own-{today:yyyy-MM-dd}-{failing.Count}", "poczta_blad", "remind", 78,
                $"{failing.Count} {Plural(failing.Count, "własna skrzynka trenera zgłasza", "własne skrzynki trenerów zgłaszają", "własnych skrzynek trenerów zgłasza")} błąd — " +
                "ich maile idą awaryjnie przez naszą skrzynkę.",
                new() { ["liczba_skrzynek"] = failing.Count.ToString() }, "Trenerzy", "/panel/tenants"));

        // Blisko dziennego limitu naszej skrzynki.
        if (dailyLimit > 0)
        {
            var near = tenants.Where(t => t.SentToday * 10 >= dailyLimit * 8).OrderByDescending(t => t.SentToday).ToList();
            if (near.Count > 0)
            {
                var top = near[0];
                var exhausted = top.SentToday >= dailyLimit;
                var facts = new Dictionary<string, string>
                {
                    ["trener"] = "[trener]", ["wyslane_dzis"] = top.SentToday.ToString(), ["limit_dzienny"] = dailyLimit.ToString(),
                };
                var text = exhausted
                    ? $"Dzienny limit naszej skrzynki wyczerpany: [trener] ({top.SentToday} z {dailyLimit}). Kolejne maile poczekają do jutra."
                    : $"[trener]: dziś już {top.SentToday} z {dailyLimit} maili przez naszą skrzynkę.";
                if (near.Count > 1)
                {
                    facts["blisko_limitu"] = near.Count.ToString();
                    text += $" Blisko limitu: {near.Count} {Plural(near.Count, "trener", "trenerów", "trenerów")}.";
                }
                text += " Warto zaproponować własną skrzynkę — podłącza się ją w ustawieniach poczty w aplikacji.";
                tips.Add(new($"mail-limit-{today:yyyy-MM-dd}-{top.Id}{(exhausted ? "-max" : "")}", "poczta_limit", exhausted ? "remind" : "think",
                    exhausted ? 76 : 48, text, facts, "Zobacz", $"/panel/tenants/{top.Id}", Name(top)));
            }
        }

        // Zmiany z ostatniego tygodnia: przejście na własną skrzynkę albo powrót na naszą.
        foreach (var c in changes.OrderByDescending(c => c.At).Take(2))
        {
            if (!byId.TryGetValue(c.TenantId, out var t)) continue;
            if (c.Mode == "own")
                tips.Add(new($"mail-change-{t.Id}-{c.At:yyyyMMdd}", "poczta_zmiana", "celebrate", 50,
                    "[trener] wysyła już maile z własnej skrzynki — nasza ma mniej pracy. 🎉",
                    new() { ["trener"] = "[trener]" }, "Zobacz", $"/panel/tenants/{t.Id}", Name(t)));
            else if (c.Mode == "platform")
                tips.Add(new($"mail-change-{t.Id}-{c.At:yyyyMMdd}", "poczta_zmiana", "think", 46,
                    "[trener] korzysta teraz z naszej skrzynki zamiast własnej.",
                    new() { ["trener"] = "[trener]" }, "Zobacz", $"/panel/tenants/{t.Id}", Name(t)));
        }

        // Stan: kto wysyła przez naszą skrzynkę i ile dziś.
        var onPlatform = tenants.Where(t => t.Mode == "platform" || (t.Mode is null && t.SentToday > 0)).ToList();
        if (onPlatform.Count > 0)
        {
            var total = tenants.Sum(t => t.SentToday);
            var top = tenants.Where(t => t.SentToday > 0).OrderByDescending(t => t.SentToday).FirstOrDefault();
            var facts = new Dictionary<string, string> { ["trenerzy_na_naszej_skrzynce"] = onPlatform.Count.ToString(), ["maile_dzis"] = total.ToString() };
            string text;
            Dictionary<string, string>? placeholders = null;
            if (onPlatform.Count == 1)
            {
                facts["trener"] = "[trener]";
                placeholders = Name(onPlatform[0]);
                text = total > 0
                    ? $"Z naszej skrzynki korzysta [trener] — dziś {total} {Plural(total, "mail", "maile", "maili")}."
                    : "Z naszej skrzynki korzysta [trener] — dziś jeszcze bez maili.";
            }
            else
            {
                text = $"Przez naszą skrzynkę wysyła {onPlatform.Count} {Plural(onPlatform.Count, "trener", "trenerów", "trenerów")}";
                if (top is null) text += " — dziś jeszcze bez maili.";
                else
                {
                    facts["trener"] = "[trener]";
                    facts["najwiecej"] = top.SentToday.ToString();
                    placeholders = Name(top);
                    text += $": dziś {total} {Plural(total, "mail", "maile", "maili")}, najwięcej [trener] ({top.SentToday}).";
                }
            }
            if (dailyLimit > 0) facts["limit_dzienny"] = dailyLimit.ToString();
            tips.Add(new($"mail-platform-{today:yyyy-MM-dd}", MailStatusKind, "think", 20, text, facts, "Limit poczty", "/panel/setup/isolation", placeholders));
        }
        return tips;
    }

    // ── Teksty od AI (z pamięcią na dzień, żeby nie płacić za ten sam dymek przy każdej stronie) ──

    private static readonly ConcurrentDictionary<string, string> PhraseCache = new();

    public async Task<string> PhraseAsync(Tip tip, bool useAi)
    {
        var text = tip.Fallback;
        if (useAi)
        {
            var key = $"{DateTime.UtcNow:yyyy-MM-dd}|{tip.Key}|{tip.Fallback}";
            if (!PhraseCache.TryGetValue(key, out var cached))
            {
                var phrase = await assistant.PhraseAsync(null, tip.Kind, tip.Facts, tip.Fallback, audience: "admin");
                cached = phrase.Text;
                if (phrase.FromAi)
                {
                    if (PhraseCache.Count > 500) PhraseCache.Clear();
                    PhraseCache[key] = cached;
                }
            }
            text = cached;
        }
        foreach (var (placeholder, value) in tip.Placeholders ?? [])
            text = text.Replace(placeholder, value);
        return text;
    }

    // ── Trenerzy zagrożeni odejściem ──

    public sealed record RiskInput(
        DateTime? ProvisionedAt, DateTime? ActivatedAt, DateTime? LastActivityAt, DateTime? TrialEndsAt,
        string BillingStatus, DateTime? FirstPaidAt, DateTime? CancelRequestedAt,
        int OverdueBills, decimal OverdueAmount, int? LowestRecentRating, bool Down,
        int? SessionsLast14, int? SessionsPrev14, int? Clients);

    public sealed record Risk(int TenantId, string Slug, string Name, string OwnerName, string OwnerEmail, string? Phone, string PlanId,
        int Score, List<string> Reasons, string Suggestion)
    {
        public bool High => Score >= RiskHigh;
        public string Level => High ? "wysokie" : "średnie";
    }

    public const int RiskHigh = 60;
    public const int RiskMedium = 30;

    /// <summary>
    /// Punkty ryzyka odejścia z prostych sygnałów (rezygnacja, zaległości, brak logowań, brak aktywacji,
    /// spadek liczby treningów, niska ocena, koniec okresu próbnego). Sugestia pochodzi z najcięższego powodu.
    /// </summary>
    public static (int Score, List<string> Reasons, string Suggestion) ScoreRisk(RiskInput i, DateTime now)
    {
        var found = new List<(int Points, string Reason, string Suggestion)>();

        if (i.CancelRequestedAt is { } cancel)
            found.Add((60, $"Zgłoszono rezygnację {cancel.ToLocalTime():dd.MM}", "Zadzwoń i zapytaj o powód rezygnacji — może da się pomóc albo zaproponować inny plan."));

        if (i.OverdueBills > 0)
            found.Add((30, $"{i.OverdueBills} {Plural(i.OverdueBills, "rachunek", "rachunki", "rachunków")} po terminie ({i.OverdueAmount.ToString("#,0.##", Pl)} zł)", "Przypomnij o płatności osobiście — e-maile z przypomnieniem już poszły."));
        else if (i.BillingStatus == "past_due")
            found.Add((30, "Nieudana płatność kartą", "Poproś o aktualizację karty w Stripe — to częsty powód „przypadkowego” odejścia."));

        if (i.LastActivityAt is { } last)
        {
            var days = (int)(now - last).TotalDays;
            if (days >= 21) found.Add((40, $"Brak aktywności od {days} dni", "Zadzwoń z pytaniem, czy wszystko działa i czego brakuje."));
            else if (days >= 10) found.Add((25, $"Brak aktywności od {days} dni", "Wyślij krótką wiadomość z jedną praktyczną wskazówką."));
        }

        if (i.ActivatedAt is null && i.ProvisionedAt is { } provisioned && (now - provisioned).TotalDays >= 7)
        {
            var days = (int)(now - provisioned).TotalDays;
            found.Add((25, $"Bez aktywacji po {days} dniach ({i.Clients ?? 0} {Plural(i.Clients ?? 0, "klient", "klientów", "klientów")})",
                "Pomóż z aktywacją: dodanie 3 klientów i pierwszego treningu — zaproponuj 15 minut wspólnej konfiguracji."));
        }

        if (i.SessionsPrev14 is >= 5 and var prev && i.SessionsLast14 is { } recent && recent <= prev / 2)
            found.Add((25, $"Treningów w ostatnich 14 dniach: {recent} (wcześniej {prev})", "Zapytaj, co się zmieniło — może klienci odeszli albo coś przestało działać."));

        if (i.LowestRecentRating is { } rating and <= 2)
            found.Add((20, $"Niska ocena aplikacji ({rating}/5)", "Odpowiedz na opinię i dopytaj, co poprawić."));

        if (i.TrialEndsAt is { } trialEnd && i.FirstPaidAt is null && i.BillingStatus != "active")
        {
            var days = (int)Math.Ceiling((trialEnd - now).TotalDays);
            if (days is >= 0 and <= 5)
                found.Add((15, days == 0 ? "Okres próbny kończy się dziś" : $"Okres próbny kończy się za {days} {Plural(days, "dzień", "dni", "dni")}", "Przypomnij o wyborze planu i zapytaj, czy czegoś brakuje do decyzji."));
        }

        if (i.Down)
            found.Add((10, "Aplikacja teraz nie odpowiada", "Najpierw napraw instancję — awaria to zły moment na rozmowę o planie."));

        var score = Math.Min(100, found.Sum(f => f.Points));
        var top = found.OrderByDescending(f => f.Points).FirstOrDefault();
        return (score, found.OrderByDescending(f => f.Points).Select(f => f.Reason).ToList(), top.Suggestion ?? "");
    }

    /// <summary>Aktywni trenerzy z ryzykiem co najmniej średnim, od najbardziej zagrożonych.</summary>
    public async Task<List<Risk>> GetRisksAsync()
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        await using var db = await dbFactory.CreateDbContextAsync();
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).ToListAsync();
        if (tenants.Count == 0) return [];

        var overdue = (await db.TenantBills.AsNoTracking().Where(b => b.Status == TenantBillStatus.Issued && b.DueDate < today)
                .Select(b => new { b.TenantId, b.Amount }).ToListAsync())
            .GroupBy(b => b.TenantId).ToDictionary(g => g.Key, g => (Count: g.Count(), Sum: g.Sum(x => x.Amount)));
        var since = now.AddDays(-60);
        var ratings = (await db.AppFeedbacks.AsNoTracking().Where(f => f.CreatedAt >= since).Select(f => new { f.TenantId, f.Rating }).ToListAsync())
            .GroupBy(f => f.TenantId).ToDictionary(g => g.Key, g => g.Min(x => x.Rating));
        var from = today.AddDays(-35);
        var snapshots = (await db.TenantMetricSnapshots.AsNoTracking().Where(x => x.Date >= from).ToListAsync())
            .GroupBy(x => x.TenantId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Date).ToList());

        var risks = new List<Risk>();
        foreach (var t in tenants)
        {
            int? last14 = null, prev14 = null;
            if (snapshots.TryGetValue(t.Id, out var list) && list.Count >= 2)
            {
                var latest = list[^1];
                var mid = list.LastOrDefault(x => x.Date <= latest.Date.AddDays(-14));
                var old = list.LastOrDefault(x => x.Date <= latest.Date.AddDays(-28));
                if (mid is not null) last14 = Math.Max(0, latest.SessionsTotal - mid.SessionsTotal);
                if (mid is not null && old is not null) prev14 = Math.Max(0, mid.SessionsTotal - old.SessionsTotal);
            }
            var o = overdue.GetValueOrDefault(t.Id);
            var input = new RiskInput(t.ProvisionedAt, t.ActivatedAt, t.LastActivityAt, t.TrialEndsAt, t.BillingStatus, t.FirstPaidAt,
                t.CancelRequestedAt, o.Count, o.Sum, ratings.TryGetValue(t.Id, out var r) ? r : null, t.IsHealthy == false,
                last14, prev14, t.MetricsClients);
            var (score, reasons, suggestion) = ScoreRisk(input, now);
            if (score < RiskMedium) continue;
            risks.Add(new(t.Id, t.Slug, Display(t.CompanyName, t.Slug), t.OwnerName, t.OwnerEmail, t.Phone, t.PlanId, score, reasons, suggestion));
        }
        return risks.OrderByDescending(r => r.Score).ToList();
    }

    // ── Pomocnicze ──

    private static string Display(string company, string slug) => string.IsNullOrWhiteSpace(company) ? slug : company.Trim();

    private static string Duration(int minutes) =>
        minutes < 90 ? $"{minutes} min" : minutes < 48 * 60 ? $"{minutes / 60} godz." : $"{minutes / 1440} dni";

    /// <summary>Polska odmiana: 1 rachunek, 2–4 rachunki, 5+ rachunków (12–14 zawsze w trzeciej formie).</summary>
    public static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
