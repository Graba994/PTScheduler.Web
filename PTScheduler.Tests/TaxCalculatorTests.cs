using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Finance;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Podsumowanie podatkowe — logika była zduplikowana w Finance i HiddenFinance
/// bez testów. Teraz jedno źródło (TaxCalculator) i pokrycie kwot.
/// </summary>
public class TaxCalculatorTests
{
    private static FinanceTaxConfigDto Cfg(Action<FinanceTaxConfigDto>? tune = null)
    {
        var c = new FinanceTaxConfigDto { Module = "standard", IncomeTaxType = "none" };
        tune?.Invoke(c);
        return c;
    }

    [Fact]
    public void NoOptions_ProfitEqualsGross()
    {
        var t = TaxCalculator.Compute(Cfg(), gross: 10_000m, months: 6);
        t.Net.Should().Be(10_000m);
        t.Vat.Should().Be(0m);
        t.IncomeTax.Should().Be(0m);
        t.Profit.Should().Be(10_000m);
    }

    [Fact]
    public void Vat23_SplitsGrossAndSumHolds()
    {
        var t = TaxCalculator.Compute(Cfg(c => { c.VatEnabled = true; c.VatRate = 23m; }), 1_230m, 1);
        t.Net.Should().BeApproximately(1_000m, 0.01m);
        t.Vat.Should().BeApproximately(230m, 0.01m);
        (t.Net + t.Vat).Should().Be(1_230m);
    }

    [Fact]
    public void FlatTax_OnNet()
    {
        var t = TaxCalculator.Compute(Cfg(c => { c.IncomeTaxType = "flat"; c.FlatTaxRate = 19m; }), 10_000m, 1);
        t.IncomeTax.Should().Be(1_900m);
        t.TaxLabel.Should().Contain("liniowy");
        t.Profit.Should().Be(8_100m);
    }

    [Fact]
    public void LumpTax_OnNet()
    {
        var t = TaxCalculator.Compute(Cfg(c => { c.IncomeTaxType = "lump"; c.LumpSumRate = 8.5m; }), 10_000m, 1);
        t.IncomeTax.Should().Be(850m);
        t.TaxLabel.Should().Contain("Ryczałt");
    }

    [Fact]
    public void ScaleTax_BelowThreshold_UsesLowRate()
    {
        var t = TaxCalculator.Compute(Cfg(c =>
        {
            c.IncomeTaxType = "scale";
            c.ScaleTaxThreshold = 120_000m;
            c.ScaleTaxRateLow = 12m;
            c.ScaleTaxRateHigh = 32m;
        }), gross: 100_000m, months: 1);

        t.IncomeTax.Should().Be(12_000m); // 100000 * 12%
    }

    [Fact]
    public void ScaleTax_AboveThreshold_UsesTwoTiers()
    {
        var t = TaxCalculator.Compute(Cfg(c =>
        {
            c.IncomeTaxType = "scale";
            c.ScaleTaxThreshold = 120_000m;
            c.ScaleTaxRateLow = 12m;
            c.ScaleTaxRateHigh = 32m;
        }), gross: 200_000m, months: 1);

        // 120000*12% + 80000*32% = 14400 + 25600
        t.IncomeTax.Should().Be(40_000m);
    }

    [Fact]
    public void ZusHealthCosts_ScaledByMonths_AndSubtractedFromProfit()
    {
        var t = TaxCalculator.Compute(Cfg(c =>
        {
            c.ZusEnabled = true; c.ZusMonthlyAmount = 1_600m;
            c.HealthInsuranceEnabled = true; c.HealthInsuranceMonthly = 380m;
            c.CostDeductionsEnabled = true; c.MonthlyFixedCosts = 250m;
        }), gross: 60_000m, months: 6);

        t.Zus.Should().Be(9_600m);      // 1600 * 6
        t.Health.Should().Be(2_280m);   // 380 * 6
        t.Costs.Should().Be(1_500m);    // 250 * 6
        t.Profit.Should().Be(60_000m - 9_600m - 2_280m - 1_500m);
    }

    [Fact]
    public void FullScenario_AllStacked()
    {
        var t = TaxCalculator.Compute(Cfg(c =>
        {
            c.VatEnabled = true; c.VatRate = 23m;
            c.IncomeTaxType = "flat"; c.FlatTaxRate = 19m;
            c.ZusEnabled = true; c.ZusMonthlyAmount = 1_600m;
        }), gross: 12_300m, months: 3);

        t.Net.Should().BeApproximately(10_000m, 0.01m);
        t.IncomeTax.Should().BeApproximately(1_900m, 0.01m); // 19% z netto
        t.Zus.Should().Be(4_800m);                            // 1600 * 3
        t.Profit.Should().BeApproximately(10_000m - 1_900m - 4_800m, 0.01m);
    }

    [Theory]
    [InlineData(false, "none", false, false, false, false)]
    [InlineData(true, "none", false, false, false, true)]
    [InlineData(false, "flat", false, false, false, true)]
    [InlineData(false, "none", true, false, false, true)]
    public void HasAnyOption(bool vat, string incomeType, bool zus, bool health, bool costs, bool expected)
    {
        var c = Cfg(x =>
        {
            x.VatEnabled = vat;
            x.IncomeTaxType = incomeType;
            x.ZusEnabled = zus;
            x.HealthInsuranceEnabled = health;
            x.CostDeductionsEnabled = costs;
        });
        TaxCalculator.HasAnyOption(c).Should().Be(expected);
    }
}
