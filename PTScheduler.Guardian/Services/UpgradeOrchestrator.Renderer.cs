using System.Text.Json;
using Docker.DotNet.Models;

namespace PTScheduler.Guardian.Services;

/// <summary>
/// Renderer Studia reklam (kontener „hantly-renderer”, Node + Remotion + Chromium, katalog studio/ w repozytorium).
/// Działa tylko w sieci wewnętrznej Portalu (bez portu na zewnątrz) i ma podpięte to samo miejsce na filmy co Portal
/// (/opt/ptscheduler/studio w Portalu = /data w rendererze). Guardian go buduje, a przy pierwszej aktualizacji sam zakłada.
/// </summary>
public sealed partial class UpgradeOrchestrator
{
    internal const string StudioMountInPortal = "/opt/ptscheduler/studio";
    internal const string RendererDataDir = "/data";
    internal const int RendererPort = 3000;
    private const long RendererMemory = 3L * 1024 * 1024 * 1024; // render 2 klatki naraz + Chromium
    private const long RendererShm = 1L * 1024 * 1024 * 1024;     // Chromium potrzebuje więcej niż domyślne 64 MB /dev/shm
    private const long RendererCpus = 3_000_000_000;               // 3 z 4 rdzeni – Portal i studia zawsze mają zapas
    private static readonly TimeSpan RendererIdleWait = TimeSpan.FromMinutes(15);

    private static string RendererContainer => Environment.GetEnvironmentVariable("GUARDIAN_RENDERER_CONTAINER") ?? "hantly-renderer";
    private const string RendererRepo = "hantly-renderer";

    private string RendererHealthUrl => $"http://{RendererContainer}:{RendererPort}/health";

    public async Task<(bool Started, string JobId, string? Error)> StartRendererUpgradeAsync(string? requestedBy = null)
    {
        if (!await _semaphore.WaitAsync(StartWait))
            return (false, "", BusyMessage());

        var job = NewJob("renderer", UpgradeTarget.Renderer, requestedBy);
        _ = RunInBackground(job, ExecuteRendererUpgradeAsync);
        return (true, job.Id, null);
    }

    /// <summary>Stan renderera dla Portalu: czy jest kontener, czy działa i co mówi /health.</summary>
    public async Task<RendererStatus> GetRendererStatusAsync()
    {
        var inspect = await InspectOrNullAsync(RendererContainer);
        if (inspect is null) return new RendererStatus(false, false, null, null, null);
        JsonElement? health = null;
        try
        {
            using var resp = await _healthHttp.GetAsync(RendererHealthUrl);
            if (resp.IsSuccessStatusCode)
                health = JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
        }
        catch { /* niedostępny – status pokaże „nie odpowiada” */ }
        return new RendererStatus(true, inspect.State.Running, inspect.Config.Image, inspect.State.StartedAt, health);
    }

    private async Task ExecuteRendererUpgradeAsync(UpgradeJob job)
    {
        // ── PRE-CHECK ───────────────────────────────────────────
        Log(job, "info", "Queued", "Sprawdzam warunki wstępne...");
        if (!await EnsureRepoAsync(job)) return;

        var current = await InspectOrNullAsync(RendererContainer);
        string? studioDir = null, network = null;
        if (current is null)
        {
            (studioDir, network) = await RendererPlacementAsync(job);
            if (studioDir is null || network is null) return;
            Log(job, "info", "Queued", $"Renderera jeszcze nie ma — założę go (dane: {studioDir}, sieć: {network}).");
        }

        job.CommitBefore = await HeadCommit();
        SetStage(job, UpgradeStage.Pulling);
        if (!await GitPull(job)) return;
        job.CommitAfter = await HeadCommit();
        Log(job, "info", "Pulling", $"Commit: {Short(job.CommitAfter)}");

        if (!File.Exists(Path.Combine(_repoDir, "studio", "Dockerfile")))
        {
            Fail(job, "Pulling", "W repozytorium nie ma studio/Dockerfile — ta wersja kodu nie ma jeszcze renderera.");
            return;
        }

        // ── BUILD ───────────────────────────────────────────────
        SetStage(job, UpgradeStage.Building);
        const string pending = RendererRepo + ":pending";
        Log(job, "info", "Building", $"Buduję obraz {pending} (pierwszy raz ok. 10 min — pobiera Chromium)...");
        var (buildOk, buildOut) = await Cli("docker",
            ["build", "--build-arg", $"BUILD_COMMIT={job.CommitAfter}", "-t", pending, "-f", "studio/Dockerfile", "."],
            _repoDir, 30);
        if (!buildOk)
        {
            Log(job, "error", "Building", TrimOutput(buildOut));
            Fail(job, "Building", "Budowanie renderera nie powiodło się. Działająca wersja (jeśli jest) zostaje bez zmian.");
            await SafeRemoveImage(pending);
            return;
        }
        Log(job, "success", "Building", $"Obraz {pending} zbudowany.");

        // ── SWAP ────────────────────────────────────────────────
        SetStage(job, UpgradeStage.Swapping);
        if (current is not null && !await WaitForRendererIdleAsync(job))
        {
            await SafeRemoveImage(pending);
            return;
        }

        string? backupName = null;
        if (current is not null)
        {
            await SafeTagImage(current.Config.Image, RendererRepo, "previous");
            backupName = $"{RendererContainer}-prev-{DateTime.UtcNow:yyyyMMddHHmmss}";
            Log(job, "info", "Swapping", $"Zatrzymuję obecny renderer (zostaje jako '{backupName}')...");
            try { await _docker.Containers.StopContainerAsync(current.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 15 }); }
            catch (Exception ex) { _logger.LogDebug(ex, "Renderer był już zatrzymany."); }
            try { await _docker.Containers.RenameContainerAsync(current.ID, new ContainerRenameParameters { NewName = backupName }, CancellationToken.None); }
            catch (Exception ex)
            {
                await SafeStart(current.ID);
                await SafeRemoveImage(pending);
                Fail(job, "Swapping", $"Nie udało się odłożyć obecnego renderera: {ex.Message}. Uruchomiono go ponownie.");
                return;
            }
        }

