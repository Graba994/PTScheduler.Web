namespace PTScheduler.Domain.Rules;

/// <summary>
/// Czyste obliczenia kwot finansowych. Wydzielone z InvoiceService, żeby
/// matematyka trafiająca na fakturę (dokument prawny) miała testy i jedno
/// źródło prawdy — dotąd była wklejona inline i nietestowana.
/// </summary>
public static class FinanceMath
{
    /// <summary>
    /// Rozbija kwotę BRUTTO na netto i VAT (ceny w PLN są zawsze brutto).
    /// Gdy VAT wyłączony lub stawka niedodatnia, całość jest netto, a VAT = 0.
    /// Niezmiennik: <c>net + vat == gross</c> co do grosza (vat liczony jako
    /// reszta), więc suma na fakturze zawsze się zgadza z kwotą pobraną.
    /// </summary>
    /// <param name="gross">Kwota brutto (finalnie pobrana od klienta).</param>
    /// <param name="vatEnabled">Czy VAT jest naliczany.</param>
    /// <param name="vatRatePercent">Stawka VAT w procentach, np. 23.</param>
    public static (decimal Net, decimal Vat) SplitVatInclusive(decimal gross, bool vatEnabled, decimal vatRatePercent)
    {
        if (!vatEnabled || vatRatePercent <= 0)
            return (gross, 0m);

        var net = gross / (1 + vatRatePercent / 100m);
        return (net, gross - net);
    }
}
