using FluentAssertions;
using PTScheduler.Application.Security;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Wejście z Portalu do aplikacji trenera: Portal podpisuje token, aplikacja go sprawdza (ten sam format).</summary>
public class PortalSsoTests
{
    private const string Secret = "sekret-instancji-annafit-1234567890";
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Token_from_portal_is_accepted_by_trainer_app()
    {
        var token = TenantSso.CreateToken("annafit", "admin@hantly.pl", "owner", Secret, Now);
        var sso = PortalSsoToken.Validate(token, Secret, "annafit", Now.AddSeconds(5));

        sso.Should().NotBeNull();
        sso!.AdminEmail.Should().Be("admin@hantly.pl");
        sso.Target.Should().Be("owner");
        sso.Nonce.Length.Should().BeGreaterThanOrEqualTo(16);
        sso.ExpiresAt.Should().Be(Now.AddSeconds(TenantSso.LifetimeSeconds));
    }

    [Fact]
    public void Token_is_bound_to_one_instance_and_secret()
    {
        var token = TenantSso.CreateToken("annafit", "admin@hantly.pl", "root", Secret, Now);
        PortalSsoToken.Validate(token, Secret, "inny-trener", Now).Should().BeNull("token jednej instancji nie otwiera innej");
        PortalSsoToken.Validate(token, "inny-sekret", "annafit", Now).Should().BeNull();
    }

    [Fact]
    public void Token_expires_after_two_minutes()
    {
        var token = TenantSso.CreateToken("annafit", "admin@hantly.pl", "root", Secret, Now);
        PortalSsoToken.Validate(token, Secret, "annafit", Now.AddSeconds(119)).Should().NotBeNull();
        PortalSsoToken.Validate(token, Secret, "annafit", Now.AddSeconds(121)).Should().BeNull();
    }

    [Fact]
    public void Tampered_or_garbage_tokens_are_rejected()
    {
        var token = TenantSso.CreateToken("annafit", "admin@hantly.pl", "root", Secret, Now);
        var parts = token.Split('.');
        // Podmiana treści (np. inny cel albo dłuższa ważność) psuje podpis.
        var forged = TenantSso.CreateToken("annafit", "admin@hantly.pl", "owner", "zly-sekret", Now).Split('.')[0] + "." + parts[1];
        PortalSsoToken.Validate(forged, Secret, "annafit", Now).Should().BeNull();
        PortalSsoToken.Validate("", Secret, "annafit", Now).Should().BeNull();
        PortalSsoToken.Validate("abc.def", Secret, "annafit", Now).Should().BeNull();
        PortalSsoToken.Validate("!!!.???", Secret, "annafit", Now).Should().BeNull();
        PortalSsoToken.Validate(token, "", "annafit", Now).Should().BeNull("bez sekretu instancji nie wpuszczamy nikogo");
    }

    [Fact]
    public void Unknown_target_falls_back_to_technical_account()
    {
        var token = TenantSso.CreateToken("annafit", "admin@hantly.pl", "klient", Secret, Now);
        PortalSsoToken.Validate(token, Secret, "annafit", Now)!.Target.Should().Be("root");
    }
}
