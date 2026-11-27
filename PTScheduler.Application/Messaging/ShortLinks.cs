using System.Security.Cryptography;

namespace PTScheduler.Application.Messaging;

/// <summary>Krótkie linki do SMS-ów i wiadomości (/l/{kod}): kod bez mylących znaków i bezpieczny cel.</summary>
public static class ShortLinks
{
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewCode(int length = 7)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    /// <summary>Krótki link prowadzi tylko do ścieżki w tej aplikacji (nigdy na zewnątrz).</summary>
    public static bool IsSafeTarget(string? path) =>
        !string.IsNullOrEmpty(path) && path.StartsWith('/') && !path.StartsWith("//") && !path.StartsWith("/\\") && !path.Contains("://");
}
