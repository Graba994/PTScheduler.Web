using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace PTScheduler.Guardian.Services;

/// <summary>Co sprzątanie usunęło — do dziennika zadania i panelu.</summary>
public sealed record CleanupResult(List<string> Containers, int Images, long ImageBytes, string? BuildCache, DateTime At)
{
    public bool Anything => Containers.Count > 0 || Images > 0 || BuildCache is not null;

    public string Summary =>
        !Anything ? "Nie było czego sprzątać."
        : string.Join(", ", new[]
        {
            Containers.Count > 0 ? $"kontenery: {Containers.Count}" : null,
            Images > 0 ? $"stare obrazy: {Images}{(ImageBytes >= 1 << 20 ? $" ({Janitor.Size(ImageBytes)})" : "")}" : null,
            BuildCache is not null ? $"cache budowania: {BuildCache}" : null
        }.Where(s => s is not null));
}

/// <summary>
/// Sprzątanie po aktualizacjach. Usuwa tylko to, czego na pewno nic nie potrzebuje:
/// <list type="bullet">
/// <item>zatrzymane kontenery próbne <c>ptportal-test-…</c>,</item>
/// <item>odłożone kopie <c>…-prev-…</c> — wyłącznie gdy kontener, którego są kopią, istnieje i działa
///   (inaczej kopia bywa jedyną drogą powrotu),</item>
/// <item>obrazy bez nazwy, których nie używa żaden kontener (tagi <c>:latest</c> i <c>:previous</c> zostają,
///   więc cofnięcie wersji nadal działa),</item>
/// <item>cache budowania starszy niż tydzień (świeży zostaje, żeby kolejne budowy były szybkie).</item>
/// </list>
/// Wolumenów, sieci ani kontenerów spoza platformy nie dotyka.
/// </summary>
public sealed partial class Janitor(DockerClient docker, IConfiguration config, ILogger<Janitor> logger)
{
    private static readonly TimeSpan MinAge = TimeSpan.FromMinutes(30);

    public bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable("GUARDIAN_AUTO_CLEANUP") ?? config["Guardian:AutoCleanup"], "false", StringComparison.OrdinalIgnoreCase);

    public CleanupResult? Last { get; private set; }

    /// <summary>Kontenery do usunięcia według reguł powyżej (bez usuwania) — używa też diagnostyka.</summary>
    public async Task<List<ContainerListResponse>> FindLeftoversAsync(string portalContainer, CancellationToken ct = default)
    {
        var all = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);
        var running = all.Where(c => c.State == "running").Select(NameOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cutoff = DateTime.UtcNow - MinAge;

        return all.Where(c =>
        {
            if (c.State == "running" || c.Created > cutoff) return false;
            var n = NameOf(c);
            if (n.StartsWith("ptportal-test-", StringComparison.Ordinal)) return true;
            var m = Backup().Match(n);
            if (!m.Success) return false;
            var original = m.Groups[1].Value;
            var isPlatform = original.Equals(portalContainer, StringComparison.OrdinalIgnoreCase) || TenantWeb().IsMatch(original);
            return isPlatform && running.Contains(original);
        }).ToList();
    }

    /// <summary>Sprząta i zwraca podsumowanie. Błędy pojedynczych kroków są logowane, nie przerywają reszty.</summary>
    public async Task<CleanupResult> CleanAsync(string portalContainer, Action<string>? log = null, CancellationToken ct = default)
    {
        var removed = new List<string>();
        try
        {
            foreach (var c in await FindLeftoversAsync(portalContainer, ct))
            {
                var name = NameOf(c);
                try
                {
                    await docker.Containers.RemoveContainerAsync(c.ID, new ContainerRemoveParameters { Force = true }, ct);
                    removed.Add(name);
                    log?.Invoke($"Usunięto pozostałość: {name}.");
                }
                catch (Exception ex) { logger.LogWarning(ex, "Nie usunięto {Container}.", name); }
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Sprzątanie: nie odczytano listy kontenerów."); }

        var images = 0;
        long bytes = 0;
        try
        {
            var filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["dangling"] = new Dictionary<string, bool> { ["true"] = true },
                ["until"] = new Dictionary<string, bool> { [$"{(int)MinAge.TotalMinutes}m"] = true }
            };
            var before = await docker.Images.ListImagesAsync(new ImagesListParameters { Filters = filters }, ct);
            var r = await docker.Images.PruneImagesAsync(new ImagesPruneParameters { Filters = filters }, ct);
            var after = await docker.Images.ListImagesAsync(new ImagesListParameters { Filters = filters }, ct);
            images = Math.Max(0, before.Count - after.Count);
            bytes = (long)r.SpaceReclaimed;
            if (images > 0) log?.Invoke($"Usunięto stare obrazy: {images}{(bytes >= 1 << 20 ? $" ({Size(bytes)})" : "")}.");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Sprzątanie: nie usunięto obrazów."); }

        string? cache = null;
        var (ok, output) = await UpgradeOrchestrator.Cli("docker", ["builder", "prune", "-f", "--filter", "until=168h"], timeoutMin: 5);
        if (ok)
        {
            var m = Reclaimed().Match(output);
            if (m.Success && !IsZero(m.Groups[1].Value))
            {
                cache = m.Groups[1].Value.Trim();
                log?.Invoke($"Wyczyszczono cache budowania starszy niż tydzień ({cache}).");
            }
        }
        else logger.LogDebug("Sprzątanie: builder prune nie zadziałał: {Output}", output);

        var result = new CleanupResult(removed, images, bytes, cache, DateTime.UtcNow);
        Last = result;
        logger.LogInformation("Sprzątanie: {Summary}", result.Summary);
        return result;
    }

    internal static string Size(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{Math.Max(0, bytes >> 20)} MB";

    private static bool IsZero(string size) => Regex.IsMatch(size.Trim(), @"^0(\.0+)?\s*[kMG]?B$", RegexOptions.IgnoreCase);

    private static string NameOf(ContainerListResponse c) => c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID[..12];

    [GeneratedRegex(@"^(.+)-prev-\d{14}$")]
    private static partial Regex Backup();
    [GeneratedRegex("^pt-[a-z0-9][a-z0-9-]*-web$")]
    private static partial Regex TenantWeb();
    [GeneratedRegex(@"Total(?:\s+reclaimed\s+space)?:\s*([0-9.]+\s*[kMGT]?B)", RegexOptions.IgnoreCase)]
    private static partial Regex Reclaimed();
}
