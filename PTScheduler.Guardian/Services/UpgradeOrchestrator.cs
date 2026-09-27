using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace PTScheduler.Guardian.Services;

public sealed partial class UpgradeOrchestrator : IDisposable
{
    /// <summary>
    /// Ile start nowego zadania czeka na zakończenie poprzedniego. Poprzednie zadanie zapisuje
    /// „Success” chwilę przed zwolnieniem blokady — bez czekania Portal dostawał 409
    /// i wdrożenie po zbudowaniu obrazu nie ruszało.
    /// </summary>
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TenantReadyTimeout = TimeSpan.FromSeconds(150);

    private readonly DockerClient _docker;
    private readonly LogStore _logStore;
    private readonly HealthWatcher _healthWatcher;
    private readonly ILogger<UpgradeOrchestrator> _logger;
    private readonly HttpClient _healthHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _repoDir;
    private readonly string _portalContainer;
    private readonly string _portalImage;
    private readonly string _tenantImage;
    private readonly string _branch;
    private readonly int _portalPort;
    private readonly string _portalUrl;
    private readonly string _guardianSecret;
    private readonly string _defaultTenantHost;
    private string? _gitAuthHeader; // nagłówek Authorization dla gita — tylko w pamięci, nigdy w .git/config
    private readonly List<string> _secretsToScrub = [];

    private volatile UpgradeJob? _activeJob;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly DateTime _bootTime = DateTime.UtcNow;

    public UpgradeOrchestrator(
        DockerClient docker, LogStore logStore, HealthWatcher healthWatcher,
        ILogger<UpgradeOrchestrator> logger, IConfiguration config)
    {
        _docker = docker;
        _logStore = logStore;
        _healthWatcher = healthWatcher;
        _logger = logger;

        _repoDir = Cfg("GUARDIAN_REPO_DIR", config["Guardian:RepoDir"], "/opt/ptscheduler/repo");
        _portalContainer = Cfg("GUARDIAN_PORTAL_CONTAINER", config["Guardian:PortalContainer"], "ptportal");
        _portalImage = Cfg("GUARDIAN_PORTAL_IMAGE", config["Guardian:PortalImage"], "ptportal:latest");
        _tenantImage = Cfg("GUARDIAN_TENANT_IMAGE", config["Guardian:TenantImage"], "ptscheduler-web:latest");
        _branch = Cfg("GUARDIAN_BRANCH", config["Guardian:Branch"], "master");
        _portalPort = int.TryParse(Cfg("GUARDIAN_PORTAL_PORT", config["Guardian:PortalPort"], "8081"), out var p) ? p : 8081;
        _portalUrl = Cfg("GUARDIAN_PORTAL_URL", config["Guardian:PortalUrl"], "http://ptportal:8081");
        _guardianSecret = Environment.GetEnvironmentVariable("GUARDIAN_SECRET") ?? config["Guardian:Secret"] ?? "";
        _defaultTenantHost = Cfg("GUARDIAN_TENANT_HOST", config["Guardian:TenantHost"], "host.docker.internal");

        if (!SafeRef().IsMatch(_branch))
            throw new InvalidOperationException($"GUARDIAN_BRANCH '{_branch}' zawiera niedozwolone znaki.");
    }

    public string? ActiveJobId => _activeJob?.Id;
    public string TenantImage => _tenantImage;
    public TimeSpan Uptime => DateTime.UtcNow - _bootTime;

    public UpgradeJob? GetJob(string id) =>
        _activeJob?.Id == id ? SnapshotFromDisk(id) : _logStore.Load(id);

    public List<UpgradeJob> GetHistory(int limit = 20) => _logStore.GetHistory(limit);

    // ── Portal upgrade ──────────────────────────────────────────────

    public async Task<(bool Started, string JobId, string? Error)> StartPortalUpgradeAsync(string? requestedBy = null)
    {
        if (!await _semaphore.WaitAsync(StartWait))
            return (false, "", BusyMessage());

        var job = NewJob("portal", UpgradeTarget.Portal, requestedBy);
        _ = RunInBackground(job, ExecutePortalUpgradeAsync);
        return (true, job.Id, null);
    }

