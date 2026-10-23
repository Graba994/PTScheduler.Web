namespace PTScheduler.Application.Scheduling;

/// <summary>„Trening za 25 min” / „Trwa · jeszcze 20 min” — do najbliższej wizyty na Dziś i na Pulpicie.</summary>
public static class Countdown
{
    /// <summary>Tekst odliczania albo null, gdy wizyta już się skończyła.</summary>
    public static string? Label(DateTime start, DateTime end, DateTime now)
    {
        if (now >= end) return null;
        if (now >= start) return $"Trwa · jeszcze {Span((int)Math.Ceiling((end - now).TotalMinutes))}";
        var minutes = (int)Math.Ceiling((start - now).TotalMinutes);
        return minutes <= 1 ? "Trening za chwilę" : $"Trening za {Span(minutes)}";
    }

    /// <summary>Czy zostało mało czasu (do 15 min) albo wizyta trwa — wtedy chip jest wyraźniejszy.</summary>
    public static bool IsSoon(DateTime start, DateTime end, DateTime now) => now < end && start - now <= TimeSpan.FromMinutes(15);

    private static string Span(int minutes) =>
        minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} godz." : $"{minutes / 60} godz. {minutes % 60} min";
}
