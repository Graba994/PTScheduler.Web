using System.Collections.Concurrent;
using System.Diagnostics;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Pod jakim adresem Portal widzi instancję trenera. Kolejno: nazwa kontenera w sieci instancji,
/// Portal:ForwardHost i port instancji,
/// host Dockera (host.docker.internal, brama 172.17.0.1), a na końcu publiczna domena przez proxy.
/// Pierwszy adres, który odpowiada na /health, zapamiętujemy na 10 minut — dzięki temu zła
/// albo nierozwiązywalna nazwa hosta nie robi z działającej instancji „nieodpowiadającej”.
/// </summary>
public static class TenantEndpoint
{
    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly ConcurrentDictionary<int, (string Base, DateTime At)> Cache = new();
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    public sealed record ProbeResult(bool Ok, int Ms, string? Base, string? Error);

    public static IReadOnlyList<string> Candidates(IConfiguration config, Tenant t)
    {
        var list = new List<string>();
        // Najpierw bezpośrednio w sieci instancji (Portal dołącza do niej sam — DockerService.EnsureAttachedToTenantNetworkAsync).
        if (DockerService.InContainer) list.Add($"http://{t.WebContainerName ?? $"pt-{t.Slug}-web"}:8080");
        if (t.Port > 0)
        {
            var configured = config.GetValue<string>("Portal:ForwardHost");
            foreach (var host in new[] { configured, "host.docker.internal", "172.17.0.1", "192.168.0.220" })
                if (!string.IsNullOrWhiteSpace(host)) list.Add($"http://{host.Trim()}:{t.Port}");
        }
        if (!string.IsNullOrWhiteSpace(t.Domain)) list.Add($"https://{t.Domain.Trim().TrimEnd('/')}");
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adres do wywołań wewnętrznych (/internal/…): ostatnio działający albo pierwszy odpowiadający.</summary>
    public static async Task<string?> BaseUrlAsync(IConfiguration config, Tenant t, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(t.Id, out var hit) && DateTime.UtcNow - hit.At < CacheFor) return hit.Base;
        var result = await ProbeAsync(config, t, ct);
        return result.Base ?? Candidates(config, t).FirstOrDefault();
    }

    /// <summary>Sprawdza /health po kolei (od ostatnio działającego adresu); błędy wszystkich prób w jednym opisie.</summary>
    public static async Task<ProbeResult> ProbeAsync(IConfiguration config, Tenant t, CancellationToken ct = default)
    {
        var candidates = Candidates(config, t).ToList();
        if (Cache.TryGetValue(t.Id, out var hit) && candidates.Remove(hit.Base)) candidates.Insert(0, hit.Base);
        if (candidates.Count == 0) return new(false, 0, null, "Brak portu i domeny instancji.");

        var errors = new List<string>();
        var total = Stopwatch.StartNew();
        foreach (var baseUrl in candidates)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var resp = await Probe.GetAsync($"{baseUrl}/health", ct);
                if (resp.IsSuccessStatusCode)
                {
                    Cache[t.Id] = (baseUrl, DateTime.UtcNow);
                    return new(true, (int)sw.ElapsedMilliseconds, baseUrl, null);
                }
                errors.Add($"{baseUrl}: HTTP {(int)resp.StatusCode}");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { errors.Add($"{baseUrl}: brak odpowiedzi w 6 s"); }
            catch (HttpRequestException ex) { errors.Add($"{baseUrl}: {ex.Message}"); }
        }
        Cache.TryRemove(t.Id, out _);
        return new(false, (int)total.ElapsedMilliseconds, null, string.Join("; ", errors));
    }
}
