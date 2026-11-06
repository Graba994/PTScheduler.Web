using System.Text.Json;
using System.Text.RegularExpressions;

namespace PTScheduler.Portal.Services;

/// <summary>Sylwetka trenera na stronie głównej Portalu (sekcja „Z nami trenują”).</summary>
public sealed class FeaturedTrainer
{
    /// <summary>Stały identyfikator — pod nim trzymamy zdjęcie.</summary>
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = "";
    public string Initials { get; set; } = "";
    /// <summary>Specjalizacja, np. „Trening siłowy i sylwetka”.</summary>
    public string Role { get; set; } = "";
    public string City { get; set; } = "";
    /// <summary>Strona trenera (np. jego aplikacja) — ma pierwszeństwo przed Instagramem.</summary>
    public string Link { get; set; } = "";
    public string Instagram { get; set; } = "";
    public string Color { get; set; } = "#7C3AED";
    /// <summary>Zmienia się przy każdym nowym zdjęciu — adres obrazka omija starą kopię w przeglądarce.</summary>
    public string? PhotoVersion { get; set; }

    public bool HasPhoto => !string.IsNullOrEmpty(PhotoVersion);
    public string PhotoUrl => $"/media/trainers/{Id}.jpg?v={PhotoVersion}";

    public string? Href =>
        !string.IsNullOrWhiteSpace(Link) ? (Link.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Link : "https://" + Link)
        : !string.IsNullOrWhiteSpace(Instagram)
            ? (Instagram.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Instagram : $"https://instagram.com/{Instagram.TrimStart('@')}")
            : null;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}

/// <summary>Odczyt i zapis sylwetek trenerów oraz ich zdjęć (w ustawieniach Portalu, więc przeżywają podmianę kontenera).</summary>
public static partial class FeaturedTrainers
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<FeaturedTrainer>> LoadAsync(SiteSettingsService settings)
    {
        var json = await settings.GetAsync(SiteSettingsService.Keys.FeaturedTrainers);
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<FeaturedTrainer>>(json, Json) ?? [];
            foreach (var t in list.Where(t => !IsValidId(t.Id))) t.Id = FeaturedTrainer.NewId();
            return list;
        }
        catch { return []; }
    }

    public static Task SaveAsync(SiteSettingsService settings, IEnumerable<FeaturedTrainer> trainers) =>
        settings.SetAsync(SiteSettingsService.Keys.FeaturedTrainers,
            JsonSerializer.Serialize(trainers.Where(t => !string.IsNullOrWhiteSpace(t.Name)).ToList()));

    public static string PhotoKey(string id) => $"featured_trainer_photo_{id}";

    public static async Task<byte[]?> GetPhotoAsync(SiteSettingsService settings, string id)
    {
        if (!IsValidId(id)) return null;
        var b64 = await settings.GetAsync(PhotoKey(id));
        if (string.IsNullOrEmpty(b64)) return null;
        try { return Convert.FromBase64String(b64); } catch { return null; }
    }

    public static Task SetPhotoAsync(SiteSettingsService settings, string id, byte[]? jpeg) =>
        IsValidId(id) ? settings.SetAsync(PhotoKey(id), jpeg is null ? "" : Convert.ToBase64String(jpeg)) : Task.CompletedTask;

    public static bool IsValidId(string? id) => id is not null && IdRx().IsMatch(id);

    [GeneratedRegex("^[a-f0-9]{8,32}$")]
    private static partial Regex IdRx();
}
