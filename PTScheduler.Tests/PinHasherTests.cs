using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PTScheduler.Domain.Security;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Hashowanie PIN-u finansów: PBKDF2 z solą, z kompatybilnością wsteczną dla
/// starego nieosolonego SHA-256.
/// </summary>
public class PinHasherTests
{
    private static string LegacySha256Hex(string pin) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));

    [Fact]
    public void Hash_Then_Verify_Roundtrips()
    {
        var stored = PinHasher.Hash("1234");
        PinHasher.Verify("1234", stored).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPin_Fails()
    {
        var stored = PinHasher.Hash("1234");
        PinHasher.Verify("9999", stored).Should().BeFalse();
    }

    [Fact]
    public void Hash_Uses_Random_Salt_So_Two_Hashes_Differ()
    {
        PinHasher.Hash("1234").Should().NotBe(PinHasher.Hash("1234"));
    }

    [Fact]
    public void Hash_Has_SelfDescribing_Pbkdf2_Format()
    {
        PinHasher.Hash("1234").Should().StartWith("pbkdf2$");
    }

    [Fact]
    public void Verify_Accepts_Legacy_Sha256_Hash()
    {
        // Stary format zapisany w bazie musi nadal działać.
        var legacy = LegacySha256Hex("1234");
        PinHasher.Verify("1234", legacy).Should().BeTrue();
        PinHasher.Verify("0000", legacy).Should().BeFalse();
    }

    [Fact]
    public void IsLegacy_Detects_Old_Format_Only()
    {
        PinHasher.IsLegacy(LegacySha256Hex("1234")).Should().BeTrue();
        PinHasher.IsLegacy(PinHasher.Hash("1234")).Should().BeFalse();
    }

    [Fact]
    public void Verify_Handles_Null_Or_Empty()
    {
        PinHasher.Verify("1234", null).Should().BeFalse();
        PinHasher.Verify("1234", "").Should().BeFalse();
    }

    [Fact]
    public void Verify_Malformed_Pbkdf2_Fails_Gracefully()
    {
        PinHasher.Verify("1234", "pbkdf2$notanumber$xx$yy").Should().BeFalse();
        PinHasher.Verify("1234", "pbkdf2$100000$!!!$!!!").Should().BeFalse();
    }
}
