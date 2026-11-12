using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// To, co trener ustawił w kreatorze „Zbuduj swoją aplikację”. Kształt zgodny z SetupBootstrapDto
/// w aplikacji trenera (POST /internal/setup/bootstrap). Hasło trafia tu wyłącznie jako skrót
/// ASP.NET Identity — Portal nigdzie nie zapisuje samego hasła.
/// </summary>
public sealed class TenantSetupPayload
{
    public string CompanyName { get; set; } = "";
    public string OwnerEmail { get; set; } = "";
    public string? OwnerFirstName { get; set; }
    public string? OwnerLastName { get; set; }
    public string? OwnerPhone { get; set; }
    public string? PasswordHash { get; set; }
    public string? SetupMode { get; set; }
    public string? AppTheme { get; set; }
    public string? SiteTemplate { get; set; }
    public List<OfferItem> Offers { get; set; } = [];
    public PackageItem? Package { get; set; }
    public string? WelcomeTokenHash { get; set; }
    public DateTime? WelcomeTokenExpiresUtc { get; set; }

    public sealed class OfferItem
    {
        public string Name { get; set; } = "";
        public int DurationMinutes { get; set; } = 60;
        public decimal? Price { get; set; }
        public bool IsPair { get; set; }
        public bool IsGroup { get; set; }
        public int? MaxParticipants { get; set; }
    }

    public sealed class PackageItem
    {
        public string Name { get; set; } = "";
        public int SessionsCount { get; set; }
        public decimal Price { get; set; }
        public int OfferIndex { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static TenantSetupPayload? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<TenantSetupPayload>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Instancja założona bez kreatora (ręcznie w Portalu) — dane tylko wypełnią /setup.</summary>
    public static TenantSetupPayload PrefillFrom(Tenant t)
    {
        var (first, last) = SplitName(t.OwnerName);
        return new TenantSetupPayload
        {
            CompanyName = t.CompanyName,
            OwnerEmail = t.OwnerEmail,
            OwnerFirstName = first,
            OwnerLastName = last,
            OwnerPhone = t.Phone,
            SetupMode = t.SetupMode
        };
    }

    public static (string? First, string? Last) SplitName(string? fullName)
    {
        var parts = (fullName ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch { 0 => (null, null), 1 => (parts[0], null), _ => (parts[0], parts[1]) };
    }

    /// <summary>Skrót hasła w formacie ASP.NET Identity (zgodny z kontem w aplikacji trenera).</summary>
    public static string HashPassword(string password) => new PasswordHasher<object>().HashPassword(new object(), password);

    /// <summary>Jednorazowy token wejścia do aplikacji zaraz po publikacji: (token do linku, skrót dla instancji).</summary>
    public static (string Token, string Hash) NewWelcomeToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        return (token, hash);
    }
}
