using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace PTScheduler.Guardian.Services;

public sealed class UpgradeOrchestrator : IDisposable
{
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
    }

    public string? ActiveJobId => _activeJob?.Id;
    public TimeSpan Uptime => DateTime.UtcNow - _bootTime;

    public UpgradeJob? GetJob(string id) =>
        _activeJob?.Id == id ? SnapshotFromDisk(id) : _logStore.Load(id);

    public List<UpgradeJob> GetHistory(int limit = 20) => _logStore.GetHistory(limit);

    // ── Portal upgrade ──────────────────────────────────────────────

    public async Task<(bool Started, string JobId, string? Error)> StartPortalUpgradeAsync()
    {
        if (!await _semaphore.WaitAsync(0))
            return (false, "", "Inna aktualizacja jest w toku.");

        var job = NewJob("portal", UpgradeTarget.Portal);
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

        var (okC, commitBefore) = await Cli("git", "rev-parse HEAD", _repoDir);
        job.CommitBefore = okC ? commitBefore.Trim() : "unknown";
        Log(job, "info", "Queued", $"Aktualny commit: {Short(job.CommitBefore)}");

        // ── PULL ────────────────────────────────────────────────
        SetStage(job, UpgradeStage.Pulling);
        if (!await GitPull(job)) return;

        var (okN, commitAfter) = await Cli("git", "rev-parse HEAD", _repoDir);
        job.CommitAfter = okN ? commitAfter.Trim() : "unknown";
        Log(job, "info", "Pulling", $"Nowy commit: {Short(job.CommitAfter)}");

        // ── BUILD ───────────────────────────────────────────────
        SetStage(job, UpgradeStage.Building);
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        Log(job, "info", "Building", "Buduję obraz ptportal:pending (10-15 min)...");

        var buildArgs = $"build --build-arg BUILD_COMMIT={job.CommitAfter} " +
            $"--build-arg BUILD_TIME={now} --build-arg BUILD_BRANCH={_branch} " +
            $"-t ptportal:pending -f PTScheduler.Portal/Dockerfile .";

        var (buildOk, buildOut) = await Cli("docker", buildArgs, _repoDir, 25);
        if (!buildOk)
        {
            Log(job, "error", "Building", TrimOutput(buildOut));
            Fail(job, "Building", "Docker build portalu nie powiódł się.");
            await SafeRemoveImage("ptportal:pending");
            return;
        }
        Log(job, "success", "Building", "Obraz ptportal:pending zbudowany.");

        // ── TEST ────────────────────────────────────────────────
        SetStage(job, UpgradeStage.Testing);
        var testName = $"ptportal-test-{DateTime.UtcNow:yyyyMMddHHmmss}";
        Log(job, "info", "Testing", $"Uruchamiam kontener testowy '{testName}'...");

        try
        {
            var testConfig = CloneConfig(inspect, "ptportal:pending");
            testConfig.HostConfig.PortBindings = null;
            var testResp = await _docker.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Name = testName,
                    Image = testConfig.Image,
                    Env = testConfig.Env,
                    HostConfig = testConfig.HostConfig
                });
            await _docker.Containers.StartContainerAsync(testResp.ID, new ContainerStartParameters());
        }
        catch (Exception ex)
        {
            Fail(job, "Testing", $"Nie udało się uruchomić kontenera testowego: {ex.Message}");
            await SafeRemoveContainer(testName);
            await SafeRemoveImage("ptportal:pending");
            return;
        }

        Log(job, "info", "Testing", "Czekam 20s na stabilność kontenera...");
        await Task.Delay(20_000);

        bool testRunning;
        try
        {
            var testInspect = await _docker.Containers.InspectContainerAsync(testName);
            testRunning = testInspect.State.Running;
        }
        catch { testRunning = false; }

        if (!testRunning)
        {
            var logs = await GetContainerLogs(testName, 60);
            Log(job, "error", "Testing", $"Kontener testowy padł w ciągu 20s.\n{logs}");
            await SafeRemoveContainer(testName);
            await SafeRemoveImage("ptportal:pending");
            Fail(job, "Testing", "Nowa wersja portalu nie uruchamia się poprawnie.");
            return;
        }

        var testHealthy = await WaitForHealth(testName, _portalPort, TimeSpan.FromSeconds(40));
        await SafeStopAndRemove(testName);

        if (!testHealthy)
        {
            Log(job, "warn", "Testing", "Health check nie odpowiedział, ale kontener działa — kontynuuję.");
        }
        else
        {
            Log(job, "success", "Testing", "Test przeszedł: kontener stabilny, health OK.");
        }

        // ── SWAP ────────────────────────────────────────────────
        SetStage(job, UpgradeStage.Swapping);
        Log(job, "info", "Swapping", "Tagowanie ptportal:latest → ptportal:previous...");
        await SafeTagImage(_portalImage, "ptportal", "previous");

        Log(job, "info", "Swapping", $"Zatrzymuję kontener '{_portalContainer}'...");
        await SafeStopAndRemove(_portalContainer);

        Log(job, "info", "Swapping", "Tagowanie ptportal:pending → ptportal:latest...");
        await SafeTagImage("ptportal:pending", "ptportal", "latest");

        Log(job, "info", "Swapping", "Uruchamiam nowy kontener portalu...");
        try
        {
            var newConfig = CloneConfig(inspect, _portalImage);
            var resp = await _docker.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Name = _portalContainer,
                    Image = newConfig.Image,
                    Env = newConfig.Env,
                    ExposedPorts = newConfig.ExposedPorts,
                    HostConfig = newConfig.HostConfig
                });
            await _docker.Containers.StartContainerAsync(resp.ID, new ContainerStartParameters());
        }
        catch (Exception ex)
        {
            Log(job, "error", "Swapping", $"Start nowego kontenera nie powiódł się: {ex.Message}");
            await PerformRollback(job, inspect);
            return;
        }
        Log(job, "success", "Swapping", "Nowy kontener uruchomiony.");

        // ── VERIFY ──────────────────────────────────────────────
        SetStage(job, UpgradeStage.Verifying);
        Log(job, "info", "Verifying", "Weryfikuję health nowego portalu (max 90s)...");

        var verified = await WaitForHealth(_portalContainer, _portalPort, TimeSpan.FromSeconds(90));
        if (!verified)
        {
            var logs = await GetContainerLogs(_portalContainer, 50);
            Log(job, "error", "Verifying", $"Portal nie odpowiada po 90s.\n{logs}");
            await PerformRollback(job, inspect);
            return;
        }

        // ── DONE ────────────────────────────────────────────────
        await SafeRemoveImage("ptportal:pending");
        job.Stage = UpgradeStage.Done;
        job.Status = UpgradeStatus.Success;
        Log(job, "success", "Done",
            $"Aktualizacja portalu zakończona pomyślnie. {Short(job.CommitBefore)} → {Short(job.CommitAfter)}");
    }

    // ── Tenant upgrade ──────────────────────────────────────────────

    public async Task<(bool Started, string JobId, string? Error)> StartTenantUpgradeAsync(bool rebuildImage = true)
    {
        if (!await _semaphore.WaitAsync(0))
            return (false, "", "Inna aktualizacja jest w toku.");

        var job = NewJob("tenant", UpgradeTarget.Tenant);
        job.RebuildImage = rebuildImage;
        _ = RunInBackground(job, ExecuteTenantUpgradeAsync);
        return (true, job.Id, null);
    }

    private async Task ExecuteTenantUpgradeAsync(UpgradeJob job)
    {
        Log(job, "info", "Queued", "Sprawdzam warunki wstępne...");
        if (!Directory.Exists(_repoDir))
        {
            Fail(job, "Queued", $"Katalog repo '{_repoDir}' nie istnieje.");
            return;
        }

        var (okC, cBefore) = await Cli("git", "rev-parse HEAD", _repoDir);
        job.CommitBefore = okC ? cBefore.Trim() : "unknown";

        SetStage(job, UpgradeStage.Pulling);
        if (!await GitPull(job)) return;

        var (okN, cAfter) = await Cli("git", "rev-parse HEAD", _repoDir);
        job.CommitAfter = okN ? cAfter.Trim() : "unknown";
        Log(job, "info", "Pulling", $"Commit: {Short(job.CommitBefore)} → {Short(job.CommitAfter)}");

        if (!job.RebuildImage)
        {
            job.Stage = UpgradeStage.Done;
            job.Status = UpgradeStatus.Success;
            Log(job, "success", "Done", "Pull zakończony (rebuild obrazu pominięty).");
            return;
        }

        SetStage(job, UpgradeStage.Building);
        Log(job, "info", "Building", "Tagowanie obecnego obrazu trenera jako :previous...");
        var prevTag = _tenantImage.Replace(":latest", ":previous");
        await SafeTagImage(_tenantImage,
            _tenantImage.Split(':')[0],
            "previous");

        Log(job, "info", "Building", "Buduję nowy obraz trenera (10-15 min)...");
        var (buildOk, buildOut) = await Cli("docker", $"build -t {_tenantImage} .", _repoDir, 25);
        if (!buildOk)
        {
            Log(job, "error", "Building", TrimOutput(buildOut));
            Log(job, "warn", "Building", "Przywracam poprzedni obraz...");
            await SafeTagImage(prevTag, _tenantImage.Split(':')[0], "latest");
            Fail(job, "Building", "Docker build trenera nie powiódł się.");
            return;
        }

        Log(job, "success", "Building", "Obraz trenera zbudowany.");
        job.Stage = UpgradeStage.Done;
        job.Status = UpgradeStatus.Success;
        Log(job, "success", "Done",
            $"Obraz trenera zaktualizowany ({Short(job.CommitAfter)}). Przeprowadź reprovisioning z portalu.");
    }

    // ── Portal rollback ─────────────────────────────────────────────

    public async Task<(bool Started, string JobId, string? Error)> RollbackPortalAsync()
    {
        if (!await _semaphore.WaitAsync(0))
            return (false, "", "Inna operacja jest w toku.");

        var job = NewJob("rollback", UpgradeTarget.Portal);
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
            Fail(job, "Swapping", "Brak obrazu ptportal:previous. Rollback niemożliwy.");
            return;
        }

        ContainerInspectResponse? inspect = null;
        try { inspect = await _docker.Containers.InspectContainerAsync(_portalContainer); }
        catch { }

        Log(job, "info", "Swapping", "Zatrzymuję aktualny kontener...");
        await SafeStopAndRemove(_portalContainer);

        Log(job, "info", "Swapping", "Tagowanie ptportal:previous → ptportal:latest...");
        await SafeTagImage("ptportal:previous", "ptportal", "latest");

        Log(job, "info", "Swapping", "Uruchamiam portal z poprzedniej wersji...");
        if (inspect is not null)
        {
            try
            {
                var cfg = CloneConfig(inspect, _portalImage);
                var resp = await _docker.Containers.CreateContainerAsync(
                    new CreateContainerParameters
                    {
                        Name = _portalContainer,
                        Image = cfg.Image,
                        Env = cfg.Env,
                        ExposedPorts = cfg.ExposedPorts,
                        HostConfig = cfg.HostConfig
                    });
                await _docker.Containers.StartContainerAsync(resp.ID, new ContainerStartParameters());
            }
            catch (Exception ex)
            {
                Fail(job, "Swapping", $"Nie udało się uruchomić kontenera: {ex.Message}");
                return;
            }
        }
        else
        {
            Fail(job, "Swapping", "Brak konfiguracji kontenera — uruchom portal ręcznie.");
            return;
        }

        SetStage(job, UpgradeStage.Verifying);
        var healthy = await WaitForHealth(_portalContainer, _portalPort, TimeSpan.FromSeconds(60));
        job.Stage = UpgradeStage.Done;

        if (healthy)
        {
            job.Status = UpgradeStatus.RolledBack;
            Log(job, "success", "Done", "Rollback zakończony — portal działa na poprzedniej wersji.");
        }
        else
        {
            job.Status = UpgradeStatus.Failed;
            Log(job, "error", "Done", "Kontener uruchomiony, ale health check nie odpowiada.");
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

    private UpgradeJob NewJob(string suffix, UpgradeTarget target)
    {
        var job = new UpgradeJob
        {
            Id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}",
            Target = target,
            Stage = UpgradeStage.Queued,
            Status = UpgradeStatus.Running,
            StartedAt = DateTime.UtcNow
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
                _logStore.Save(job);
                _activeJob = null;
                _semaphore.Release();
            }
        });
    }

    private async Task<bool> GitPull(UpgradeJob job)
    {
        Log(job, "info", "Pulling", "git fetch origin...");
        var (fetchOk, fetchOut) = await Cli("git", "fetch origin", _repoDir);
        if (!fetchOk)
        {
            Fail(job, "Pulling", $"git fetch failed: {fetchOut}");
            return false;
        }
        Log(job, "success", "Pulling", "git fetch OK");

        Log(job, "info", "Pulling", $"git pull origin {_branch}...");
        var (pullOk, pullOut) = await Cli("git", $"pull origin {_branch}", _repoDir);
        if (!pullOk)
        {
            Fail(job, "Pulling", $"git pull failed: {pullOut}");
            return false;
        }
        Log(job, "success", "Pulling", "git pull OK");
        return true;
    }

    private async Task PerformRollback(UpgradeJob job, ContainerInspectResponse originalInspect)
    {
        Log(job, "warn", "Verifying", "Rozpoczynam rollback...");
        await SafeStopAndRemove(_portalContainer);
        await SafeTagImage("ptportal:previous", "ptportal", "latest");

        try
        {
            var cfg = CloneConfig(originalInspect, _portalImage);
            var resp = await _docker.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Name = _portalContainer,
                    Image = cfg.Image,
                    Env = cfg.Env,
                    ExposedPorts = cfg.ExposedPorts,
                    HostConfig = cfg.HostConfig
                });
            await _docker.Containers.StartContainerAsync(resp.ID, new ContainerStartParameters());
            var ok = await WaitForHealth(_portalContainer, _portalPort, TimeSpan.FromSeconds(60));

            if (ok)
            {
                job.Status = UpgradeStatus.RolledBack;
                Log(job, "warn", "Verifying", "Rollback OK — portal przywrócony.");
            }
            else
            {
                job.Status = UpgradeStatus.Failed;
                Log(job, "error", "Verifying", "Rollback: kontener działa, ale health check nie przechodzi.");
            }
        }
        catch (Exception ex)
        {
            job.Status = UpgradeStatus.Failed;
            Log(job, "error", "Verifying", $"Rollback nie powiódł się: {ex.Message}");
        }
    }

    private record ClonedConfig(string Image, IList<string> Env,
        IDictionary<string, EmptyStruct>? ExposedPorts, HostConfig HostConfig);

    private ClonedConfig CloneConfig(ContainerInspectResponse src, string newImage)
    {
        var env = (src.Config.Env ?? [])
            .Where(e => !e.StartsWith("PTS_BUILD_", StringComparison.Ordinal))
            .ToList();

        return new ClonedConfig(
            newImage,
            env,
            src.Config.ExposedPorts,
            new HostConfig
            {
                Binds = src.HostConfig.Binds,
                NetworkMode = src.HostConfig.NetworkMode ?? "bridge",
                RestartPolicy = src.HostConfig.RestartPolicy
                    ?? new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                PortBindings = src.HostConfig.PortBindings
            });
    }

    private async Task<bool> WaitForHealth(string container, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await _healthHttp.GetAsync($"http://{container}:{port}/health");
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }
            await Task.Delay(3_000);
        }
        return false;
    }

    private async Task<string> GetContainerLogs(string container, int tail)
    {
        try
        {
            var mux = await _docker.Containers.GetContainerLogsAsync(container,
                false, new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Tail = tail.ToString()
                });
            var buffer = new byte[81920];
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                var result = await mux.ReadOutputAsync(buffer, 0, buffer.Length, default);
                if (result.Count == 0) break;
                sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            return sb.ToString();
        }
        catch { return "(nie udało się pobrać logów)"; }
    }



    private async Task SafeStopAndRemove(string container)
    {
        try { await _docker.Containers.StopContainerAsync(container, new ContainerStopParameters { WaitBeforeKillSeconds = 10 }); } catch { }
        try { await _docker.Containers.RemoveContainerAsync(container, new ContainerRemoveParameters { Force = true }); } catch { }
    }

    private async Task SafeRemoveContainer(string container)
    {
        try { await _docker.Containers.RemoveContainerAsync(container, new ContainerRemoveParameters { Force = true }); } catch { }
    }

    private async Task SafeRemoveImage(string image)
    {
        try { await _docker.Images.DeleteImageAsync(image, new ImageDeleteParameters()); } catch { }
    }

    private async Task SafeTagImage(string source, string repo, string tag)
    {
        try { await _docker.Images.TagImageAsync(source, new ImageTagParameters { RepositoryName = repo, Tag = tag }); } catch { }
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
                Message = message
            });
        }
        _logStore.Save(job);
        _logger.LogInformation("[{Stage}] {Message}", stage, message);
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

    private static async Task<(bool Ok, string Output)> Cli(
        string file, string args, string? workDir = null, int timeoutMin = 5)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = workDir ?? "/tmp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        try
        {
            using var p = Process.Start(psi)!;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMin));
            var stdout = await p.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            var output = stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr);
            return (p.ExitCode == 0, output);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public void Dispose()
    {
        _healthHttp.Dispose();
        _semaphore.Dispose();
    }
}
