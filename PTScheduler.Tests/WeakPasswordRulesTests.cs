using FluentAssertions;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Hasła łatwe do zgadnięcia są odrzucane z jasnym powodem; dobre przechodzą.</summary>
public class WeakPasswordRulesTests
{
    [Theory]
    [InlineData("Haslo123")]
    [InlineData("haslo123!")]
    [InlineData("Qwerty2024")]
    [InlineData("Trening1")]
    [InlineData("P4ssw0rd1")]
    [InlineData("12345678")]
    [InlineData("abcdefgh")]
    [InlineData("aaaaaaa1")]
    [InlineData("zaq12wsx")]
    public void Common_Passwords_Are_Rejected(string password) =>
        WeakPasswordRules.LocalReason(password, "ola@example.com", "Ola", "Nowak").Should().NotBeNull();

    [Theory]
    [InlineData("Nowak2026!", "nowak")]
    [InlineData("jankowalski1", "jankowalski")]
    public void Passwords_Based_On_Name_Or_Email_Are_Rejected(string password, string localPart) =>
        WeakPasswordRules.LocalReason(password, $"{localPart}@example.com", "Ola", "Nowak").Should().Contain("imieniu");

    [Theory]
    [InlineData("Rower-Kawa-Ogród7")]
    [InlineData("kot9Lampa!sufit")]
    [InlineData("Bx7#qPm2vL")]
    public void Strong_Passwords_Pass(string password) =>
        WeakPasswordRules.LocalReason(password, "ola@example.com", "Ola", "Nowak").Should().BeNull();
}
