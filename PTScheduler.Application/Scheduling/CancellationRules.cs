using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Scheduling;

/// <summary>Wynik oceny odwołania wizyty przez klienta.</summary>
/// <param name="Allowed">Czy klient może odwołać wizytę sam.</param>
/// <param name="IsLate">Odwołanie po terminie bezpłatnego odwołania.</param>
/// <param name="RefundsSession">Sesja wraca do pakietu.</param>
/// <param name="Message">Wyjaśnienie dla klienta (powód blokady albo konsekwencja).</param>
public sealed record CancellationDecision(bool Allowed, bool IsLate, bool RefundsSession, string Message);

/// <summary>
/// Polityka odwołań trenera — jedno źródło prawdy dla UI i serwera.
/// Czas: <paramref name="now"/> to zegar ścienny studia (IAppClock.LocalNow),
/// tak jak Session.StartTime.
/// </summary>
public static class CancellationRules
{
    public static CancellationDecision ForClient(TrainerConfigDto cfg, DateTime sessionStart, DateTime now)
    {
        if (sessionStart <= now)
            return new(false, true, false, "Wizyta już się rozpoczęła — odwołanie nie jest możliwe.");

        var window = Math.Max(0, cfg.CancellationWindowHours);
        var isLate = window > 0 && sessionStart - now < TimeSpan.FromHours(window);
        if (!isLate)
            return new(true, false, true, "Bezpłatne odwołanie — sesja wróci do pakietu.");

        return cfg.LateCancellationPolicy switch
        {
            LateCancellationPolicy.ChargeSession => new(true, true, false,
                $"Do wizyty zostało mniej niż {window} h — po odwołaniu sesja przepada (zostanie pobrana z pakietu)."),
            LateCancellationPolicy.Refund => new(true, true, true,
                "Późne odwołanie — trener nie pobiera za nie sesji."),
            _ => new(false, true, false,
                $"Wizytę można odwołać samodzielnie najpóźniej {window} h przed rozpoczęciem. Skontaktuj się z trenerem."),
        };
    }

    /// <summary>Opis zasad pokazywany klientowi (np. przy rezerwacji).</summary>
    public static string Describe(TrainerConfigDto cfg)
    {
        var window = Math.Max(0, cfg.CancellationWindowHours);
        var late = window == 0
            ? "Wizytę możesz odwołać bezpłatnie w dowolnym momencie przed jej rozpoczęciem."
            : cfg.LateCancellationPolicy switch
            {
                LateCancellationPolicy.ChargeSession =>
                    $"Bezpłatne odwołanie do {window} h przed wizytą. Później sesja przepada.",
                LateCancellationPolicy.Refund =>
                    $"Zalecamy odwołanie do {window} h przed wizytą; późne odwołanie nie pobiera sesji.",
                _ => $"Samodzielne odwołanie do {window} h przed wizytą. Później — tylko przez kontakt z trenerem.",
            };
        var noShow = cfg.NoShowChargesSession
            ? " Nieobecność bez odwołania pobiera sesję z pakietu."
            : "";
        return late + noShow;
    }
}
