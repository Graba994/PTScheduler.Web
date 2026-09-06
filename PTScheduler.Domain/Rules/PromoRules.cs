namespace PTScheduler.Domain.Rules;

/// <summary>
/// Jedyne miejsce, w którym rozstrzyga się, czy promocja na sesję wstępną
/// jest aktywna.
///
/// <para>
/// Powstało dlatego, że ta sama reguła była wcześniej liczona w trzech
/// miejscach i na trzy różne sposoby — względem <c>DateTime.Now</c>,
/// <c>DateTime.UtcNow</c> i <c>DateTime.Today</c>. W kontenerze działającym
/// w UTC dawały różne wyniki, więc panel trenera i strona publiczna mogły
/// pokazywać sprzeczne ceny.
/// </para>
/// </summary>
public static class PromoRules
{
    /// <summary>
    /// Czy promocja obowiązuje w dniu <paramref name="today"/>.
    ///
    /// <para>
    /// <c>validUntil</c> pochodzi z wyboru daty i oznacza <b>ostatni dzień
    /// obowiązywania włącznie</b> — „ważna do 15.03” obowiązuje przez cały
    /// 15 marca. Wcześniejszy warunek <c>validUntil &gt; today</c> wygaszał
    /// promocję dzień za wcześnie.
    /// </para>
    /// </summary>
    /// <param name="promoPrice">Cena promocyjna; brak = brak promocji.</param>
    /// <param name="validUntil">Ostatni dzień obowiązywania włącznie.</param>
    /// <param name="today">Dzisiejsza data na zegarze ściennym aplikacji.</param>
    public static bool IsActive(decimal? promoPrice, DateTime? validUntil, DateOnly today)
        => promoPrice.HasValue
           && validUntil.HasValue
           && today <= DateOnly.FromDateTime(validUntil.Value);

    /// <summary>
    /// Cena do zapłaty: promocyjna, jeśli promocja obowiązuje, w przeciwnym
    /// razie podstawowa.
    /// </summary>
    public static decimal EffectivePrice(
        decimal basePrice, decimal? promoPrice, DateTime? validUntil, DateOnly today)
        => IsActive(promoPrice, validUntil, today) ? promoPrice!.Value : basePrice;
}
