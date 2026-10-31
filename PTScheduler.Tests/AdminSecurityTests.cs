using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Weryfikacja dwuetapowa administratorów Portalu: kod QR dla aplikacji uwierzytelniającej i wpisywanie kodów.</summary>
public class AdminSecurityTests
{
    [Fact]
    public void Authenticator_uri_names_hantly_and_the_account()
    {
        AdminSecurity.AuthenticatorUri("anna@hantly.pl", "JBSWY3DPEHPK3PXP")
            .Should().Be("otpauth://totp/Hantly:anna%40hantly.pl?secret=JBSWY3DPEHPK3PXP&issuer=Hantly&digits=6");
    }

    [Fact]
    public void Key_is_grouped_for_manual_entry()
    {
        AdminSecurity.FormatKey("JBSWY3DPEHPK3PXP").Should().Be("jbsw y3dp ehpk 3pxp");
    }

    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData(" 12-34-56 ", "123456")]
    [InlineData("ab12c-d34ef", "ab12cd34ef")]
    [InlineData(null, "")]
    public void Codes_ignore_spaces_and_dashes(string? input, string expected) =>
        AdminSecurity.NormalizeCode(input).Should().Be(expected);
}
