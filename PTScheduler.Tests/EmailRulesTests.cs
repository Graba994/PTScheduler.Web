using FluentAssertions;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Adresy e-mail przy zakładaniu kont: konkretny powód błędu i podpowiedź przy literówce.</summary>
public class EmailRulesTests
{
    [Theory]
    [InlineData("jan.kowalski@gmail.com")]
    [InlineData("  Jan.Kowalski@WP.pl ")]
    [InlineData("anna+trening@firma.com.pl")]
    public void Accepts_valid_addresses(string email) => EmailRules.IsValid(email).Should().BeTrue();

    [Theory]
    [InlineData("", "Podaj adres")]
    [InlineData("jan kowalski@gmail.com", "spacji")]
    [InlineData("jan.kowalski.gmail.com", "Brakuje znaku „@”")]
    [InlineData("jan@@gmail.com", "jeden znak")]
    [InlineData("@gmail.com", "przed „@”")]
    [InlineData("jan@", "Brakuje domeny")]
    [InlineData("jan@gmail", "niepełna")]
    [InlineData("jan..k@gmail.com", "kropkę")]
    [InlineData("jan@gmail.c", "Końcówka")]
    [InlineData("jań@gmail.com", "niedozwolone")]
    public void Explains_what_is_wrong(string email, string fragment) =>
        EmailRules.Problem(email).Should().Contain(fragment);

    [Theory]
    [InlineData("jan@gmial.com", "jan@gmail.com")]
    [InlineData("jan@gmail.con", "jan@gmail.com")]
    [InlineData("Jan@WP.P", null)]
    [InlineData("jan@onet.p", null)]
    [InlineData("jan@firma.con", "jan@firma.com")]
    [InlineData("jan@gmail.com", null)]
    public void Suggests_fix_for_common_typos(string email, string? expected)
    {
        // „wp.p” i „onet.p” są niepoprawne (za krótka końcówka) — najpierw komunikat o błędzie, podpowiedź tylko dla poprawnych formalnie.
        if (expected is null && !EmailRules.IsValid(email)) { EmailRules.Problem(email).Should().NotBeNull(); return; }
        EmailRules.Suggestion(email).Should().Be(expected);
    }

    [Fact]
    public void Addresses_are_compared_after_normalization() =>
        EmailRules.Normalize("  Jan.Kowalski@WP.pl ").Should().Be("jan.kowalski@wp.pl");
}
