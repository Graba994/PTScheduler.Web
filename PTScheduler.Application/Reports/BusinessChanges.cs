using System.Globalization;
using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Reports;

/// <summary>Jedna zmiana w biznesie trenera. <paramref name="Tone"/>: „good”, „bad” albo „info”.</summary>
public sealed record BusinessChange(string Kind, string Tone, string Text, int Weight, string? Href = null);

public sealed record BusinessChangesResult(List<BusinessChange> Items, Dictionary<string, string> Facts, string Fallback);

/// <summary>
/// „Co się zmieniło” na górze raportu biznesowego: z policzonego raportu wybiera kilka najważniejszych zmian
/// (retencja, przychód i jego przyczyna, nowi i utraceni klienci, obłożenie, odwołania, kończące się pakiety)
/// i układa z nich tekst podstawowy. AI może go przeredagować, ale tylko z tych faktów — imion klientów tu nie ma.
/// </summary>
public static class BusinessChanges
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Od ilu punktów procentowych zmiana retencji jest warta uwagi.</summary>
    public const int RetentionThresholdPp = 5;
    /// <summary>Od ilu procent zmiana przychodu jest warta uwagi.</summary>
    public const int RevenueThresholdPercent = 10;

    public static BusinessChangesResult Compute(BusinessReportDto r)
    {
        var items = new List<BusinessChange>();
        var facts = new Dictionary<string, string> { ["okres_dni"] = r.PeriodDays.ToString() };

        // ── Retencja ──
        if (r.RetentionPercent is int ret && r.RetentionPercentPrevious is int retPrev && Math.Abs(ret - retPrev) >= RetentionThresholdPp)
        {
            var diff = ret - retPrev;
            facts["retencja_proc"] = ret.ToString();
            facts["retencja_zmiana_pp"] = (diff > 0 ? "+" : "−") + Math.Abs(diff);
            var why = diff < 0 && r.LostClients.Count > 0
                ? $" — {r.LostClients.Count} {Plural(r.LostClients.Count, "osoba przestała", "osoby przestały", "osób przestało")} przychodzić"
                : "";
            items.Add(new("retencja", diff > 0 ? "good" : "bad",
                $"Retencja {(diff > 0 ? "wzrosła" : "spadła")} o {Math.Abs(diff)} pp, do {ret}%{why}.", 90 + Math.Abs(diff)));
        }
        else if (r.LostClients.Count > 0)
        {
            items.Add(new("utraceni", "bad",
                $"{r.LostClients.Count} {Plural(r.LostClients.Count, "osoba przestała", "osoby przestały", "osób przestało")} przychodzić — warto się odezwać.", 70 + 5 * r.LostClients.Count));
        }
        if (r.LostClients.Count > 0) facts["przestalo_przychodzic"] = r.LostClients.Count.ToString();

        // ── Przychód i jego przyczyna ──
        if (r.RevenuePrevious > 0 || r.Revenue > 0)
        {
            var change = r.RevenuePrevious > 0 ? (int)Math.Round((r.Revenue - r.RevenuePrevious) / r.RevenuePrevious * 100) : 100;
            if (Math.Abs(change) >= RevenueThresholdPercent)
            {
                facts["przychod"] = Money(r.Revenue);
                facts["przychod_poprzednio"] = Money(r.RevenuePrevious);
                if (r.RevenuePrevious > 0) facts["przychod_zmiana_proc"] = (change > 0 ? "+" : "−") + Math.Abs(change) + "%";
                var reason = RevenueReason(r, change > 0);
                if (reason is not null) facts["przyczyna"] = reason;
                var text = r.RevenuePrevious > 0
                    ? $"Przychód {(change > 0 ? "wzrósł" : "spadł")} o {Math.Abs(change)}% (do {Money(r.Revenue)})"
                    : $"Przychód: {Money(r.Revenue)} — w poprzednim okresie nie było wpłat";
                items.Add(new("przychod", change > 0 ? "good" : "bad", text + (reason is not null ? $" — {reason}." : "."), 80 + Math.Min(40, Math.Abs(change) / 2)));
            }
        }

        // ── Nowi klienci ──
        if (r.NewClients > 0)
        {
            facts["nowi_klienci"] = r.NewClients.ToString();
            items.Add(new("nowi", "good", $"{r.NewClients} {Plural(r.NewClients, "nowa osoba", "nowe osoby", "nowych osób")} na pierwszym treningu.", 50 + 5 * r.NewClients));
        }

        // ── Obłożenie ──
        if (r.PastOccupancy.Percent is int occ && r.PastOccupancy.AvailableMinutes > 0)
        {
            facts["oblozenie_proc"] = occ.ToString();
            var freeDays = r.OccupancyByWeekday.Where(d => d.AvailableMinutes > 0 && d.Percent is < 40)
                .OrderBy(d => d.Percent).Take(2).Select(d => DayName(d.Day)).ToList();
            if (occ < 40)
            {
                if (freeDays.Count > 0) facts["najluzniejsze_dni"] = string.Join(", ", freeDays);
                items.Add(new("oblozenie", "info",
                    $"Obłożenie grafiku {occ}%{(freeDays.Count > 0 ? $" — najwięcej wolnego {string.Join(" i ", freeDays)}" : "")}. Te godziny można sprzedać.", 55, "/trainer/availability"));
            }
            else if (occ >= 75)
            {
                items.Add(new("oblozenie", "good", $"Grafik zajęty w {occ}% — to dobry moment na wyższe ceny albo większe pakiety.", 60));
            }
            if (r.UpcomingOccupancy.Percent is int next && r.UpcomingOccupancy.AvailableMinutes > 0 && occ - next >= 15)
            {
                facts["oblozenie_najblizsze_2_tyg_proc"] = next.ToString();
                items.Add(new("pusto", "bad", $"Najbliższe 2 tygodnie są zajęte tylko w {next}% (wcześniej {occ}%) — warto przypomnieć się klientom.", 75));
            }
        }

        // ── Odwołania ──
        if (r.CancellationRatePercent is int cr && cr >= 15 && r.CancelledCount + r.NoShowCount >= 3)
        {
            facts["odwolania_proc"] = cr.ToString();
            var slot = r.TopCancelledSlots.FirstOrDefault(s => s.Cancelled >= 2);
            if (slot is not null) facts["najczesciej_odwolywane"] = $"{DayName(slot.Day)} {slot.Hour}:00";
            items.Add(new("odwolania", "bad",
                $"{cr}% wizyt odwołanych albo nieodbytych{(slot is not null ? $" — najczęściej {DayName(slot.Day)} o {slot.Hour}:00" : "")}.", 60 + cr / 2, "/trainer/availability"));
        }

        // ── Pakiety do odnowienia i zaległości ──
        if (r.Forecast.EndingPackages > 0)
        {
            facts["konczace_sie_pakiety"] = r.Forecast.EndingPackages.ToString();
            items.Add(new("pakiety", "info",
                $"{r.Forecast.EndingPackages} {Plural(r.Forecast.EndingPackages, "pakiet zaraz się kończy", "pakiety zaraz się kończą", "pakietów zaraz się kończy")} — zaproponuj kolejne.", 50 + 3 * r.Forecast.EndingPackages));
        }
        if (r.Forecast.UnpaidAmount > 0)
        {
            facts["nieoplacone"] = Money(r.Forecast.UnpaidAmount);
            items.Add(new("nieoplacone", "bad", $"Nieopłacone pakiety: {Money(r.Forecast.UnpaidAmount)}.", 45, "/finance"));
        }

        items = items.OrderByDescending(i => i.Weight).Take(5).ToList();
        if (items.Count == 0)
        {
            const string calm = "Bez większych zmian względem poprzedniego okresu — biznes idzie równym tempem.";
            return new([], facts, calm);
        }
        // Tekst: najważniejsza zmiana + ile jest dobrych wiadomości i spraw do uwagi (szczegóły są na liście pod spodem).
        var rest = items.Skip(1).ToList();
        var good = rest.Count(i => i.Tone == "good");
        var attention = rest.Count - good;
        if (good > 0) facts["dobre_wiadomosci"] = good.ToString();
        if (attention > 0) facts["sprawy_do_uwagi"] = attention.ToString();
        var tail = (good, attention) switch
        {
            (0, 0) => "",
            (> 0, 0) => $" Poza tym {good} {Plural(good, "dobra wiadomość", "dobre wiadomości", "dobrych wiadomości")} — szczegóły poniżej.",
            (0, > 0) => $" Poza tym {attention} {Plural(attention, "sprawa", "sprawy", "spraw")} do uwagi — szczegóły poniżej.",
            _ => $" Poza tym {good} {Plural(good, "dobra wiadomość", "dobre wiadomości", "dobrych wiadomości")} i {attention} {Plural(attention, "sprawa", "sprawy", "spraw")} do uwagi — szczegóły poniżej.",
        };
        return new(items, facts, items[0].Text + tail);
    }

    /// <summary>Dlaczego zmienił się przychód: więcej/mniej płacących osób czy większe/mniejsze zakupy na osobę.</summary>
    private static string? RevenueReason(BusinessReportDto r, bool up)
    {
        var payingPrev = r.AvgRevenuePerClientPrevious > 0 ? (int)Math.Round(r.RevenuePrevious / r.AvgRevenuePerClientPrevious) : 0;
        var payingDiff = r.PayingClients - payingPrev;
        var avgChange = r.AvgRevenuePerClientPrevious > 0 ? (r.AvgRevenuePerClient - r.AvgRevenuePerClientPrevious) / r.AvgRevenuePerClientPrevious : 0;
        if (up && avgChange >= 0.1m && payingDiff <= 0) return "dzięki większym zakupom na klienta";
        if (up && payingDiff > 0) return "bo płaci więcej osób";
        if (!up && payingDiff < 0) return "bo płaci mniej osób";
        if (!up && avgChange <= -0.1m) return "przez mniejsze zakupy na klienta";
        return null;
    }

    private static string DayName(string shortName) => shortName switch
    {
        "Pon" => "w poniedziałki",
        "Wt" => "we wtorki",
        "Śr" => "w środy",
        "Czw" => "w czwartki",
        "Pt" => "w piątki",
        "Sob" => "w soboty",
        "Niedz" => "w niedziele",
        _ => shortName
    };

    private static string Money(decimal v) => v.ToString("#,0", Pl).Replace(' ', ' ') + " zł";

    private static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