        await SafeTagImage(pending, RendererRepo, "latest");
        string? newId = null;
        var started = DateTime.UtcNow;
        try
        {
            newId = current is not null
                ? await CreateLikeAsync(current, RendererContainer, RendererRepo + ":latest")
                : await CreateRendererAsync(studioDir!, network!);
            await _docker.Containers.StartContainerAsync(newId, new ContainerStartParameters());
            Log(job, "success", "Swapping", "Nowy renderer uruchomiony.");
        }
        catch (Exception ex)
        {
            Log(job, "error", "Swapping", $"Nowy renderer nie wystartował: {ex.Message}");
            await RestoreRendererAsync(job, newId, current?.ID, backupName);
            return;
        }

        // ── VERIFY ──────────────────────────────────────────────
        SetStage(job, UpgradeStage.Verifying);
        Log(job, "info", "Verifying", "Sprawdzam nowy renderer (max 120 s)...");
        var (ok, how) = await WaitForReadyAsync(newId, RendererHealthUrl, TimeSpan.FromSeconds(120), started, allowLogFallback: false);
        if (!ok)
        {
            var logs = await GetContainerLogs(newId, 50);
            Log(job, "error", "Verifying", $"Nowy renderer nie działa ({how}). Ostatnie logi:\n{logs}");
            await RestoreRendererAsync(job, newId, current?.ID, backupName);
            return;
        }

