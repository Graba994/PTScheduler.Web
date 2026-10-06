using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services.Wger;

/// <summary>
/// Pobiera katalog ćwiczeń z publicznego API wger.de (/api/v2/exerciseinfo/) — bez klucza,
/// kilka zapytań po 100 pozycji. Rekordy zapisujemy jako ćwiczenia bazowe (Source = Wger,
/// SourceKey = „wger:{uuid}”); ponowny import aktualizuje je w miejscu. Zdjęcia i filmy
/// zostają na serwerach wger (bez kopiowania), a autorzy i licencja trafiają do Attribution,
/// bo treści wger są na CC-BY-SA.
/// </summary>
public partial class WgerCatalogImporter(
    IHttpClientFactory httpFactory,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<WgerCatalogImporter> logger) : IWgerCatalogImporter
{
    public const string KeyPrefix = "wger:";
    private const int LanguageEnglish = 2;
    private const int LanguagePolish = 14;
    private const int CategoryCardio = 15;
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string BaseUrl =>
        (Environment.GetEnvironmentVariable("WGER_API_BASE_URL") ?? "https://wger.de/api/v2/").TrimEnd('/') + "/";

    public async Task<WgerImportResult> ImportAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        using var http = httpFactory.CreateClient("wger");
        http.Timeout = TimeSpan.FromSeconds(60);
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PTScheduler/1.0 (+https://ptscheduler.pl)");

        var records = new List<WgerExercise>();
        string? next = $"{BaseUrl}exerciseinfo/?limit={PageSize}&offset=0";
        var total = 0;
        while (next is not null)
        {
            ct.ThrowIfCancellationRequested();
            var page = await http.GetFromJsonAsync<WgerPage>(next, Json, ct)
                ?? throw new InvalidOperationException("wger.de zwróciło pustą odpowiedź.");
            records.AddRange(page.Results);
            total = Math.Max(page.Count, records.Count);
            progress?.Report((records.Count / 2, total)); // pobieranie = pierwsza połowa paska
            next = page.Next;
            if (records.Count > 20_000) break; // bezpiecznik na wypadek pętli stronicowania
        }

        await using var db = dbFactory.CreateDbContext();
        var existing = await db.Exercises
            .Where(e => e.SourceKey != null && e.SourceKey.StartsWith(KeyPrefix))
            .ToDictionaryAsync(e => e.SourceKey!, ct);

        int added = 0, updated = 0, skipped = 0, done = 0;
        foreach (var r in records)
        {
            done++;
            var mapped = Map(r);
            if (mapped is null) { skipped++; continue; }

            if (existing.TryGetValue(mapped.SourceKey!, out var e))
            {
                Copy(mapped, e);
                updated++;
            }
            else
            {
                db.Exercises.Add(mapped);
                existing[mapped.SourceKey!] = mapped;
                added++;
            }
            if (done % 100 == 0) progress?.Report(((records.Count + done) / 2, records.Count));
        }

        await db.SaveChangesAsync(ct);
        progress?.Report((records.Count, records.Count));
        logger.LogInformation("wger: {Added} nowych, {Updated} zaktualizowanych, {Skipped} pominiętych (bez nazwy PL/EN).", added, updated, skipped);
        return new WgerImportResult(added, updated, skipped, records.Count);
    }

    /// <summary>Rekord wger → ćwiczenie bazowe. Null, gdy nie ma nazwy po polsku ani po angielsku.</summary>
    internal static Exercise? Map(WgerExercise r)
    {
        var translations = r.Translations ?? r.Exercises ?? [];
        var en = translations.FirstOrDefault(t => t.Language == LanguageEnglish && !string.IsNullOrWhiteSpace(t.Name));
        var pl = translations.FirstOrDefault(t => t.Language == LanguagePolish && !string.IsNullOrWhiteSpace(t.Name));
        if ((en is null && pl is null) || string.IsNullOrWhiteSpace(r.Uuid)) return null;

        var nameEn = Clean(en?.Name ?? pl!.Name!, 200);
        var namePl = Clean(pl?.Name ?? en!.Name!, 200);
        var category = r.Category?.Id == CategoryCardio ? ExerciseCategory.Cardio : ExerciseCategory.Strength;
        var equipment = MapEquipment(r.Equipment ?? []);
        var primary = MapMuscles(r.Muscles ?? []);
        var secondary = MapMuscles(r.MusclesSecondary ?? []).Except(primary).ToList();

        var images = (r.Images ?? [])
            .Where(i => IsHttp(i.Image))
            .OrderByDescending(i => i.IsMain)
            .Select(i => i.Image!)
            .Distinct()
            .Take(6)
            .ToList();
        var video = (r.Videos ?? []).Where(v => IsHttp(v.Video)).OrderByDescending(v => v.IsMain).FirstOrDefault();

        return new Exercise
        {
            OwnerTrainerUserId = null,
            Visibility = ExerciseVisibility.Public,
            Source = ExerciseSource.Wger,
            SourceKey = KeyPrefix + r.Uuid,
            NameEn = nameEn,
            NamePl = namePl,
            DescriptionEn = HtmlToText(en?.Description),
            DescriptionPl = HtmlToText(pl?.Description),
            PrimaryMuscles = string.Join(",", primary),
            SecondaryMuscles = string.Join(",", secondary),
            Category = category,
            Level = ExerciseLevel.Beginner,
            Equipment = equipment,
            ImageUrls = string.Join(",", images),
            VideoType = video is null ? ExerciseVideoType.None : ExerciseVideoType.Url,
            VideoRef = video?.Video,
            Tracking = ExerciseTrackingRules.Guess(category, equipment, nameEn, namePl),
            Attribution = BuildAttribution(r, video)
        };
    }

    private static void Copy(Exercise from, Exercise to)
    {
        to.NameEn = from.NameEn;
        to.NamePl = from.NamePl;
        to.DescriptionEn = from.DescriptionEn;
        to.DescriptionPl = from.DescriptionPl;
        to.PrimaryMuscles = from.PrimaryMuscles;
        to.SecondaryMuscles = from.SecondaryMuscles;
        to.Category = from.Category;
        to.Equipment = from.Equipment;
        to.ImageUrls = from.ImageUrls;
        to.VideoType = from.VideoType;
        to.VideoRef = from.VideoRef;
        to.Attribution = from.Attribution;
        to.Tracking ??= from.Tracking; // ręcznej zmiany typu pomiaru nie nadpisujemy
    }

    // Słowniki wger (fixtures: muscles.json, equipment.json) → kanoniczne klucze katalogu.
    private static readonly Dictionary<int, string> MuscleById = new()
    {
        [1] = "biceps", [2] = "shoulders", [3] = "chest", [4] = "chest", [5] = "triceps",
        [6] = "abdominals", [7] = "calves", [8] = "glutes", [9] = "traps", [10] = "quadriceps",
        [11] = "hamstrings", [12] = "lats", [13] = "biceps", [14] = "abdominals", [15] = "calves",
        [16] = "lower back"
    };

    private static readonly Dictionary<string, string> MuscleByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["biceps"] = "biceps", ["shoulders"] = "shoulders", ["chest"] = "chest", ["triceps"] = "triceps",
        ["abs"] = "abdominals", ["calves"] = "calves", ["glutes"] = "glutes", ["quads"] = "quadriceps",
        ["hamstrings"] = "hamstrings", ["lats"] = "lats", ["lower back"] = "lower back", ["trapezius"] = "traps",
        ["forearms"] = "forearms", ["obliques"] = "abdominals"
    };

    private static List<string> MapMuscles(IEnumerable<WgerMuscle> muscles) =>
        muscles.Select(m => MuscleById.TryGetValue(m.Id, out var k) ? k
                : MuscleByName.TryGetValue(m.NameEn ?? "", out var n) ? n
                : MuscleByName.TryGetValue(m.Name ?? "", out var n2) ? n2 : null)
            .Where(k => k is not null)
            .Select(k => k!)
            .Distinct()
            .ToList();

    // Kolejność = priorytet: przy „ławka + hantle” liczy się hantla.
    private static readonly (int Id, string Name, string Key)[] EquipmentMap =
    [
        (1, "barbell", "barbell"),
        (2, "sz-bar", "e-z curl bar"),
        (3, "dumbbell", "dumbbell"),
        (10, "kettlebell", "kettlebells"),
        (12, "cable machine", "cable"),
        (11, "resistance band", "bands"),
        (5, "swiss ball", "exercise ball"),
        (6, "pull-up bar", "body only"),
        (8, "bench", "body only"),
        (9, "incline bench", "body only"),
        (4, "gym mat", "body only"),
        (7, "none (bodyweight exercise)", "body only")
    ];

    private static string? MapEquipment(IReadOnlyCollection<WgerNamed> equipment)
    {
        if (equipment.Count == 0) return null;
        foreach (var (id, name, key) in EquipmentMap)
            if (equipment.Any(e => e.Id == id || string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                return key;
        return "other";
    }

    private static string BuildAttribution(WgerExercise r, WgerVideo? video)
    {
        var authors = (r.TotalAuthorsHistory ?? r.AuthorHistory ?? [])
            .Concat((r.Images ?? []).Select(i => i.LicenseAuthor))
            .Concat(video is null ? [] : [video.LicenseAuthor])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var license = r.License?.ShortName?.Trim();
        var text = "wger.de"
            + (string.IsNullOrWhiteSpace(license) ? "" : $" · licencja {license}")
            + (authors.Count > 0 ? $" · autorzy: {string.Join(", ", authors)}" : "");
        return text.Length > 1000 ? text[..997] + "…" : text;
    }

    private static bool IsHttp(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "https" or "http";

    private static string Clean(string s, int max)
    {
        s = WebUtility.HtmlDecode(s).Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>Opis wger jest w HTML — zamieniamy na zwykły tekst z krokami w osobnych liniach.</summary>
    internal static string? HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var s = BlockEnd().Replace(html, "\n");
        s = Tags().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        var lines = s.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        var text = string.Join("\n", lines);
        return text.Length == 0 ? null : text.Length > 8000 ? text[..8000] : text;
    }

    [GeneratedRegex(@"<\s*(br\s*/?|/p|/li|/h[1-6]|/div)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();
    [GeneratedRegex(@"[ \t\r ]+")]
    private static partial Regex Spaces();

    // ----- Kształt odpowiedzi /api/v2/exerciseinfo/ (serializers.ExerciseInfoSerializer) -----
    internal sealed class WgerPage
    {
        public int Count { get; set; }
        public string? Next { get; set; }
        public List<WgerExercise> Results { get; set; } = [];
    }

    internal sealed class WgerExercise
    {
        public int Id { get; set; }
        public string? Uuid { get; set; }
        public WgerNamed? Category { get; set; }
        public List<WgerMuscle>? Muscles { get; set; }
        public List<WgerMuscle>? MusclesSecondary { get; set; }
        public List<WgerNamed>? Equipment { get; set; }
        public WgerLicense? License { get; set; }
        public List<WgerImage>? Images { get; set; }
        public List<WgerTranslation>? Translations { get; set; }
        /// <summary>Starsze wersje API nazywały tłumaczenia „exercises”.</summary>
        public List<WgerTranslation>? Exercises { get; set; }
        public List<WgerVideo>? Videos { get; set; }
        public List<string>? AuthorHistory { get; set; }
        public List<string>? TotalAuthorsHistory { get; set; }
    }

    internal sealed class WgerNamed { public int Id { get; set; } public string? Name { get; set; } }
    internal sealed class WgerMuscle { public int Id { get; set; } public string? Name { get; set; } public string? NameEn { get; set; } }
    internal sealed class WgerLicense { public int Id { get; set; } public string? ShortName { get; set; } }
    internal sealed class WgerImage { public string? Image { get; set; } public bool IsMain { get; set; } public string? LicenseAuthor { get; set; } }
    internal sealed class WgerVideo { public string? Video { get; set; } public bool IsMain { get; set; } public string? LicenseAuthor { get; set; } }

    internal sealed class WgerTranslation
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int Language { get; set; }
    }
}
