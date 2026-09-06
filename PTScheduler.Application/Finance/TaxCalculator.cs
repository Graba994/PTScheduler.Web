using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Rules;

namespace PTScheduler.Application.Finance;

/// <summary>Rozbicie przychodu na podatki, składki, koszty i zysk netto.</summary>
public sealed record TaxBreakdown(
    decimal Gross,
    decimal Vat,
    decimal Net,
    decimal IncomeTax,
    string TaxLabel,
    decimal Zus,
    decimal Health,
    decimal Costs,
    decimal Profit);

/// <summary>
/// Obliczenia podsumowania podatkowego. Wydzielone ze stron Finance i
/// HiddenFinance, które miały DWIE identyczne, niezależne kopie tej logiki
/// (ryzyko rozjazdu) i żadnych testów — mimo że to kwoty podatkowe.
///
/// <para>
/// VAT liczymy przez wspólne <see cref="FinanceMath.SplitVatInclusive"/>
/// (ceny są brutto), więc rozbicie brutto→netto jest identyczne jak na fakturze.
/// </para>
/// </summary>
public static class TaxCalculator
{
    /// <summary>Czy jakakolwiek opcja podatkowa jest włączona (czy pokazywać podsumowanie).</summary>
    public static bool HasAnyOption(FinanceTaxConfigDto tax) =>
        tax.VatEnabled || tax.IncomeTaxType != "none" || tax.ZusEnabled
        || tax.HealthInsuranceEnabled || tax.CostDeductionsEnabled;

    /// <param name="gross">Przychód brutto (strona sama decyduje co wchodzi w skład).</param>
    /// <param name="months">Liczba miesięcy, za które naliczane są składki/koszty.</param>
    public static TaxBreakdown Compute(FinanceTaxConfigDto tax, decimal gross, int months)
    {
        var (net, vat) = FinanceMath.SplitVatInclusive(gross, tax.VatEnabled, tax.VatRate);

        decimal incomeTax = 0;
        var taxLabel = "";
        switch (tax.IncomeTaxType)
        {
            case "flat":
                incomeTax = net * tax.FlatTaxRate / 100;
                taxLabel = $"PIT liniowy ({tax.FlatTaxRate}%)";
                break;
            case "lump":
                incomeTax = net * tax.LumpSumRate / 100;
                taxLabel = $"Ryczałt ({tax.LumpSumRate}%)";
                break;
            case "scale":
                if (net <= tax.ScaleTaxThreshold)
                {
                    incomeTax = net * tax.ScaleTaxRateLow / 100;
                    taxLabel = $"Skala ({tax.ScaleTaxRateLow}%)";
                }
                else
                {
                    incomeTax = tax.ScaleTaxThreshold * tax.ScaleTaxRateLow / 100
                                + (net - tax.ScaleTaxThreshold) * tax.ScaleTaxRateHigh / 100;
                    taxLabel = $"Skala ({tax.ScaleTaxRateLow}%/{tax.ScaleTaxRateHigh}%)";
                }
                break;
        }

        var zus = tax.ZusEnabled ? tax.ZusMonthlyAmount * months : 0;
        var health = tax.HealthInsuranceEnabled ? tax.HealthInsuranceMonthly * months : 0;
        var costs = tax.CostDeductionsEnabled ? tax.MonthlyFixedCosts * months : 0;
        var profit = net - incomeTax - zus - health - costs;

        return new TaxBreakdown(gross, vat, net, incomeTax, taxLabel, zus, health, costs, profit);
    }
}