        // ── DONE ────────────────────────────────────────────────
        if (current is not null)
        {
            try { await _docker.Containers.RemoveContainerAsync(current.ID, new ContainerRemoveParameters { Force = true }); }
            catch (Exception ex) { Log(job, "warn", "Done", $"Nie usunięto poprzedniego renderera ({backupName}): {ex.Message}"); }
        }
        await SafeRemoveImage(pending);
        job.Stage = UpgradeStage.Done;
        job.Status = UpgradeStatus.Success;
        Log(job, "success", "Done", current is null
            ? $"Renderer założony i działa ({how}). Wersja {Short(job.CommitAfter)}."
            : $"Renderer zaktualizowany ({how}). {Short(job.CommitBefore)} → {Short(job.CommitAfter)}");
    }

    /// <summary>
    /// Gdzie postawić nowy renderer: dane w tym samym katalogu serwera, który Portal ma jako miejsce na filmy,
    /// sieć taka jak Portalu. Ręcznie: GUARDIAN_STUDIO_DIR i GUARDIAN_RENDERER_NETWORK.
    /// </summary>
    private async Task<(string? StudioDir, string? Network)> RendererPlacementAsync(UpgradeJob job)
    {
        var portal = await InspectOrNullAsync(_portalContainer);
        var studioDir = Environment.GetEnvironmentVariable("GUARDIAN_STUDIO_DIR") is { Length: > 0 } dir ? dir
            : portal?.Mounts?.FirstOrDefault(m => m.Destination == StudioMountInPortal)?.Source;
        if (string.IsNullOrWhiteSpace(studioDir))
        {
            Fail(job, "Queued",
                $"Portal nie ma podpiętego miejsca na filmy ({StudioMountInPortal}), więc renderer nie miałby gdzie zapisywać. " +
                "Podepnij je raz na serwerze: STUDIO_DIR=/mnt/user/hantly-studio bash deploy/update-portal.sh — i spróbuj ponownie.");
            return (null, null);
        }

        var network = Environment.GetEnvironmentVariable("GUARDIAN_RENDERER_NETWORK") is { Length: > 0 } net ? net
            : portal?.HostConfig?.NetworkMode is { Length: > 0 } mode && mode is not ("default" or "bridge" or "host") ? mode
            : null;
        if (network is null)
        {
            Fail(job, "Queued", "Nie wiem, w jakiej sieci Dockera postawić renderer (Portal nie ma własnej sieci). Ustaw GUARDIAN_RENDERER_NETWORK w Guardianie.");
            return (null, null);
        }
        return (studioDir, network);
    }

    /// <summary>Pierwsze założenie renderera: bez portów na zewnątrz, z limitami, z restartem po awarii.</summary>
    private async Task<string> CreateRendererAsync(string studioDir, string network)
    {
        var created = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = RendererContainer,
            Image = RendererRepo + ":latest",
            Env = ["RENDER_CONCURRENCY=2"],
            Labels = new Dictionary<string, string> { ["pl.hantly.role"] = "renderer" },
            HostConfig = new HostConfig
            {
                Binds = [$"{studioDir}:{RendererDataDir}"],
                NetworkMode = network,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                Memory = RendererMemory,
                ShmSize = RendererShm,
                NanoCPUs = RendererCpus,
            },
            NetworkingConfig = new NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, EndpointSettings> { [network] = new() { Aliases = [RendererContainer] } }
            }
        });
        return created.ID;
    }

    /// <summary>Czeka, aż renderer skończy render, zrzuty albo nagrywanie głosu – podmiana w trakcie zepsułaby film.</summary>
    private async Task<bool> WaitForRendererIdleAsync(UpgradeJob job)
    {
        var deadline = DateTime.UtcNow + RendererIdleWait;
        var said = false;
        while (DateTime.UtcNow < deadline)
        {
            string? busy;
            try
            {
                using var resp = await _healthHttp.GetAsync(RendererHealthUrl);
                busy = resp.IsSuccessStatusCode ? RendererBusy(await resp.Content.ReadAsStringAsync()) : null;
            }
            catch { return true; } // nie odpowiada – nie ma czego przerywać
            if (busy is null) return true;
            if (!said) { Log(job, "info", "Swapping", $"Renderer pracuje ({busy}) — czekam, aż skończy (max {RendererIdleWait.TotalMinutes:0} min)..."); said = true; }
            await Task.Delay(10_000);
        }
        Fail(job, "Swapping", "Renderer pracuje dłużej niż 15 minut — nie przerywam. Spróbuj, gdy skończy; obecna wersja działa bez zmian.");
        return false;
    }

    /// <summary>Opis trwającej pracy z /health renderera albo null, gdy jest wolny.</summary>
    internal static string? RendererBusy(string healthJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(healthJson);
            var root = doc.RootElement;
            var parts = new List<string>();
            if (root.TryGetProperty("busy", out var b) && b.ValueKind == JsonValueKind.String) parts.Add(b.GetString()!);
            if (root.TryGetProperty("queued", out var q) && q.TryGetInt32(out var nq) && nq > 0) parts.Add($"{nq} w kolejce");
            if (root.TryGetProperty("voicing", out var v) && v.TryGetInt32(out var nv) && nv > 0) parts.Add("nagrywa głos");
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
        catch (JsonException) { return null; }
    }

    private async Task RestoreRendererAsync(UpgradeJob job, string? newId, string? originalId, string? backupName)
    {
        try
        {
            if (newId is not null) await _docker.Containers.RemoveContainerAsync(newId, new ContainerRemoveParameters { Force = true });
            else await SafeRemoveContainer(RendererContainer);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Nie usunięto nieudanego renderera."); }

        if (originalId is null)
        {
            // pierwsza instalacja – nie ma do czego wracać; obraz zostaje do diagnozy
            job.Status = UpgradeStatus.Failed;
            job.Error = "Nowy renderer nie wstał — szczegóły w dzienniku zadania. Portal działa bez zmian.";
            Log(job, "error", "Verifying", job.Error);
            return;
        }

        Log(job, "warn", "Verifying", "Przywracam poprzedni renderer...");
        try
        {
            await _docker.Containers.RenameContainerAsync(originalId, new ContainerRenameParameters { NewName = RendererContainer }, CancellationToken.None);
            await SafeTagImage(RendererRepo + ":previous", RendererRepo, "latest");
            var restartedAt = DateTime.UtcNow;
            await _docker.Containers.StartContainerAsync(originalId, new ContainerStartParameters());
            var (ok, how) = await WaitForReadyAsync(originalId, RendererHealthUrl, TimeSpan.FromSeconds(90), restartedAt, allowLogFallback: false);
            job.Status = ok ? UpgradeStatus.RolledBack : UpgradeStatus.Failed;
            job.Error = ok ? "Nowa wersja renderera nie wstała — przywrócono poprzednią." : $"Przywrócono poprzedni renderer, ale on też nie odpowiada ({how}).";
            Log(job, ok ? "warn" : "error", "Verifying", ok ? $"Poprzedni renderer działa ({how})." : job.Error);
        }
        catch (Exception ex)
        {
            job.Status = UpgradeStatus.Failed;
            job.Error = $"Przywracanie renderera nie powiodło się: {ex.Message}. Poprzedni kontener leży jako '{backupName}' — " +
                        $"uruchom go: docker rename {backupName} {RendererContainer} && docker start {RendererContainer}";
            Log(job, "error", "Verifying", job.Error);
        }
    }

    private async Task<ContainerInspectResponse?> InspectOrNullAsync(string name)
    {
        try { return await _docker.Containers.InspectContainerAsync(name); }
        catch (Docker.DotNet.DockerContainerNotFoundException) { return null; }
    }
}

public sealed record RendererStatus(bool Exists, bool Running, string? Image, string? StartedAt, JsonElement? Health);
