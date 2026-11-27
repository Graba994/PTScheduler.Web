using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Scheduling;

/// <summary>
/// Wolne okienka w grafiku trenera i kogo na nie zaprosić — same reguły, bez AI.
/// Okienko to ciąg wolnych slotów dostępności (co najmniej jedna wizyta); kandydaci to klienci,
/// którzy zwykle trenują o tej porze, nie mają nic zaplanowanego w najbliższych dniach
/// i nie dostali niedawno zaproszenia.
/// </summary>
public static class FreeSlots
{
    public sealed record Window(DateTime Start, DateTime End)
    {
        public int Minutes => (int)(End - Start).TotalMinutes;
    }

    /// <param name="Hits">Ile wizyt w ostatnich tygodniach zaczęło się o tej porze (± godzina).</param>
    /// <param name="SameWeekday">Ile z nich w ten sam dzień tygodnia.</param>
    public sealed record Candidate(int ClientId, int Hits, int SameWeekday, int SessionTypeId, DateTime SuggestedStart);

    /// <summary>Historia klienta do dopasowania: odbyte wizyty, najbliższa zaplanowana, ostatnie zaproszenie.</summary>
    public sealed record ClientHistory(int ClientId, IReadOnlyList<(DateTime Start, int SessionTypeId)> Completed,
        DateTime? NextScheduled, DateTime? LastInvitedUtc);

    /// <summary>
    /// Skleja wolne sloty w okienka (sloty nachodzą na siebie co krok siatki, więc łączymy te, które się stykają lub zazębiają).
    /// Okienka krótsze niż <paramref name="minMinutes"/> pomijamy.
    /// </summary>
    public static List<Window> Merge(IEnumerable<AvailableSlotDto> slots, DateTime notBefore, int minMinutes = 60)
    {
        var free = slots.Where(s => s.IsAvailable && s.Start >= notBefore).OrderBy(s => s.Start).ToList();
        var result = new List<Window>();
        DateTime? start = null, end = null;
        foreach (var s in free)
        {
            if (start is null) { start = s.Start; end = s.End; continue; }
            if (s.Start <= end) { if (s.End > end) end = s.End; continue; }
            result.Add(new(start.Value, end!.Value));
            start = s.Start; end = s.End;
        }
        if (start is not null) result.Add(new(start.Value, end!.Value));
        return result.Where(w => w.Minutes >= minMinutes).ToList();
    }

    /// <summary>
    /// Najlepsi kandydaci na okienko. Pomija osoby z wizytą w ciągu <paramref name="busyDays"/> dni
    /// i zaproszone w ostatnich <paramref name="cooldownDays"/> dniach. Proponowana godzina to najczęstsza
    /// godzina klienta mieszcząca się w okienku (albo początek okienka).
    /// </summary>
    public static List<Candidate> Rank(Window window, IEnumerable<ClientHistory> clients, DateTime now, DateTime utcNow,
        int sessionMinutes, int take = 5, int busyDays = 7, int cooldownDays = 5)
    {
        var from = TimeOnly.FromDateTime(window.Start).AddMinutes(-60);
        var to = TimeOnly.FromDateTime(window.End.AddMinutes(-sessionMinutes)).AddMinutes(60);
        var list = new List<Candidate>();
        foreach (var c in clients)
        {
            if (c.NextScheduled is { } next && next >= now && next < now.AddDays(busyDays)) continue;
            if (c.LastInvitedUtc is { } inv && utcNow - inv < TimeSpan.FromDays(cooldownDays)) continue;
            var matching = c.Completed.Where(x => Within(TimeOnly.FromDateTime(x.Start), from, to)).ToList();
            if (matching.Count < 2) continue;
            var same = matching.Count(x => x.Start.DayOfWeek == window.Start.DayOfWeek);
            var favourite = matching.GroupBy(x => TimeOnly.FromDateTime(x.Start)).OrderByDescending(g => g.Count()).First().Key;
            var start = window.Start.Date.Add(favourite.ToTimeSpan());
            if (start < window.Start || start.AddMinutes(sessionMinutes) > window.End) start = window.Start;
            var type = matching.OrderByDescending(x => x.Start).First().SessionTypeId;
            list.Add(new(c.ClientId, matching.Count, same, type, start));
        }
        return list.OrderByDescending(c => c.SameWeekday).ThenByDescending(c => c.Hits).Take(take).ToList();
    }

    private static bool Within(TimeOnly t, TimeOnly from, TimeOnly to) => from <= to ? t >= from && t <= to : t >= from || t <= to;

    /// <summary>„jutro (sobota) 10:00–12:00” albo „w poniedziałek 20.10, 10:00–12:00”.</summary>
    public static string Describe(Window w, DateTime now)
    {
        var pl = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        var days = (w.Start.Date - now.Date).Days;
        var hours = $"{w.Start:HH:mm}–{w.End:HH:mm}";
        return days switch
        {
            0 => $"dziś {hours}",
            1 => $"jutro ({w.Start.ToString("dddd", pl)}) {hours}",
            _ => $"{Weekday(w.Start, pl)} {w.Start:d.MM}, {hours}",
        };
    }

    /// <summary>„w poniedziałek”, „we wtorek”, „w środę”… (biernik z przyimkiem).</summary>
    private static string Weekday(DateTime d, System.Globalization.CultureInfo pl) => d.DayOfWeek switch
    {
        DayOfWeek.Tuesday => "we wtorek",
        DayOfWeek.Wednesday => "w środę",
        DayOfWeek.Friday => "w piątek",
        DayOfWeek.Saturday => "w sobotę",
        DayOfWeek.Sunday => "w niedzielę",
        _ => "w " + d.ToString("dddd", pl),
    };
}
