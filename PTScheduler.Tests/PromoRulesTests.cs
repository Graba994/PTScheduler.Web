using FluentAssertions;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Reguła ważności promocji. Powstała, bo ten sam warunek był wcześniej
/// liczony w trzech miejscach względem trzech różnych punktów odniesienia
/// (<c>DateTime.Now</c>, <c>DateTime.UtcNow</c>, <c>DateTime.Today</c>),
/// przez co panel trenera i strona publiczna mogły pokazywać różne ceny.
/// </summary>
public class PromoRulesTests
{
    private static readonly DateTime ValidUntil = new(2026, 3, 15);

    [Fact]
    public void Active_Before_Deadline()
    {
        PromoRules.IsActive(50m, ValidUntil, new DateOnly(2026, 3, 10))
            .Should().BeTrue();
    }

    [Fact]
    public void Active_On_The_Deadline_Day_Itself()
    {
        // Kluczowy przypadek. Poprzedni warunek `validUntil > today` zwracał
        // tu false, więc promocja "ważna do 15.03" wygasała 14.03 wieczorem.
        PromoRules.IsActive(50m, ValidUntil, new DateOnly(2026, 3, 15))
            .Should().BeTrue("promocja ważna do 15.03 obowiązuje przez cały 15 marca");
    }

    [Fact]
    public void Expired_Day_After_Deadline()
    {
        PromoRules.IsActive(50m, ValidUntil, new DateOnly(2026, 3, 16))
            .Should().BeFalse();
    }

    [Fact]
    public void Inactive_Without_Promo_Price()
    {
        PromoRules.IsActive(null, ValidUntil, new DateOnly(2026, 3, 10))
            .Should().BeFalse();
    }

    [Fact]
    public void Inactive_Without_Deadline()
    {
        PromoRules.IsActive(50m, null, new DateOnly(2026, 3, 10))
            .Should().BeFalse();
    }

    [Fact]
    public void Deadline_Time_Component_Is_Ignored()
    {
        // InputDate daje północ, ale gdyby kiedyś przyszła godzina inna niż
        // 00:00, reguła i tak ma patrzeć wyłącznie na datę.
        var withTime = new DateTime(2026, 3, 15, 23, 59, 0);

        PromoRules.IsActive(50m, withTime, new DateOnly(2026, 3, 15))
            .Should().BeTrue();
    }

    [Fact]
    public void EffectivePrice_Uses_Promo_While_Active()
    {
        PromoRules.EffectivePrice(200m, 150m, ValidUntil, new DateOnly(2026, 3, 15))
            .Should().Be(150m);
    }

    [Fact]
    public void EffectivePrice_Falls_Back_To_Base_When_Expired()
    {
        PromoRules.EffectivePrice(200m, 150m, ValidUntil, new DateOnly(2026, 3, 16))
            .Should().Be(200m);
    }

    [Fact]
    public void EffectivePrice_Falls_Back_To_Base_Without_Promo()
    {
        PromoRules.EffectivePrice(200m, null, ValidUntil, new DateOnly(2026, 3, 10))
            .Should().Be(200m);
    }
}