    private async Task ExecutePortalUpgradeAsync(UpgradeJob job)
    {
        // ── PRE-CHECK ───────────────────────────────────────────
        Log(job, "info", "Queued", "Sprawdzam warunki wstępne...");
        if (!Directory.Exists(_repoDir))
        {
            Fail(job, "Queued", $"Katalog repo '{_repoDir}' nie istnieje.");
            return;
        }

        ContainerInspectResponse inspect;
        try
        {
            inspect = await _docker.Containers.InspectContainerAsync(_portalContainer);
        }
        catch
        {
            Fail(job, "Queued", $"Nie mogę zinspekcjonować kontenera '{_portalContainer}'.");
            return;
        }

        job.CommitBefore = await HeadCommit();
        Log(job, "info", "Queued", $"Aktualny commit: {Short(job.CommitBefore)}");

        // ── PULL ────────────────────────────────────────────────
        SetStage(job, UpgradeStage.Pulling);
        if (!await GitPull(job)) return;

        job.CommitAfter = await HeadCommit();
        Log(job, "info", "Pulling", $"Nowy commit: {Short(job.CommitAfter)}");

        // ── BUILD ───────────────────────────────────────────────
        SetStage(job, UpgradeStage.Building);
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        Log(job, "info", "Building", "Buduję obraz ptportal:pending (10-15 min)...");

        var (buildOk, buildOut) = await Cli("docker",
            ["build", "--build-arg", $"BUILD_COMMIT={job.CommitAfter}", "--build-arg", $"BUILD_TIME={now}",
             "--build-arg", $"BUILD_BRANCH={_branch}", "-t", "ptportal:pending", "-f", "PTScheduler.Portal/Dockerfile", "."],
            _repoDir, 25);
        if (!buildOk)
        {
            Log(job, "error", "Building", TrimOutput(buildOut));
            Fail(job, "Building", "Docker build portalu nie powiódł się.");
            await SafeRemoveImage("ptportal:pending");
            return;
        }
        Log(job, "success", "Building", "Obraz ptportal:pending zbudowany.");

        // ── TEST ────────────────────────────────────────────────
        // Kontener próbny ma te same sieci co Portal (np. sieć z bazą danych), ale bez portów,
        // aliasów i restartów. Jeśli nie wstanie — przerywamy, zanim dotkniemy działającego Portalu.
        SetStage(job, UpgradeStage.Testing);
        var testName = $"ptportal-test-{DateTime.UtcNow:yyyyMMddHHmmss}";
        Log(job, "info", "Testing", $"Uruchamiam próbną wersję obok działającego Portalu ('{testName}')...");

        string testId;
        var testStarted = DateTime.UtcNow;
        try
        {
            testId = await CreateLikeAsync(inspect, testName, "ptportal:pending", testContainer: true);
            await _docker.Containers.StartContainerAsync(testId, new ContainerStartParameters());
        }
        catch (Exception ex)
        {
            Fail(job, "Testing", $"Nie udało się uruchomić wersji próbnej: {ex.Message}. Portal działa bez zmian.");
            await SafeRemoveContainer(testName);
            await SafeRemoveImage("ptportal:pending");
            return;
        }

        var (testOk, testHow) = await WaitForReadyAsync(testId, $"http://{testName}:{_portalPort}/health",
            TimeSpan.FromSeconds(120), testStarted, allowLogFallback: true);
        if (!testOk)
        {
            var logs = await GetContainerLogs(testId, 60);
            Log(job, "error", "Testing", $"Wersja próbna nie działa ({testHow}). Ostatnie logi:\n{logs}");
            await SafeStopAndRemove(testId);
            await SafeRemoveImage("ptportal:pending");
            Fail(job, "Testing", $"Nowa wersja Portalu nie wstała ({testHow}) — przerwano przed podmianą, Portal działa bez zmian.");
            return;
        }
        await SafeStopAndRemove(testId);
        Log(job, "success", "Testing", $"Wersja próbna działa ({testHow}).");

        // ── SWAP ────────────────────────────────────────────────
        // Stary kontener nie jest usuwany, tylko odkładany — przy problemie wraca dokładnie on
        // (ze wszystkimi sieciami, wolumenami i ustawieniami).
        SetStage(job, UpgradeStage.Swapping);
        await SafeTagImage(_portalImage, "ptportal", "previous");
        var backupName = $"{_portalContainer}-prev-{DateTime.UtcNow:yyyyMMddHHmmss}";

        Log(job, "info", "Swapping", $"Zatrzymuję obecny Portal (zostaje jako '{backupName}')...");
        try { await _docker.Containers.StopContainerAsync(inspect.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 20 }); }
        catch (Exception ex) { _logger.LogDebug(ex, "Portal był już zatrzymany."); }
        try { await _docker.Containers.RenameContainerAsync(inspect.ID, new ContainerRenameParameters { NewName = backupName }, CancellationToken.None); }
        catch (Exception ex)
        {
            await SafeStart(inspect.ID);
            await SafeRemoveImage("ptportal:pending");
            Fail(job, "Swapping", $"Nie udało się odłożyć obecnego Portalu: {ex.Message}. Uruchomiono go ponownie.");
            return;
        }

        await SafeTagImage("ptportal:pending", "ptportal", "latest");
        string? newId = null;
        var swapStarted = DateTime.UtcNow;
        try
        {
            newId = await CreateLikeAsync(inspect, _portalContainer, _portalImage);
            await _docker.Containers.StartContainerAsync(newId, new ContainerStartParameters());
            Log(job, "success", "Swapping", "Nowy Portal uruchomiony.");
        }
        catch (Exception ex)
        {
            Log(job, "error", "Swapping", $"Nowy Portal nie wystartował: {ex.Message}");
            await RestorePortalAsync(job, newId, inspect.ID, backupName);
            return;
        }

        // ── VERIFY ──────────────────────────────────────────────
        SetStage(job, UpgradeStage.Verifying);
        Log(job, "info", "Verifying", "Sprawdzam nowy Portal (max 120 s)...");
        var (ok, how) = await WaitForReadyAsync(newId, $"http://{_portalContainer}:{_portalPort}/health",
            TimeSpan.FromSeconds(120), swapStarted, allowLogFallback: true);
        if (!ok)
        {
            var logs = await GetContainerLogs(newId, 50);
            Log(job, "error", "Verifying", $"Nowy Portal nie działa ({how}). Ostatnie logi:\n{logs}");
            await RestorePortalAsync(job, newId, inspect.ID, backupName);
            return;
        }

