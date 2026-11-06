namespace PTScheduler.Domain.Rules;

/// <summary>
/// Waluta w interfejsie i powiadomieniach: „zł” zamiast kodu „PLN” (czytelniej po polsku).
/// Kod ISO zostaje w dokumentach księgowych, eksportach i rozmowach z bramkami płatności.
/// </summary>
public static class CurrencyLabel
{
    public static string Of(string? code) =>
        string.IsNullOrWhiteSpace(code) || code.Trim().Equals("PLN", StringComparison.OrdinalIgnoreCase) ? "zł" : code.Trim().ToUpperInvariant();
}
