using System.Security.Cryptography;

namespace PTScheduler.Domain.Security;

/// <summary>
/// Hashowanie i weryfikacja PIN-u finansów.
///
/// <para>
/// Wcześniej PIN był hashowany nieosolonym SHA-256 — dla 4–8 cyfr trywialny do
/// złamania tęczową tablicą. Teraz PBKDF2 (HMAC-SHA256) z losową solą i wieloma
/// iteracjami. Format zapisu jest samoopisujący:
/// <c>pbkdf2$&lt;iteracje&gt;$&lt;sól_b64&gt;$&lt;hash_b64&gt;</c>.
/// </para>
///
/// <para>
/// <see cref="Verify"/> akceptuje też stary format (64-znakowy hex nieosolonego
/// SHA-256), żeby istniejące PIN-y nadal działały; przy najbliższym ustawieniu
/// PIN-u zapis przechodzi na PBKDF2. Porównania są w stałym czasie.
/// </para>
/// </summary>
public static class PinHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;   // bajty
    private const int HashSize = 32;   // bajty (SHA-256)
    private const string Prefix = "pbkdf2";

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        if (stored.StartsWith(Prefix + "$", StringComparison.Ordinal))
            return VerifyPbkdf2(pin, stored);

        // Stary format: nieosolony SHA-256 jako hex. Wspieramy do czasu, aż PIN
        // zostanie ponownie ustawiony (wtedy zapis przechodzi na PBKDF2).
        return VerifyLegacySha256(pin, stored);
    }

    /// <summary>
    /// Czy zapisany hash jest w starym, słabym formacie — pozwala warstwie wyżej
    /// zaproponować ponowne ustawienie PIN-u.
    /// </summary>
    public static bool IsLegacy(string? stored) =>
        !string.IsNullOrEmpty(stored) && !stored.StartsWith(Prefix + "$", StringComparison.Ordinal);

    private static bool VerifyPbkdf2(string pin, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }

        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool VerifyLegacySha256(string pin, string stored)
    {
        var actual = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pin));
        var actualHex = Convert.ToHexStringLower(actual);
        var a = System.Text.Encoding.UTF8.GetBytes(actualHex);
        var b = System.Text.Encoding.UTF8.GetBytes(stored);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