        // ── DONE ────────────────────────────────────────────────
        try { await _docker.Containers.RemoveContainerAsync(inspect.ID, new ContainerRemoveParameters { Force = true }); }
        catch (Exception ex) { Log(job, "warn", "Done", $"Nie usunięto poprzedniego kontenera ({backupName}): {ex.Message}"); }
        await SafeRemoveImage("ptportal:pending");
        job.Stage = UpgradeStage.Done;
        job.Status = UpgradeStatus.Success;
        Log(job, "success", "Done",
            $"Portal zaktualizowany ({how}). {Short(job.CommitBefore)} → {Short(job.CommitAfter)}");
    }

    /// <summary>Usuwa nieudany nowy Portal i przywraca odłożony — dokładnie ten sam kontener co przed aktualizacją.</summary>
    private async Task RestorePortalAsync(UpgradeJob job, string? newId, string originalId, string backupName)
    {
        Log(job, "warn", "Verifying", "Przywracam poprzedni Portal...");
        try
        {
            if (newId is not null)
                await _docker.Containers.RemoveContainerAsync(newId, new ContainerRemoveParameters { Force = true });
            else
                await SafeRemoveContainer(_portalContainer);
            await _docker.Containers.RenameContainerAsync(originalId, new ContainerRenameParameters { NewName = _portalContainer }, CancellationToken.None);
            await SafeTagImage("ptportal:previous", "ptportal", "latest");
            var restartedAt = DateTime.UtcNow;
            await _docker.Containers.StartContainerAsync(originalId, new ContainerStartParameters());
            var (ok, how) = await WaitForReadyAsync(originalId, $"http://{_portalContainer}:{_portalPort}/health",
                TimeSpan.FromSeconds(90), restartedAt, allowLogFallback: true);
            job.Status = ok ? UpgradeStatus.RolledBack : UpgradeStatus.Failed;
            job.Error = ok ? "Nowa wersja nie wstała — przywrócono poprzedni Portal." : $"Przywrócono poprzedni kontener, ale on też nie odpowiada ({how}).";
            Log(job, ok ? "warn" : "error", "Verifying", ok ? $"Poprzedni Portal działa ({how})." : job.Error);
        }
        catch (Exception ex)
        {
            job.Status = UpgradeStatus.Failed;
            job.Error = $"Przywracanie nie powiodło się: {ex.Message}. Poprzedni kontener leży jako '{backupName}' — uruchom go: docker rename {backupName} {_portalContainer} && docker start {_portalContainer}";
            Log(job, "error", "Verifying", job.Error);
        }
    }

    // ── Tenant upgrade (obraz) + opcjonalne wdrożenie ───────────────

    public async Task<(bool Started, string JobId, string? Error)> StartTenantUpgradeAsync(
        bool rebuildImage = true, TenantRollingRequest? rollout = null, string? requestedBy = null)
    {
        if (rollout is { Tenants.Count: > 0 } && ValidateRollout(rollout) is { } invalid)
            return (false, "", invalid);

        if (!await _semaphore.WaitAsync(StartWait))
            return (false, "", BusyMessage());

        var release = rebuildImage && rollout is { Tenants.Count: > 0 };
        var job = NewJob(release ? "release" : "tenant", release ? UpgradeTarget.TenantRelease : UpgradeTarget.Tenant, requestedBy);
        job.RebuildImage = rebuildImage;
        if (release) PrepareRollout(job, rollout!);
        _ = RunInBackground(job, j => ExecuteTenantUpgradeAsync(j, release ? rollout : null));
        return (true, job.Id, null);
    }

    private async Task ExecuteTenantUpgradeAsync(UpgradeJob job, TenantRollingRequest? rollout)
    {
        Log(job, "info", "Queued", "Sprawdzam warunki wstępne...");
        if (!Directory.Exists(_repoDir))
        {
            Fail(job, "Queued", $"Katalog repo '{_repoDir}' nie istnieje.");
            return;
        }

        job.CommitBefore = await HeadCommit();

        SetStage(job, UpgradeStage.Pulling);
        if (!await GitPull(job)) return;

        job.CommitAfter = await HeadCommit();
        Log(job, "info", "Pulling", $"Commit: {Short(job.CommitBefore)} → {Short(job.CommitAfter)}");

        if (!job.RebuildImage)
        {
            job.Stage = UpgradeStage.Done;
            job.Status = UpgradeStatus.Success;
            Log(job, "success", "Done", "Pull zakończony (rebuild obrazu pominięty).");
            return;
        }

        SetStage(job, UpgradeStage.Building);
        var repo = _tenantImage.Split(':')[0];
        var prevTag = $"{repo}:previous";
        Log(job, "info", "Building", "Zapamiętuję obecny obraz aplikacji jako :previous...");
        await SafeTagImage(_tenantImage, repo, "previous");

        Log(job, "info", "Building", "Buduję nowy obraz aplikacji trenerów (kilka–kilkanaście minut)...");
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var (buildOk, buildOut) = await Cli("docker",
            ["build", "--build-arg", $"BUILD_COMMIT={job.CommitAfter}", "--build-arg", $"BUILD_TIME={now}",
             "--build-arg", $"BUILD_BRANCH={_branch}", "-t", _tenantImage, "."],
            _repoDir, 25);
        if (!buildOk)
        {
            Log(job, "error", "Building", TrimOutput(buildOut));
            Log(job, "warn", "Building", "Przywracam poprzedni obraz...");
            await SafeTagImage(prevTag, repo, "latest");
            Fail(job, "Building", "Budowanie obrazu aplikacji trenerów nie powiodło się — instancje dalej działają na poprzedniej wersji.");
            return;
        }
        Log(job, "success", "Building", $"Obraz aplikacji zbudowany ({Short(job.CommitAfter)}).");

        if (rollout is null)
        {
            job.Stage = UpgradeStage.Done;
            job.Status = UpgradeStatus.Success;
            Log(job, "success", "Done", "Obraz gotowy. Instancje dostaną go przy wdrożeniu.");
            return;
        }

        // Wdrożenie w tym samym zadaniu — bez drugiego wywołania z Portalu, więc bez wyścigu.
        await RolloutAsync(job, rollout);
    }

    // ── Tenant rolling update (sam obraz już jest) ───────────────────

    public async Task<(bool Started, string JobId, string? Error)> StartTenantRollingUpdateAsync(
        TenantRollingRequest request, string? requestedBy = null)
    {
        if (request.Tenants.Count == 0)
            return (false, "", "Brak tenantów do aktualizacji.");
        if (ValidateRollout(request) is { } invalid)
            return (false, "", invalid);

        if (!await _semaphore.WaitAsync(StartWait))
            return (false, "", BusyMessage());

        var job = NewJob("tenant-rolling", UpgradeTarget.TenantRolling, requestedBy);
        PrepareRollout(job, request);
        _ = RunInBackground(job, async j =>
        {
            try { await _docker.Images.InspectImageAsync(_tenantImage); }
            catch
            {
                Fail(j, "Queued", $"Obraz '{_tenantImage}' nie istnieje. Najpierw zbuduj aplikację trenerów.");
                return;
            }
            await RolloutAsync(j, request);
        });
        return (true, job.Id, null);
    }

    private static void PrepareRollout(UpgradeJob job, TenantRollingRequest request)
    {
        job.Concurrency = Math.Clamp(request.Concurrency, 1, 10);
        job.TenantsTotal = request.Tenants.Count;
        job.TenantResults = request.Tenants.Select(t => new TenantUpdateResult { Slug = t.Slug }).ToList();
    }

    /// <summary>Dane z Portalu trafiają do nazw kontenerów i adresów — przepuszczamy tylko bezpieczne wartości.</summary>
    private static string? ValidateRollout(TenantRollingRequest r)
    {
        if (r.Tenants.Count > 1000) return "Za dużo instancji w jednym zadaniu (max 1000).";
        foreach (var t in r.Tenants)
        {
            if (!SafeSlug().IsMatch(t.Slug)) return $"Nieprawidłowy identyfikator instancji: '{t.Slug}'.";
            if (t.Port is < 1 or > 65535) return $"Nieprawidłowy port instancji {t.Slug}: {t.Port}.";
        }
        if (r.Tenants.Select(t => t.Slug).Distinct().Count() != r.Tenants.Count) return "Powtórzona instancja na liście.";
        if (!string.IsNullOrWhiteSpace(r.HealthHost) && !SafeHost().IsMatch(r.HealthHost)) return "Nieprawidłowy adres hosta do sprawdzania instancji.";
        return null;
    }

    private async Task RolloutAsync(UpgradeJob job, TenantRollingRequest request)
    {
        var host = string.IsNullOrWhiteSpace(request.HealthHost) ? _defaultTenantHost : request.HealthHost.Trim();
        SetStage(job, UpgradeStage.Swapping);
        Log(job, "info", "Swapping",
            $"Wdrażam nową wersję na {request.Tenants.Count} {(request.Tenants.Count == 1 ? "instancję" : "instancji")} " +
            $"(po {job.Concurrency} naraz, sprawdzanie pod {host}).");

        var gate = new SemaphoreSlim(job.Concurrency);
        var stopRequested = 0;
        var tasks = new List<Task>();

        foreach (var tenant in request.Tenants)
        {
            var result = job.TenantResults!.First(r => r.Slug == tenant.Slug);
            await gate.WaitAsync();

            if (Volatile.Read(ref stopRequested) == 1)
            {
                gate.Release();
                result.Status = TenantUpdateStatus.Skipped;
                result.Error = "Pominięto — poprzednia instancja nie przeszła aktualizacji.";
                job.MarkTenantCompleted();
                Log(job, "warn", "Swapping", $"[{tenant.Slug}] Pominięto (zatrzymanie po pierwszym błędzie).");
                continue;
            }

            tasks.Add(Task.Run(async () =>
            {
                try { await UpdateSingleTenantAsync(job, tenant, result, host); }
                catch (Exception ex)
                {
                    result.Status = TenantUpdateStatus.Failed;
                    result.Error = $"Nieoczekiwany błąd: {ex.Message}";
                    result.CompletedAt = DateTime.UtcNow;
                    job.MarkTenantCompleted();
                    Log(job, "error", "Swapping", $"[{tenant.Slug}] {result.Error}");
                }
                finally
                {
                    if (request.StopOnFirstFailure && result.Status is TenantUpdateStatus.Failed or TenantUpdateStatus.RolledBack)
                        Volatile.Write(ref stopRequested, 1);
                    gate.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        var succeeded = job.TenantResults!.Count(r => r.Status == TenantUpdateStatus.Success);
        var failed = job.TenantResults!.Count(r => r.Status == TenantUpdateStatus.Failed);
        var rolledBack = job.TenantResults!.Count(r => r.Status == TenantUpdateStatus.RolledBack);
        var skipped = job.TenantResults!.Count(r => r.Status == TenantUpdateStatus.Skipped);

        job.Stage = UpgradeStage.Done;
        if (failed == 0 && rolledBack == 0 && skipped == 0)
        {
            job.Status = UpgradeStatus.Success;
            Log(job, "success", "Done", $"Gotowe — wszystkie instancje ({succeeded}) działają na nowej wersji.");
        }
        else if (succeeded == 0)
        {
            job.Status = UpgradeStatus.Failed;
            job.Error = $"Żadna instancja nie przeszła na nową wersję (wycofane: {rolledBack}, błędy: {failed}, pominięte: {skipped}).";
            Log(job, "error", "Done", job.Error + " Instancje działają na poprzedniej wersji — szczegóły wyżej.");
        }
        else
        {
            job.Status = UpgradeStatus.PartialSuccess;
            Log(job, "warn", "Done", $"Częściowo: nowa wersja na {succeeded}, wycofane {rolledBack}, błędy {failed}, pominięte {skipped}.");
        }
    }

    /// <summary>
    /// Podmiana jednej instancji. Stary kontener nie jest usuwany, tylko zatrzymany i przemianowany —
    /// jeśli nowa wersja nie wstanie, wraca dokładnie on (z tą samą konfiguracją, limitami i siecią).
    /// </summary>
    private async Task UpdateSingleTenantAsync(UpgradeJob job, TenantInfo tenant, TenantUpdateResult result, string host)
    {
        var name = $"pt-{tenant.Slug}-web";
        result.StartedAt = DateTime.UtcNow;
        result.Status = TenantUpdateStatus.Updating;
        Log(job, "info", "Swapping", $"[{tenant.Slug}] Start aktualizacji...");

        ContainerInspectResponse original;
        try { original = await _docker.Containers.InspectContainerAsync(name); }
        catch
        {
            Finish(job, result, TenantUpdateStatus.Failed, $"Kontener '{name}' nie istnieje — utwórz instancję ponownie z Portalu.");
            return;
        }

        var backupName = $"{name}-prev-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var wasRunning = original.State?.Running == true;

        try { await _docker.Containers.StopContainerAsync(original.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 20 }); }
        catch (Exception ex) { _logger.LogDebug(ex, "{Container} był już zatrzymany.", name); }

        try { await _docker.Containers.RenameContainerAsync(original.ID, new ContainerRenameParameters { NewName = backupName }, CancellationToken.None); }
        catch (Exception ex)
        {
            if (wasRunning) await SafeStart(original.ID);
            Finish(job, result, TenantUpdateStatus.Failed, $"Nie udało się odłożyć starej wersji: {ex.Message}");
            return;
        }

        string? newId = null;
        var startedAt = DateTime.UtcNow;
        try
        {
            newId = await CreateLikeAsync(original, name, _tenantImage);
            await _docker.Containers.StartContainerAsync(newId, new ContainerStartParameters());
        }
        catch (Exception ex)
        {
            Log(job, "error", "Swapping", $"[{tenant.Slug}] Nowy kontener nie wystartował: {ex.Message}");
            await RestoreTenantAsync(job, tenant.Slug, name, newId, original.ID, wasRunning, result);
            return;
        }

        result.Status = TenantUpdateStatus.HealthCheck;
        Log(job, "info", "Swapping", $"[{tenant.Slug}] Czekam, aż nowa wersja odpowie (max {(int)TenantReadyTimeout.TotalSeconds} s)...");

        var (ready, how) = await WaitForTenantReadyAsync(newId, host, tenant.Port, startedAt);
        if (!ready)
        {
            var logs = await GetContainerLogs(newId, 40);
            Log(job, "warn", "Swapping", $"[{tenant.Slug}] Nowa wersja nie działa ({how}). Ostatnie logi aplikacji:\n{logs}");
            await RestoreTenantAsync(job, tenant.Slug, name, newId, original.ID, wasRunning, result, how);
            return;
        }

        try { await _docker.Containers.RemoveContainerAsync(original.ID, new ContainerRemoveParameters { Force = true }); }
        catch (Exception ex) { Log(job, "warn", "Swapping", $"[{tenant.Slug}] Nie usunięto starej wersji ({backupName}): {ex.Message}"); }

        result.Detail = how;
        Finish(job, result, TenantUpdateStatus.Success, null, $"[{tenant.Slug}] Działa na nowej wersji ({how}).");
    }

    private async Task RestoreTenantAsync(UpgradeJob job, string slug, string name, string? newId, string originalId,
        bool wasRunning, TenantUpdateResult result, string? reason = null)
    {
        try
        {
            if (newId is not null)
                await _docker.Containers.RemoveContainerAsync(newId, new ContainerRemoveParameters { Force = true });
            await _docker.Containers.RenameContainerAsync(originalId, new ContainerRenameParameters { NewName = name }, CancellationToken.None);
            if (wasRunning) await _docker.Containers.StartContainerAsync(originalId, new ContainerStartParameters());
            Finish(job, result, TenantUpdateStatus.RolledBack,
                $"Nowa wersja nie wstała{(reason is null ? "" : $" ({reason})")} — przywrócono poprzednią.",
                $"[{slug}] Przywrócono poprzednią wersję — instancja działa jak przed aktualizacją.");
        }
        catch (Exception ex)
        {
            Finish(job, result, TenantUpdateStatus.Failed,
                $"Przywracanie nie powiodło się: {ex.Message}. Stara wersja leży jako zatrzymany kontener z sufiksem „-prev-”.");
        }
    }

    private void Finish(UpgradeJob job, TenantUpdateResult result, TenantUpdateStatus status, string? error, string? message = null)
    {
        result.Status = status;
        result.Error = error;
        result.CompletedAt = DateTime.UtcNow;
        job.MarkTenantCompleted();
        var level = status switch { TenantUpdateStatus.Success => "success", TenantUpdateStatus.RolledBack => "warn", _ => "error" };
        Log(job, level, "Swapping", message ?? $"[{result.Slug}] {error}");
    }

    /// <summary>
    /// Nowy kontener jak stary, ale z nowym obrazem: cała konfiguracja hosta (wolumeny, porty,
    /// limity pamięci/CPU, polityka restartu, logi), etykiety, zmienne i sieci z aliasami.
    /// </summary>
    /// <param name="testContainer">
    /// Kontener próbny: bez publikowanych portów (zajęte przez działający), bez aliasów sieciowych
    /// (inaczej część ruchu trafiałaby do próby) i bez automatycznych restartów.
    /// </param>
    private async Task<string> CreateLikeAsync(ContainerInspectResponse src, string name, string image, bool testContainer = false)
    {
        var host = src.HostConfig;
        var primary = host.NetworkMode ?? "bridge";
        var networks = src.NetworkSettings?.Networks ?? new Dictionary<string, EndpointSettings>();
        var shortId = src.ID.Length >= 12 ? src.ID[..12] : src.ID;
        IList<string>? AliasesOf(EndpointSettings e) =>
            testContainer ? null
            : e.Aliases?.Where(a => a != shortId && a != src.ID).ToList() is { Count: > 0 } list ? list : null;

        var savedPorts = host.PortBindings;
        var savedRestart = host.RestartPolicy;
        if (testContainer)
        {
            host.PortBindings = null;
            host.RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No };
        }
        try
        {
            return await CreateLikeCoreAsync(src, name, image, host, primary, networks, AliasesOf);
        }
        finally
        {
            host.PortBindings = savedPorts;
            host.RestartPolicy = savedRestart;
        }
    }

    private async Task<string> CreateLikeCoreAsync(ContainerInspectResponse src, string name, string image, HostConfig host,
        string primary, IDictionary<string, EndpointSettings> networks, Func<EndpointSettings, IList<string>?> AliasesOf)
    {
        var create = new CreateContainerParameters
        {
            Name = name,
            Image = image,
            Env = src.Config.Env ?? [],
            ExposedPorts = src.Config.ExposedPorts,
            Labels = src.Config.Labels,
            HostConfig = host,
            NetworkingConfig = networks.TryGetValue(primary, out var primaryEndpoint)
                ? new NetworkingConfig { EndpointsConfig = new Dictionary<string, EndpointSettings> { [primary] = new() { Aliases = AliasesOf(primaryEndpoint), Links = primaryEndpoint.Links } } }
                : null
        };
        var created = await _docker.Containers.CreateContainerAsync(create);

        foreach (var (net, endpoint) in networks)
        {
            if (net == primary) continue;
            try
            {
                await _docker.Networks.ConnectNetworkAsync(net, new NetworkConnectParameters
                {
                    Container = created.ID,
                    EndpointConfig = new EndpointSettings { Aliases = AliasesOf(endpoint), Links = endpoint.Links }
                });
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Nie podłączono {Container} do sieci {Network}.", name, net); }
        }
        return created.ID;
    }

    /// <summary>
    /// Czy nowa wersja działa: najpierw HTTP /health pod adresem hosta (jak Portal),
    /// a gdy Guardian w ogóle nie widzi tego adresu — kontener działa bez restartów
    /// i aplikacja zgłosiła w logach, że wystartowała.
    /// </summary>
    private Task<(bool Ok, string How)> WaitForTenantReadyAsync(string containerId, string host, int port, DateTime startedAt) =>
        WaitForReadyAsync(containerId, $"http://{host}:{port}/health", TenantReadyTimeout, startedAt, allowLogFallback: true,
            unreachableHint: $"Guardian nie widzi {host}:{port} — ustaw GUARDIAN_TENANT_HOST");

    /// <summary>
    /// Czy kontener działa: /health pod podanym adresem; gdy Guardian w ogóle nie widzi tego adresu
    /// (inna sieć), wystarczy start aplikacji w logach i brak restartów. Zatrzymanie albo restarty = porażka.
    /// </summary>
    private async Task<(bool Ok, string How)> WaitForReadyAsync(string containerId, string healthUrl, TimeSpan timeout,
        DateTime startedAt, bool allowLogFallback, string? unreachableHint = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var httpReachable = false;
        string? lastHttp = null;

        while (DateTime.UtcNow < deadline)
        {
            ContainerInspectResponse inspect;
            try { inspect = await _docker.Containers.InspectContainerAsync(containerId); }
            catch (Exception ex) { return (false, $"kontener zniknął: {ex.Message}"); }

            if (!inspect.State.Running)
                return (false, $"aplikacja się zatrzymała (kod wyjścia {inspect.State.ExitCode})");
            if (inspect.RestartCount > 0)
                return (false, $"aplikacja restartuje się w kółko ({inspect.RestartCount}×)");

            var sw = Stopwatch.StartNew();
            try
            {
                using var resp = await _healthHttp.GetAsync(healthUrl);
                httpReachable = true;
                if (resp.IsSuccessStatusCode) return (true, $"odpowiada na /health w {sw.ElapsedMilliseconds} ms");
                lastHttp = $"/health zwraca HTTP {(int)resp.StatusCode}";
            }
            catch (HttpRequestException) { /* jeszcze wstaje albo adres niewidoczny z Guardiana */ }
            catch (TaskCanceledException) { lastHttp = "/health nie odpowiada w 10 s"; }

            await Task.Delay(3_000);
        }

        if (!httpReachable && allowLogFallback)
        {
            var logs = await GetContainerLogs(containerId, 300, startedAt);
            var crashed = logs.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase);
            if (!crashed && (logs.Contains("Application started", StringComparison.OrdinalIgnoreCase)
                || logs.Contains("Now listening on", StringComparison.OrdinalIgnoreCase)))
                return (true, $"aplikacja wystartowała wg logów{(unreachableHint is null ? "" : $" ({unreachableHint})")}");
            return (false, crashed ? "aplikacja zgłosiła nieobsłużony wyjątek (szczegóły w logach)" : $"brak odpowiedzi z {healthUrl} i brak startu w logach");
        }
        return (false, lastHttp ?? "brak odpowiedzi /health");
    }

    // ── Portal rollback ─────────────────────────────────────────────

    public async Task<(bool Started, string JobId, string? Error)> RollbackPortalAsync(string? requestedBy = null)
    {
        if (!await _semaphore.WaitAsync(StartWait))
            return (false, "", BusyMessage());

        var job = NewJob("rollback", UpgradeTarget.Portal, requestedBy);
        job.Stage = UpgradeStage.Swapping;
        _ = RunInBackground(job, ExecuteRollbackAsync);
        return (true, job.Id, null);
    }

    private async Task ExecuteRollbackAsync(UpgradeJob job)
    {
        Log(job, "info", "Swapping", "Sprawdzam obraz ptportal:previous...");
        try { await _docker.Images.InspectImageAsync("ptportal:previous"); }
        catch
        {
            Fail(job, "Swapping", "Brak obrazu ptportal:previous — nie ma do czego wrócić.");
            return;
        }

        ContainerInspectResponse current;
        try { current = await _docker.Containers.InspectContainerAsync(_portalContainer); }
        catch
        {
            Fail(job, "Swapping", $"Nie ma kontenera '{_portalContainer}' — uruchom Portal ręcznie (docker compose / szablon Unraid).");
            return;
        }

        var backupName = $"{_portalContainer}-prev-{DateTime.UtcNow:yyyyMMddHHmmss}";
        Log(job, "info", "Swapping", $"Odkładam obecny Portal jako '{backupName}'...");
        try { await _docker.Containers.StopContainerAsync(current.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 20 }); } catch { /* już zatrzymany */ }
        await _docker.Containers.RenameContainerAsync(current.ID, new ContainerRenameParameters { NewName = backupName }, CancellationToken.None);

        string? newId = null;
        var startedAt = DateTime.UtcNow;
        try
        {
            // Ta sama konfiguracja i wszystkie sieci co obecny kontener — tylko obraz z poprzedniej wersji.
            newId = await CreateLikeAsync(current, _portalContainer, "ptportal:previous");
            await _docker.Containers.StartContainerAsync(newId, new ContainerStartParameters());
        }
        catch (Exception ex)
        {
            Log(job, "error", "Swapping", $"Nie udało się uruchomić poprzedniej wersji: {ex.Message}");
            if (newId is not null) await SafeRemoveContainer(newId);
            await _docker.Containers.RenameContainerAsync(current.ID, new ContainerRenameParameters { NewName = _portalContainer }, CancellationToken.None);
            await SafeStart(current.ID);
            Fail(job, "Swapping", "Przywracanie nie powiodło się — uruchomiono z powrotem obecny Portal.");
            return;
        }

        SetStage(job, UpgradeStage.Verifying);
        var (ok, how) = await WaitForReadyAsync(newId, $"http://{_portalContainer}:{_portalPort}/health",
            TimeSpan.FromSeconds(90), startedAt, allowLogFallback: true);
        job.Stage = UpgradeStage.Done;
        if (ok)
        {
            await SafeRemoveContainer(current.ID);
            await SafeTagImage("ptportal:previous", "ptportal", "latest");
            job.Status = UpgradeStatus.RolledBack;
            Log(job, "success", "Done", $"Portal działa na poprzedniej wersji ({how}).");
        }
        else
        {
            var logs = await GetContainerLogs(newId, 40);
            Log(job, "error", "Done", $"Poprzednia wersja też nie działa ({how}). Ostatnie logi:\n{logs}");
            await SafeRemoveContainer(newId);
            await _docker.Containers.RenameContainerAsync(current.ID, new ContainerRenameParameters { NewName = _portalContainer }, CancellationToken.None);
            await SafeStart(current.ID);
            Fail(job, "Done", "Poprzednia wersja nie wstała — przywrócono kontener sprzed tej operacji. Sprawdź logi (np. połączenie z bazą).");
        }
    }

    // ── Startup cleanup ─────────────────────────────────────────────

    public async Task CleanupOrphanedContainersAsync()
    {
        try
        {
            var containers = await _docker.Containers.ListContainersAsync(
                new ContainersListParameters { All = true });
            foreach (var c in containers)
            {
                if (c.Names.Any(n => n.Contains("ptportal-test-")))
                {
                    _logger.LogInformation("Cleaning orphaned test container: {Name}", c.Names.First());
                    await SafeStopAndRemove(c.ID);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cleanup failed");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private const string BusyPrefix = "Trwa inne zadanie";

    /// <summary>Odmowa, bo działa inne zadanie (409 — warto ponowić), a nie błąd danych (400).</summary>
    public static bool IsBusy(string? error) => error?.StartsWith(BusyPrefix, StringComparison.Ordinal) == true;

    private string BusyMessage() =>
        _activeJob is { } j ? $"{BusyPrefix} ({j.Target}, od {j.StartedAt:HH:mm} UTC) — poczekaj na jego koniec." : $"{BusyPrefix} — poczekaj na jego koniec.";

    private UpgradeJob NewJob(string suffix, UpgradeTarget target, string? requestedBy = null)
    {
        var job = new UpgradeJob
        {
            Id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}",
            Target = target,
            Stage = UpgradeStage.Queued,
            Status = UpgradeStatus.Running,
            StartedAt = DateTime.UtcNow,
            RequestedBy = requestedBy
        };
        _activeJob = job;
        _logStore.Save(job);
        return job;
    }

    private Task RunInBackground(UpgradeJob job, Func<UpgradeJob, Task> action)
    {
        return Task.Run(async () =>
        {
            try { await action(job); }
            catch (Exception ex)
            {
                Log(job, "error", job.Stage.ToString(), $"Nieoczekiwany wyjątek: {ex.Message}");
                job.Status = UpgradeStatus.Failed;
                job.Error = ex.Message;
            }
            finally
            {
                job.CompletedAt = DateTime.UtcNow;
                if (job.Status == UpgradeStatus.Running)
                    job.Status = UpgradeStatus.Failed;
                _activeJob = null;
                _semaphore.Release();
                _logStore.Save(job);
                _logStore.Prune();
            }
        });
    }

    private async Task<string> HeadCommit()
    {
        var (ok, output) = await Cli("git", ["rev-parse", "HEAD"], _repoDir);
        return ok ? output.Trim() : "unknown";
    }

    /// <summary>
    /// Pobranie zmian: fetch + fast-forward. Bez „git pull”, który potrafi utworzyć commit scalający
    /// albo utknąć na konflikcie; przy rozjechanej historii dostajesz czytelny komunikat.
    /// </summary>
    private async Task<bool> GitPull(UpgradeJob job)
    {
        await SyncGitCredentials(job);

        Log(job, "info", "Pulling", $"Pobieram zmiany z gałęzi {_branch}...");
        var (fetchOk, fetchOut) = await Git(["fetch", "--prune", "origin", _branch]);
        if (!fetchOk)
        {
            Fail(job, "Pulling", $"Nie udało się pobrać zmian z GitHuba: {TrimOutput(fetchOut)}");
            return false;
        }

        var (ffOk, ffOut) = await Git(["merge", "--ff-only", $"origin/{_branch}"]);
        if (!ffOk)
        {
            Fail(job, "Pulling",
                "Kod na serwerze rozjechał się z GitHubem (lokalne zmiany albo inna historia) — Guardian niczego nie nadpisał. " +
                $"Sprawdź repozytorium na serwerze (git status). Szczegóły: {TrimOutput(ffOut)}");
            return false;
        }
        Log(job, "success", "Pulling", "Zmiany pobrane.");
        return true;
    }

    /// <summary>
    /// Token GitHuba z Portalu trzymamy tylko w pamięci i podajemy gitowi jako nagłówek
    /// (-c http.extraHeader) — nie trafia do .git/config ani do logów zadania.
    /// Stary adres z tokenem w URL (poprzednie wersje Guardiana) zastępujemy czystym.
    /// </summary>
    private async Task SyncGitCredentials(UpgradeJob job)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_portalUrl}/api/internal/git-config");
            req.Headers.Add("X-Guardian-Secret", _guardianSecret);
            using var resp = await _healthHttp.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            var owner = doc.RootElement.TryGetProperty("owner", out var o) ? o.GetString() : null;
            var repo = doc.RootElement.TryGetProperty("repo", out var r) ? r.GetString() : null;

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)) return;
            if (!SafeRef().IsMatch(owner) || !SafeRef().IsMatch(repo))
            {
                Log(job, "warn", "Pulling", "Portal podał nieprawidłową nazwę repozytorium — pomijam synchronizację.");
                return;
            }

            await Cli("git", ["remote", "set-url", "origin", $"https://github.com/{owner}/{repo}.git"], _repoDir);
            if (!string.IsNullOrWhiteSpace(token))
            {
                _gitAuthHeader = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
                lock (_secretsToScrub) { _secretsToScrub.Add(token); }
            }
            Log(job, "info", "Pulling", "Dostęp do GitHuba pobrany z Portalu.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie pobrano dostępu do GitHuba z Portalu — używam ustawień repozytorium.");
        }
    }

    private Task<(bool Ok, string Output)> Git(IEnumerable<string> args)
    {
        var all = new List<string>();
        if (_gitAuthHeader is not null) all.AddRange(["-c", $"http.extraHeader={_gitAuthHeader}"]);
        all.AddRange(args);
        return Cli("git", all, _repoDir, 5);
    }





    private async Task<string> GetContainerLogs(string container, int tail, DateTime? sinceUtc = null)
    {
        try
        {
            var mux = await _docker.Containers.GetContainerLogsAsync(container,
                false, new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Tail = tail.ToString(),
                    Since = sinceUtc is { } since ? new DateTimeOffset(since.AddSeconds(-2)).ToUnixTimeSeconds().ToString() : null
                });
            var buffer = new byte[81920];
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                var result = await mux.ReadOutputAsync(buffer, 0, buffer.Length, default);
                if (result.Count == 0) break;
                sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            return Scrub(sb.ToString());
        }
        catch { return "(nie udało się pobrać logów)"; }
    }



    private async Task SafeStopAndRemove(string container)
    {
        try { await _docker.Containers.StopContainerAsync(container, new ContainerStopParameters { WaitBeforeKillSeconds = 10 }); } catch (Exception ex) { _logger.LogDebug(ex, "Nie zatrzymano {Container} (może już nie działać).", container); }
        try { await _docker.Containers.RemoveContainerAsync(container, new ContainerRemoveParameters { Force = true }); } catch (Exception ex) { _logger.LogDebug(ex, "Nie usunięto {Container} (może nie istnieć).", container); }
    }

    private async Task SafeRemoveContainer(string container)
    {
        try { await _docker.Containers.RemoveContainerAsync(container, new ContainerRemoveParameters { Force = true }); } catch (Exception ex) { _logger.LogDebug(ex, "Nie usunięto {Container} (może nie istnieć).", container); }
    }

    private async Task SafeStart(string container)
    {
        try { await _docker.Containers.StartContainerAsync(container, new ContainerStartParameters()); }
        catch (Exception ex) { _logger.LogWarning(ex, "Nie udało się uruchomić {Container}.", container); }
    }

    private async Task SafeRemoveImage(string image)
    {
        try { await _docker.Images.DeleteImageAsync(image, new ImageDeleteParameters()); } catch (Exception ex) { _logger.LogDebug(ex, "Nie usunięto obrazu {Image} (może być używany lub nie istnieć).", image); }
    }

    private async Task SafeTagImage(string source, string repo, string tag)
    {
        try { await _docker.Images.TagImageAsync(source, new ImageTagParameters { RepositoryName = repo, Tag = tag }); } catch (Exception ex) { _logger.LogWarning(ex, "Nie udało się otagować obrazu {Source} jako {Repo}:{Tag}.", source, repo, tag); }
    }

    private void Log(UpgradeJob job, string level, string stage, string message)
    {
        lock (job.Log)
        {
            job.Log.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Stage = stage,
                Message = Scrub(message)
            });
        }
        _logStore.Save(job);
        _logger.LogInformation("[{Stage}] {Message}", stage, Scrub(message));
    }

    private void Fail(UpgradeJob job, string stage, string message)
    {
        Log(job, "error", stage, message);
        job.Status = UpgradeStatus.Failed;
        job.Error = message;
    }

    private void SetStage(UpgradeJob job, UpgradeStage stage)
    {
        job.Stage = stage;
        _logStore.Save(job);
    }

    private UpgradeJob? SnapshotFromDisk(string id) => _logStore.Load(id);

    private static string Short(string commit) =>
        commit.Length > 7 ? commit[..7] : commit;

    private static string TrimOutput(string output) =>
        output.Length > 2000 ? output[^2000..] : output;

    private static string Cfg(string envVar, string? configValue, string fallback) =>
        Environment.GetEnvironmentVariable(envVar) ?? configValue ?? fallback;

    /// <summary>Hasła i tokeny nigdy nie trafiają do logów zadania (widać je w Portalu).</summary>
    private string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        lock (_secretsToScrub)
        {
            foreach (var secret in _secretsToScrub.Where(x => x.Length >= 6))
                text = text.Replace(secret, "***");
        }
        if (_guardianSecret.Length >= 6) text = text.Replace(_guardianSecret, "***");
        return TokenInUrl().Replace(text, "https://***@");
    }

    /// <summary>
    /// Uruchamia program z listą argumentów (bez powłoki i bez sklejania stringów),
    /// więc wartości z Portalu nie mogą dopisać własnych parametrów.
    /// </summary>
    private static async Task<(bool Ok, string Output)> Cli(
        string file, IEnumerable<string> args, string? workDir = null, int timeoutMin = 5)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            WorkingDirectory = workDir ?? "/tmp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0"; // git nie może czekać na hasło w nieskończoność

        try
        {
            using var p = Process.Start(psi)!;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMin));
            var stdoutTask = p.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = p.StandardError.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* już zakończony */ }
                return (false, $"Przekroczono limit czasu ({timeoutMin} min): {file} {string.Join(' ', psi.ArgumentList.Take(2))}");
            }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr);
            return (p.ExitCode == 0, output);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex SafeSlug();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-/]{0,99}$")]
    private static partial Regex SafeRef();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{0,252}$")]
    private static partial Regex SafeHost();
    [GeneratedRegex(@"https://[^@\s/]+@")]
    private static partial Regex TokenInUrl();

    public void Dispose()
    {
        _healthHttp.Dispose();
        _semaphore.Dispose();
    }
}
