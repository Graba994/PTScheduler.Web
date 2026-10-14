using System.Globalization;
using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Progress;

/// <summary>
/// Fakty i tekst podstawowy podsumowania postępów — dla klienta („Twoje postępy”) i dla trenera
/// (asystent w profilu klienta). AI może tekst przeredagować, ale tylko na podstawie tych faktów:
/// każda liczba, którą wolno mu użyć, jest tu podana wprost (także różnice, np. „+10 kg”).
/// </summary>
public static class ProgressSummaryText
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public sealed record Summary(Dictionary<string, string> Facts, string Fallback);

    public static Summary ForClient(ClientProgressDto p)
    {
        var facts = BaseFacts(p);
        var parts = new List<string> { Intro(p, "Masz za sobą już") };
        var top = p.Strength.Take(2).Select(s => $"{s.Exercise}: {N(s.FromKg)} → {N(s.ToKg)} kg").ToList();
        if (top.Count > 0) parts.Add(string.Join(", ", top) + ".");
        if (Body(p) is { } body) parts.Add(Capitalize(body) + ".");
        if (top.Count == 0 && Body(p) is null) parts.Add("Tak trzymać — każdy trening się liczy.");
        return new(facts, string.Join(" ", parts));
    }

    public static Summary ForTrainer(ClientProgressDto p)
    {
        var facts = BaseFacts(p);
        facts["uwagi"] = p.Insights.Count.ToString();
        var parts = new List<string> { Intro(p, "Za klientem") };
        if (p.Strength.FirstOrDefault() is { } best) parts.Add($"Największy postęp: {best.Exercise} +{N(best.DeltaKg)} kg.");
        if (p.Missed30 > 0) parts.Add($"W ostatnich 30 dniach {p.Attended30} {Plural(p.Attended30, "obecność", "obecności", "obecności")} i {p.Missed30} {Plural(p.Missed30, "nieobecność", "nieobecności", "nieobecności")}.");
        var exercises = p.Insights.Select(i => i.Exercise).Distinct().Count();
        parts.Add(exercises > 0
            ? $"{exercises} {Plural(exercises, "ćwiczenie wymaga", "ćwiczenia wymagają", "ćwiczeń wymaga")} uwagi — szczegóły poniżej."
            : "Plan idzie zgodnie z założeniami.");
        return new(facts, string.Join(" ", parts));
    }

    private static Dictionary<string, string> BaseFacts(ClientProgressDto p)
    {
        var f = new Dictionary<string, string>
        {
            ["liczba_treningow"] = p.CompletedSessions.ToString(),
            ["dni_z_zapisem_cwiczen"] = p.WorkoutDays.ToString(),
        };
        if (p.Since is { } since) f["od"] = since.ToString("d MMMM yyyy", Pl);
        for (var i = 0; i < Math.Min(3, p.Strength.Count); i++)
        {
            var s = p.Strength[i];
            f[$"cwiczenie_{i + 1}"] = s.Exercise;
            f[$"cwiczenie_{i + 1}_kg"] = $"{N(s.FromKg)} → {N(s.ToKg)} (+{N(s.DeltaKg)})";
        }
        if (p.WeightFromKg is { } wf && p.WeightToKg is { } wt) f["masa_ciala_kg"] = $"{N(wf)} → {N(wt)} ({Signed(wt - wf)})";
        if (p.WaistFromCm is { } af && p.WaistToCm is { } at) f["talia_cm"] = $"{N(af)} → {N(at)} ({Signed(at - af)})";
        f["obecnosci_30_dni"] = p.Attended30.ToString();
        f["nieobecnosci_30_dni"] = p.Missed30.ToString();
        return f;
    }

    private static string Intro(ClientProgressDto p, string lead)
    {
        var since = p.Since is { } d ? $" (od {d.ToString("d MMMM yyyy", Pl)})" : "";
        return p.CompletedSessions > 0
            ? $"{lead} {p.CompletedSessions} {Plural(p.CompletedSessions, "trening", "treningi", "treningów")}{since}."
            : $"{lead} {p.WorkoutDays} {Plural(p.WorkoutDays, "dzień", "dni", "dni")} zapisanych treningów{since}.";
    }

    private static string? Body(ClientProgressDto p)
    {
        var parts = new List<string>();
        if (p.WeightFromKg is { } wf && p.WeightToKg is { } wt && wf != wt) parts.Add($"masa ciała {Signed(wt - wf)} kg");
        if (p.WaistFromCm is { } af && p.WaistToCm is { } at && af != at) parts.Add($"talia {Signed(at - af)} cm");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string N(decimal v) => v.ToString("0.##", Pl);
    private static string Signed(decimal v) => (v > 0 ? "+" : v < 0 ? "−" : "") + N(Math.Abs(v));
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Pl) + s[1..];

    public static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
