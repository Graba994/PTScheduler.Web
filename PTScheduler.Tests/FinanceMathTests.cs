using FluentAssertions;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Rozbicie brutto na netto + VAT dla faktur. Kwoty prawne — muszą się zgadzać
/// co do grosza i poprawnie znikać, gdy VAT wyłączony.
/// </summary>
public class FinanceMathTests
{
    [Fact]
    public void SplitsGrossIntoNetAndVat_At23Percent()
    {
        var (net, vat) = FinanceMath.SplitVatInclusive(123m, vatEnabled: true, vatRatePercent: 23m);

        net.Should().BeApproximately(100m, 0.005m);
        vat.Should().BeApproximately(23m, 0.005m);
    }

    [Fact]
    public void Net_Plus_Vat_Always_Equals_Gross()
    {
        // Niezmiennik: vat liczony jako reszta, więc suma zawsze == brutto,
        // niezależnie od zaokrągleń.
        var (net, vat) = FinanceMath.SplitVatInclusive(99.99m, vatEnabled: true, vatRatePercent: 23m);
        (net + vat).Should().Be(99.99m);
    }

    [Fact]
    public void VatDisabled_WholeAmountIsNet_NoVat()
    {
        var (net, vat) = FinanceMath.SplitVatInclusive(200m, vatEnabled: false, vatRatePercent: 23m);
        net.Should().Be(200m);
        vat.Should().Be(0m);
    }

    [Fact]
    public void ZeroRate_TreatedAsNoVat()
    {
        var (net, vat) = FinanceMath.SplitVatInclusive(200m, vatEnabled: true, vatRatePercent: 0m);
        net.Should().Be(200m);
        vat.Should().Be(0m);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(23)]
    public void WorksAcrossRates_AndPreservesSum(int rate)
    {
        var (net, vat) = FinanceMath.SplitVatInclusive(500m, vatEnabled: true, vatRatePercent: rate);
        (net + vat).Should().Be(500m);
        net.Should().BeLessThan(500m);
        vat.Should().BeGreaterThan(0m);
    }
}
