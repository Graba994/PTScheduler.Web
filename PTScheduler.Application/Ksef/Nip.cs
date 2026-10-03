namespace PTScheduler.Application.Ksef;

/// <summary>Walidacja polskiego NIP (10 cyfr, suma kontrolna).</summary>
public static class Nip
{
    private static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    /// <summary>Usuwa kreski, spacje i prefiks „PL”.</summary>
    public static string Normalize(string? nip)
    {
        if (string.IsNullOrWhiteSpace(nip)) return string.Empty;
        var s = nip.Trim().ToUpperInvariant();
        if (s.StartsWith("PL")) s = s[2..];
        return new string(s.Where(char.IsDigit).ToArray());
    }

    public static bool IsValid(string? nip)
    {
        var n = Normalize(nip);
        if (n.Length != 10) return false;
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (n[i] - '0') * Weights[i];
        var control = sum % 11;
        return control != 10 && control == n[9] - '0';
    }
}
