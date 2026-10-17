using System.Globalization;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Rules;

namespace PTScheduler.Application.Progress;

/// <summary>Wszystko, co wiemy o kliencie z różnych kart profilu — wejście „W skrócie”.</summary>
public sealed record ClientSnapshotInput(
    ClientProgressDto? Progress,
    WellbeingSummary? Wellbeing,
    ClientChurnRules.Result? Risk,
    int HealthFlags,
    int? PackageRemaining,
    int? DaysSinceLastVisit);

/// <summary>Jedna obserwacja. <paramref name="Tone"/>: „good”, „bad” albo „info”.</summary>
public sealed record SnapshotPoint(string Kind, string Tone, string Text);

/// <param name="Headline">Jedno zdanie bez AI: dobre strony, „ale” i to, co niepokoi (rada jest osobno).</param>
/// <param name="Fallback">Tekst podstawowy dla AI: nagłówek + rada.</param>
public sealed record ClientSnapshotResult(List<SnapshotPoint> Points, string Advice, Dictionary<string, string> Facts, string Fallback, string Headline = "")
{
    public bool HasData => Points.Count > 0;
}

/// <summary>
/// „W skrócie” na górze profilu klienta: łączy postępy, frekwencję, samopoczucie z ankiet, ryzyko odejścia,
/// uwagi zdrowotne i stan pakietu w kilka obserwacji i jedną radę (co zrobić teraz). Tekst podstawowy dla AI —
/// bez imienia klienta i w formach neutralnych płciowo; liczby wyłącznie stąd.
/// </summary>
public static class ClientSnapshot
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static ClientSnapshotResult Compute(ClientSnapshotInput x)
    {
        var points = new List<SnapshotPoint>();
        var facts = new Dictionary<string, string>();
        var p = x.Progress;

        // ── Regularność ──
        if (x.DaysSinceLastVisit is >= 14)
        {
            facts["dni_od_ostatniego_treningu"] = x.DaysSinceLastVisit.Value.ToString();
            points.Add(new("przerwa", "bad", $"Ostatni trening {x.DaysSinceLastVisit} dni temu."));
        }
        else if (p is not null && p.Attended30 + p.Missed30 > 0)
        {
            facts["treningi_30_dni"] = p.Attended30.ToString();
            if (p.Missed30 > 0) facts["nieobecnosci_30_dni"] = p.Missed30.ToString();
            if (p.Missed30 >= 2 && p.Missed30 * 3 >= p.Attended30)
                points.Add(new("frekwencja", "bad", $"W ostatnich 30 dniach {p.Attended30} {Plural(p.Attended30, "trening", "treningi", "treningów")} i {p.Missed30} {Plural(p.Missed30, "nieobecność", "nieobecności", "nieobecności")}."));
            else if (p.Attended30 >= 4)
                points.Add(new("frekwencja", "good", $"Trenuje regularnie: {p.Attended30} {Plural(p.Attended30, "trening", "treningi", "treningów")} w 30 dni."));
        }

        // ── Postępy ──
        if (p?.Strength.FirstOrDefault() is { } best)
        {
            facts["najwiekszy_postep"] = $"{best.Exercise} +{N(best.DeltaKg)} kg";
            points.Add(new("sila", "good", $"Ciężary rosną — największy postęp: {best.Exercise} +{N(best.DeltaKg)} kg."));
        }
        var stuck = p?.Insights.Select(i => i.Exercise).Distinct().Count() ?? 0;
        if (stuck > 0)
        {
            facts["cwiczenia_do_poprawy"] = stuck.ToString();
            points.Add(new("plan", "info", $"{stuck} {Plural(stuck, "ćwiczenie wymaga", "ćwiczenia wymagają", "ćwiczeń wymaga")} uwagi w planie."));
        }
        if (p?.WeightFromKg is { } wf && p.WeightToKg is { } wt && Math.Abs(wt - wf) >= 1)
        {
            facts["masa_ciala_zmiana_kg"] = Signed(wt - wf);
            points.Add(new("cialo", "info", $"Masa ciała {Signed(wt - wf)} kg od pierwszego pomiaru."));
        }

        // ── Samopoczucie i zdrowie ──
        var w = x.Wellbeing;
        if (w is not null && w.Points.Count >= 4)
        {
            if (w.Direction == TrendDirection.Down)
            {
                facts["samopoczucie"] = "spada";
                points.Add(new("samopoczucie", "bad", "Samopoczucie z ankiet po treningu spada."));
            }
            else if (w.Direction == TrendDirection.Up)
            {
                facts["samopoczucie"] = "rośnie";
                points.Add(new("samopoczucie", "good", "Samopoczucie z ankiet po treningu rośnie."));
            }
            if (w.PainInLast5 > 0)
            {
                facts["bol_w_ostatnich_5_ankietach"] = w.PainInLast5.ToString();
                points.Add(new("bol", "bad", $"Ból zgłoszony w {w.PainInLast5} z ostatnich 5 ankiet."));
            }
        }
        if (x.HealthFlags > 0)
        {
            facts["uwagi_zdrowotne_do_planu"] = x.HealthFlags.ToString();
            points.Add(new("zdrowie", "bad", $"{x.HealthFlags} {Plural(x.HealthFlags, "uwaga", "uwagi", "uwag")} z ankiety zdrowotnej dotyczy ćwiczeń w planie."));
        }

        // ── Pakiet ──
        if (x.PackageRemaining is 0)
        {
            facts["pakiet"] = "skończony";
            points.Add(new("pakiet", "info", "Pakiet się skończył."));
        }
        else if (x.PackageRemaining is int left and <= 2)
        {
            facts["treningi_w_pakiecie"] = left.ToString();
            points.Add(new("pakiet", "info", $"W pakiecie {(left == 1 ? "został 1 trening" : $"zostały {left} treningi")}."));
        }

        // ── Ryzyko odejścia ──
        if (x.Risk is { IsRisk: true } risk)
        {
            facts["ryzyko_odejscia"] = risk.IsHigh ? "wysokie" : "średnie";
            points.Insert(0, new("ryzyko", "bad", $"{(risk.IsHigh ? "Wysokie" : "Średnie")} ryzyko odejścia."));
        }

        var advice = Advice(x, stuck);
        facts["rada"] = advice;
        if (points.Count == 0) return new([], advice, facts, "");

        // Obserwacje: najpierw to, co niepokoi, potem informacje i dobre wiadomości.
        var ordered = points.OrderBy(pt => pt.Tone == "bad" ? 0 : pt.Tone == "info" ? 1 : 2).ToList();
        var headline = Headline(points, x);
        return new(ordered, advice, facts, headline + " " + advice, headline);
    }

    /// <summary>„Trenuje regularnie i ciężary rosną, ale samopoczucie spada.” — najwyżej dwie dobre i dwie złe rzeczy.</summary>
    private static string Headline(List<SnapshotPoint> points, ClientSnapshotInput x)
    {
        string? Short(SnapshotPoint pt) => pt.Kind switch
        {
            "frekwencja" => pt.Tone == "good" ? "trenuje regularnie" : "często opuszcza treningi",
            "przerwa" => $"od {x.DaysSinceLastVisit} dni nie ma treningu",
            "sila" => "ciężary rosną",
            "plan" => "część ćwiczeń stoi w miejscu",
            "samopoczucie" => pt.Tone == "good" ? "samopoczucie rośnie" : "samopoczucie spada",
            "bol" => "w ankietach pojawia się ból",
            "zdrowie" => "ankieta zdrowotna ma uwagi do planu",
            "pakiet" => x.PackageRemaining == 0 ? "pakiet się skończył" : "pakiet zaraz się skończy",
            "ryzyko" => "jest ryzyko odejścia",
            _ => null
        };
        var good = points.Where(p => p.Tone == "good").Select(Short).OfType<string>().Take(2).ToList();
        var bad = points.Where(p => p.Tone != "good").OrderBy(p => p.Tone == "bad" ? 0 : 1).Select(Short).OfType<string>().Take(2).ToList();
        string Join(List<string> l) => string.Join(" i ", l);
        var text = (good.Count, bad.Count) switch
        {
            (> 0, > 0) => $"{Join(good)}, ale {Join(bad)}.",
            (> 0, 0) => $"{Join(good)} — wszystko idzie w dobrą stronę.",
            (0, > 0) => $"{Join(bad)}.",
            _ => points[0].Text,
        };
        return char.ToUpper(text[0], Pl) + text[1..];
    }

    /// <summary>Jedna rada — najpilniejsza rzecz do zrobienia.</summary>
    private static string Advice(ClientSnapshotInput x, int stuck)
    {
        if (x.Risk is { IsRisk: true, Top: { } top }) return top.Suggestion;
        if (x.HealthFlags > 0 || x.Wellbeing?.PainInLast5 > 0) return "Przed kolejnym treningiem zapytaj o ból i przejrzyj plan pod kątem uwag zdrowotnych.";
        if (x.Wellbeing?.IsDropping == true) return "Zapytaj o sen i regenerację — może przyda się lżejszy tydzień.";
        if (x.PackageRemaining is int left and <= 2) return "To dobry moment, żeby zaproponować kolejny pakiet.";
        if (stuck > 0) return "Zmień bodziec w ćwiczeniach, które stoją w miejscu — szczegóły w karcie postępów.";
        return "Pochwal konkretny postęp — to najlepiej motywuje.";
    }

    private static string N(decimal v) => v.ToString("0.##", Pl);
    private static string Signed(decimal v) => (v > 0 ? "+" : v < 0 ? "−" : "") + N(Math.Abs(v));

    private static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
