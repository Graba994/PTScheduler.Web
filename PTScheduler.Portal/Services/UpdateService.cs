using System.Diagnostics;
using System.Text.Json;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;

namespace PTScheduler.Portal.Services;

public class UpdateService(
    IConfiguration config,
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService siteSettings,
    TenantService tenants,
    DockerService docker,
    ILogger<UpdateService> logger)
{
    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { UserAgent = { new System.Net.Http.Headers.ProductInfoHeaderValue("PTScheduler-Portal", "1.0") } },
        Timeout = TimeSpan.FromSeconds(15)
    };

    private string RepoDir => config.GetValue<string>("Portal:RepoDir") ?? "/opt/ptscheduler/repo";
    private string TenantImage => config.GetValue<string>("Portal:TenantImage") ?? "ptscheduler-web:latest";
    private string PortalContainerName => config.GetValue<string>("Portal:ContainerName") ?? "ptportal";
    private string PortalImage => config.GetValue<string>("Portal:PortalImage") ?? "ptportal:latest";

    private async Task<(string Owner, string Repo, string Branch, string? Token)> GetGitHubConfigAsync()
    {
        var s = await siteSettings.GetAllAsync(
            SiteSettingsService.Keys.GithubOwner,
            SiteSettingsService.Keys.GithubRepo,
            SiteSettingsService.Keys.GithubBranch,
            SiteSettingsService.Keys.GithubToken);

        var owner = s[SiteSettingsService.Keys.GithubOwner];
        var repo = s[SiteSettingsService.Keys.GithubRepo];
        var branch = s[SiteSettingsService.Keys.GithubBranch];
        var token = s[SiteSettingsService.Keys.GithubToken];

        if (string.IsNullOrWhiteSpace(branch))
            branch = Environment.GetEnvironmentVariable("PTS_BUILD_BRANCH") ?? "master";

        return (owner, repo, branch, string.IsNullOrWhiteSpace(token) ? null : token);
    }

    public async Task<VersionInfo> GetCurrentAsync()
    {
        var (_, _, branch, _) = await GetGitHubConfigAsync();
        return new VersionInfo
        {
            Commit = Environment.GetEnvironmentVariable("PTS_BUILD_COMMIT") ?? "unknown",
            BuildTime = Environment.GetEnvironmentVariable("PTS_BUILD_TIME") ?? "unknown",
            Branch = branch
        };
    }

    public VersionInfo GetCurrent()
    {
        return new VersionInfo
        {
            Commit = Environment.GetEnvironmentVariable("PTS_BUILD_COMMIT") ?? "unknown",
            BuildTime = Environment.GetEnvironmentVariable("PTS_BUILD_TIME") ?? "unknown",
            Branch = Environment.GetEnvironmentVariable("PTS_BUILD_BRANCH") ?? "master"
        };
    }

    public async Task<RemoteInfo?> CheckRemoteAsync()
    {
        try
        {
            var (owner, repo, branch, token) = await GetGitHubConfigAsync();
            var url = $"https://api.github.com/repos/{owner}/{repo}/commits/{branch}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("GitHub API returned {Status} for {Url}", (int)resp.StatusCode, url);
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            var root = doc.RootElement;
            return new RemoteInfo
            {
                Commit = root.GetProperty("sha").GetString() ?? "",
                Message = root.GetProperty("commit").GetProperty("message").GetString() ?? "",
                Author = root.GetProperty("commit").GetProperty("author").GetProperty("name").GetString() ?? "",
                Date = root.GetProperty("commit").GetProperty("author").GetProperty("date").GetString() ?? ""
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GitHub check failed");
            return null;
        }
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync()
    {
        try
        {
            var (owner, repo, branch, token) = await GetGitHubConfigAsync();
            var url = $"https://api.github.com/repos/{owner}/{repo}/commits/{branch}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var status = (int)resp.StatusCode;
                var hint = status switch
                {
                    401 => "Token jest nieprawidłowy lub wygasł.",
                    403 => "Token nie ma uprawnień do tego repozytorium.",
                    404 => $"Repo '{owner}/{repo}' lub branch '{branch}' nie istnieje, albo brak tokenu dla prywatnego repo.",
                    _ => $"Nieoczekiwany status HTTP."
                };
                return (false, $"HTTP {status} — {hint}");
            }

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var sha = doc.RootElement.GetProperty("sha").GetString() ?? "";
            var msg = doc.RootElement.GetProperty("commit").GetProperty("message").GetString() ?? "";
            var shortMsg = msg.Length > 60 ? msg[..60] + "…" : msg;

            return (true, $"Połączono! Najnowszy commit: {sha[..7]} — {shortMsg}");
        }
        catch (TaskCanceledException)
        {
            return (false, "Timeout — GitHub API nie odpowiedziało w 15 sekund.");
        }
        catch (Exception ex)
        {
            return (false, $"Błąd: {ex.Message}");
        }
    }

    public bool IsBehind(VersionInfo current, RemoteInfo? remote) =>
        remote is not null
        && !string.IsNullOrEmpty(remote.Commit)
        && current.Commit != "unknown"
        && !remote.Commit.StartsWith(current.Commit)
        && !current.Commit.StartsWith(remote.Commit);

    public async Task<VersionInfo> GetTenantBuildAsync()
    {
        var s = await siteSettings.GetAllAsync(
            SiteSettingsService.Keys.LastTenantBuildCommit,
            SiteSettingsService.Keys.LastTenantBuildTime);
        var (_, _, branch, _) = await GetGitHubConfigAsync();
        return new VersionInfo
        {
            Commit = s[SiteSettingsService.Keys.LastTenantBuildCommit] ?? "unknown",
            BuildTime = s[SiteSettingsService.Keys.LastTenantBuildTime] ?? "unknown",
            Branch = branch
        };
    }

    public async Task SaveTenantBuildInfoAsync(string? commit)
    {
        var c = string.IsNullOrWhiteSpace(commit) ? "unknown" : commit.Trim();
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        await siteSettings.SetAsync(SiteSettingsService.Keys.LastTenantBuildCommit, c);
        await siteSettings.SetAsync(SiteSettingsService.Keys.LastTenantBuildTime, now);
    }

    public async Task<VersionInfo?> GetLocalRepoHeadAsync()
    {
        if (!Directory.Exists(RepoDir)) return null;
        try
        {
            var (ok, sha) = await RunAsync("git", "rev-parse HEAD", RepoDir);
            if (!ok) return null;
            var (_, logOut) = await RunAsync("git", "log -1 --format=%s", RepoDir);
            var (_, dateOut) = await RunAsync("git", "log -1 --format=%ci", RepoDir);
            var (_, branchOut) = await RunAsync("git", "rev-parse --abbrev-ref HEAD", RepoDir);
            return new VersionInfo
            {
                Commit = sha.Trim(),
                BuildTime = dateOut.Trim(),
                Branch = branchOut.Trim(),
                Message = logOut.Trim()
            };
        }
        catch { return null; }
    }

    public bool IsRepoBehindRemote(VersionInfo? repoHead, RemoteInfo? remote)
    {
        if (repoHead is null || remote is null) return false;
        if (string.IsNullOrEmpty(remote.Commit) || repoHead.Commit == "unknown") return false;
        return !remote.Commit.StartsWith(repoHead.Commit)
            && !repoHead.Commit.StartsWith(remote.Commit);
    }

    public bool IsTenantBehindRepo(VersionInfo tenantBuild, VersionInfo? repoHead)
    {
        if (repoHead is null) return false;
        if (tenantBuild.Commit == "unknown") return true;
        return !repoHead.Commit.StartsWith(tenantBuild.Commit)
            && !tenantBuild.Commit.StartsWith(repoHead.Commit);
    }

    public async Task<UpgradeResult> UpgradePortalAsync()
    {
        var log = new List<string>();
        try
        {
            var (_, _, branch, _) = await GetGitHubConfigAsync();

            if (!Directory.Exists(RepoDir))
                return new UpgradeResult(false,
                    $"Katalog repo '{RepoDir}' nie zamontowany. Dodaj -v /mnt/user/appdata/ptscheduler-repo:/opt/ptscheduler/repo",
                    log);

            var (fetchOk, fetchOut) = await RunAsync("git", "fetch origin", RepoDir);
            log.Add($"git fetch: {(fetchOk ? "OK" : "FAIL")}");
            log.Add(fetchOut);
            if (!fetchOk) return new UpgradeResult(false, "git fetch nie powiódł się", log);

            var (pullOk, pullOut) = await RunAsync("git", $"pull origin {branch}", RepoDir);
            log.Add($"git pull origin {branch}: {(pullOk ? "OK" : "FAIL")}");
            log.Add(pullOut);
            if (!pullOk) return new UpgradeResult(false, "git pull nie powiódł się", log);

            var (commitOk, commitOut) = await RunAsync("git", "rev-parse HEAD", RepoDir);
            var newCommit = commitOk ? commitOut.Trim() : "unknown";
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            log.Add("→ Building portal image (ptportal:pending)...");
            var (buildOk, buildOut) = await RunAsync("docker",
                $"build --build-arg BUILD_COMMIT={newCommit} --build-arg BUILD_TIME={now} --build-arg BUILD_BRANCH={branch} -t ptportal:pending -f PTScheduler.Portal/Dockerfile .",
                RepoDir, timeoutMinutes: 20);
            log.Add(buildOut);
            if (!buildOk) return new UpgradeResult(false, "Build portalu nie powiódł się", log);
            log.Add("✓ ptportal:pending zbudowany");

            var self = await docker.InspectAsync(PortalContainerName);
            if (self is null)
                return new UpgradeResult(false,
                    $"Nie mogę zinspekcjonować kontenera '{PortalContainerName}'. Sprawdź Portal:ContainerName.", log);

            log.Add($"✓ Zinspekcjonowałem kontener {PortalContainerName}");
            log.Add("→ Uruchamiam pomocnika-restartera (5 sekund opóźnienia)...");

            // Serialize current portal config as docker run flags for the helper.
            // PTS_BUILD_* is excluded on purpose — those come baked into the new image via
            // --build-arg above, and an explicit `-e PTS_BUILD_COMMIT=<old value>` here would
            // silently override them, making the "current version" badge never advance past
            // whatever commit the portal happened to be built at originally.
            var envArgs = string.Join(" ",
                self.Config.Env
                    .Where(e => !e.StartsWith("PTS_BUILD_", StringComparison.Ordinal))
                    .Select(e => $"-e {ShellQuote(e)}"));
            var bindArgs = self.HostConfig.Binds is null ? "" :
                string.Join(" ", self.HostConfig.Binds.Select(b => $"-v {ShellQuote(b)}"));
            var netMode = self.HostConfig.NetworkMode ?? "bridge";
            var restart = self.HostConfig.RestartPolicy?.Name.ToString().ToLowerInvariant() ?? "unless-stopped";
            var portArgs = "";
            if (netMode != "host" && self.HostConfig.PortBindings is not null)
            {
                var parts = new List<string>();
                foreach (var (containerPort, bindings) in self.HostConfig.PortBindings)
                {
                    if (bindings is null) continue;
                    foreach (var b in bindings)
                    {
                        var hostPort = string.IsNullOrEmpty(b.HostPort) ? "" : b.HostPort;
                        var proto = containerPort.Split('/').Last();
                        var num = containerPort.Split('/')[0];
                        parts.Add($"-p {hostPort}:{num}/{proto}");
                    }
                }
                portArgs = string.Join(" ", parts);
            }

            var runCmd = $"docker run -d --name {PortalContainerName} --network {netMode} --restart {restart} {envArgs} {bindArgs} {portArgs} {PortalImage}";

            var script =
                "set -e\n" +
                "sleep 5\n" +
                $"docker tag {PortalImage} ptportal:previous 2>/dev/null || true\n" +
                $"docker stop {PortalContainerName} 2>/dev/null || true\n" +
                $"docker rm {PortalContainerName} 2>/dev/null || true\n" +
                $"docker tag ptportal:pending {PortalImage}\n" +
                $"{runCmd}\n" +
                "sleep 12\n" +
                $"if ! docker inspect --format='{{{{.State.Running}}}}' {PortalContainerName} 2>/dev/null | grep -q true; then\n" +
                $"  echo 'New container failed — rolling back to ptportal:previous'\n" +
                $"  docker stop {PortalContainerName} 2>/dev/null || true\n" +
                $"  docker rm {PortalContainerName} 2>/dev/null || true\n" +
                $"  docker tag ptportal:previous {PortalImage}\n" +
                $"  {runCmd}\n" +
                "fi\n" +
                "docker rmi ptportal:pending 2>/dev/null || true\n";

            log.Add("→ Sprawdzam obraz docker:cli dla pomocnika-restartera...");
            await docker.EnsureImagePulledAsync("docker:cli");

            var helperName = $"ptportal-upgrader-{DateTime.UtcNow:yyyyMMddHHmmss}";
            await docker.StartDetachedAsync(new CreateContainerParameters
            {
                Name = helperName,
                Image = "docker:cli",
                Cmd = new List<string> { "sh", "-c", script },
                HostConfig = new HostConfig
                {
                    AutoRemove = true,
                    Binds = new List<string> { "/var/run/docker.sock:/var/run/docker.sock" },
                    NetworkMode = "bridge"
                }
            });

            log.Add($"✓ Restarter '{helperName}' uruchomiony. Portal zostanie zrestartowany za 5 sekund.");
            return new UpgradeResult(true,
                "Portal zaraz się zrestartuje. Odczekaj 10-20 sekund i odśwież stronę.",
                log);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Portal upgrade failed");
            log.Add($"✖ Wyjątek: {ex.Message}");
            return new UpgradeResult(false, ex.Message, log);
        }
    }

    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    public async Task<UpgradeResult> RunUpgradeAsync(bool rebuildTenantImage, bool reprovisionTenants)
    {
        var log = new List<string>();
        var ok = true;

        try
        {
            var (_, _, branch, _) = await GetGitHubConfigAsync();

            if (!Directory.Exists(RepoDir))
            {
                return new UpgradeResult(false,
                    $"Katalog repo '{RepoDir}' nie istnieje w kontenerze. Zamontuj wolumin z hosta na /opt/ptscheduler/repo.",
                    log);
            }

            log.Add($"→ Repo: {RepoDir}");

            var (fetchOk, fetchOut) = await RunAsync("git", $"fetch origin", RepoDir);
            log.Add($"git fetch: {(fetchOk ? "OK" : "FAIL")}");
            log.Add(fetchOut);
            if (!fetchOk) return new UpgradeResult(false, "git fetch nie powiódł się", log);

            var (pullOk, pullOut) = await RunAsync("git", $"pull origin {branch}", RepoDir);
            log.Add($"git pull origin {branch}: {(pullOk ? "OK" : "FAIL")}");
            log.Add(pullOut);
            if (!pullOk) return new UpgradeResult(false, "git pull nie powiódł się", log);

            if (rebuildTenantImage)
            {
                log.Add($"→ Zachowuję poprzedni obraz jako {TenantImage.Replace(":latest", ":previous")}...");
                await RunAsync("docker", $"tag {TenantImage} {TenantImage.Replace(":latest", ":previous")}", RepoDir);

                log.Add("→ Building tenant image: ptscheduler-web:latest");
                var (buildOk, buildOut) = await RunAsync("docker",
                    $"build -t {TenantImage} .", RepoDir, timeoutMinutes: 20);
                log.Add(buildOut);
                if (!buildOk)
                {
                    ok = false;
                    log.Add("✖ Build obrazu trenera nie powiódł się — przywracam poprzedni obraz.");
                    await RunAsync("docker", $"tag {TenantImage.Replace(":latest", ":previous")} {TenantImage}", RepoDir);
                    return new UpgradeResult(false, "docker build zakończył się błędem", log);
                }
                log.Add("✓ Nowy obraz trenera zbudowany");

                var (commitOk2, builtCommit) = await RunAsync("git", "rev-parse HEAD", RepoDir);
                if (commitOk2)
                {
                    var trimmed = builtCommit.Trim();
                    var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    await siteSettings.SetAsync(SiteSettingsService.Keys.LastTenantBuildCommit, trimmed);
                    await siteSettings.SetAsync(SiteSettingsService.Keys.LastTenantBuildTime, now);
                    log.Add($"✓ Zapisano wersję: {trimmed[..7]}");
                }
            }

            if (reprovisionTenants)
            {
                await using var db = dbFactory.CreateDbContext();
                var active = await db.Tenants
                    .AsNoTracking()
                    .Where(t => t.Status == Entities.TenantStatus.Active)
                    .ToListAsync();

                log.Add($"→ Reprovisioning {active.Count} aktywnych trenerów...");
                foreach (var t in active)
                {
                    var (reOk, reMsg) = await tenants.ReprovisionWebAsync(t.Id);
                    log.Add($"  {(reOk ? "✓" : "✖")} {t.Slug}: {reMsg}");
                    if (!reOk) ok = false;
                }
            }

            return new UpgradeResult(ok,
                ok ? "Upgrade zakończony pomyślnie" : "Upgrade zakończony z błędami — sprawdź log",
                log);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upgrade failed");
            log.Add($"✖ Wyjątek: {ex.Message}");
            return new UpgradeResult(false, ex.Message, log);
        }
    }

    private static async Task<(bool Success, string Output)> RunAsync(
        string file, string args, string workingDir, int timeoutMinutes = 5)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (false, "process failed to start");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
            var stdout = await p.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);

            var output = stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n[stderr]\n" + stderr);
            return (p.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ── Guardian integration ───────────────────────────────────

    private static readonly HttpClient _guardianHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private async Task<(string Url, string Secret)> GetGuardianConfigAsync()
    {
        var s = await siteSettings.GetAllAsync(
            SiteSettingsService.Keys.GuardianUrl,
            SiteSettingsService.Keys.GuardianSecret);

        var url = s[SiteSettingsService.Keys.GuardianUrl];
        var secret = s[SiteSettingsService.Keys.GuardianSecret];

        if (string.IsNullOrWhiteSpace(url))
            url = config.GetValue<string>("Portal:GuardianUrl")
                ?? Environment.GetEnvironmentVariable("GUARDIAN_URL")
                ?? "http://ptguardian:9090";

        if (string.IsNullOrWhiteSpace(secret))
            secret = config.GetValue<string>("Portal:GuardianSecret")
                ?? Environment.GetEnvironmentVariable("GUARDIAN_SECRET")
                ?? "";

        return (url.TrimEnd('/'), secret);
    }

    private async Task<string?> CallGuardianAsync(HttpMethod method, string path)
    {
        var (url, secret) = await GetGuardianConfigAsync();
        using var req = new HttpRequestMessage(method, $"{url}{path}");
        req.Headers.Add("X-Guardian-Secret", secret);
        try
        {
            using var resp = await _guardianHttp.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Guardian call failed: {Path}", path);
            return null;
        }
    }

    public async Task<GuardianStatusDto?> GetGuardianStatusAsync()
    {
        var json = await CallGuardianAsync(HttpMethod.Get, "/api/status");
        if (json is null) return null;
        return JsonSerializer.Deserialize<GuardianStatusDto>(json, _jsonOpts);
    }

    public async Task<GuardianStartResponse?> StartPortalUpgradeViaGuardianAsync()
    {
        var json = await CallGuardianAsync(HttpMethod.Post, "/api/upgrade/portal");
        if (json is null) return null;
        return JsonSerializer.Deserialize<GuardianStartResponse>(json, _jsonOpts);
    }

    public async Task<GuardianStartResponse?> StartTenantUpgradeViaGuardianAsync(bool rebuild = true)
    {
        var json = await CallGuardianAsync(HttpMethod.Post, $"/api/upgrade/tenant?rebuild={rebuild.ToString().ToLower()}");
        if (json is null) return null;
        return JsonSerializer.Deserialize<GuardianStartResponse>(json, _jsonOpts);
    }

    public async Task<GuardianJobDto?> GetGuardianJobAsync(string jobId)
    {
        var json = await CallGuardianAsync(HttpMethod.Get, $"/api/upgrade/jobs/{jobId}");
        if (json is null) return null;
        return JsonSerializer.Deserialize<GuardianJobDto>(json, _jsonOpts);
    }

    public async Task<GuardianActiveResponse?> GetGuardianActiveJobAsync()
    {
        var json = await CallGuardianAsync(HttpMethod.Get, "/api/upgrade/active");
        if (json is null) return null;
        return JsonSerializer.Deserialize<GuardianActiveResponse>(json, _jsonOpts);
    }

    public async Task<List<GuardianJobDto>> GetGuardianHistoryAsync(int limit = 10)
    {
        var json = await CallGuardianAsync(HttpMethod.Get, $"/api/upgrade/history?limit={limit}");
        if (json is null) return [];
        return JsonSerializer.Deserialize<List<GuardianJobDto>>(json, _jsonOpts) ?? [];
    }

    public async Task<GuardianStartResponse?> RollbackPortalViaGuardianAsync()
    {
        var json = await CallGuardianAsync(HttpMethod.Post, "/api/rollback/portal");
        if (json is null) return null;
        return JsonSerializer.Deserialize<GuardianStartResponse>(json, _jsonOpts);
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

public record GuardianStartResponse(bool Started, string? JobId, string? Error);
public record GuardianActiveResponse(bool Active, GuardianJobDto? Job);

public class GuardianStatusDto
{
    public bool Healthy { get; set; }
    public string Uptime { get; set; } = "";
    public bool PortalHealthy { get; set; }
    public DateTime? PortalLastChecked { get; set; }
    public GuardianJobDto? ActiveJob { get; set; }
    public int TotalJobs { get; set; }
}

public class GuardianJobDto
{
    public string Id { get; set; } = "";
    public string Target { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CommitBefore { get; set; } = "";
    public string CommitAfter { get; set; } = "";
    public string? Error { get; set; }
    public List<GuardianLogEntryDto> Log { get; set; } = [];
}

public class GuardianLogEntryDto
{
    public DateTime Timestamp { get; set; }
    public string Stage { get; set; } = "";
    public string Message { get; set; } = "";
    public string Level { get; set; } = "info";
}

public class VersionInfo
{
    public string Commit { get; set; } = "unknown";
    public string BuildTime { get; set; } = "unknown";
    public string Branch { get; set; } = "master";
    public string Message { get; set; } = "";
    public string CommitShort => Commit.Length > 7 ? Commit[..7] : Commit;
}

public class RemoteInfo
{
    public string Commit { get; set; } = "";
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public string Date { get; set; } = "";
    public string CommitShort => Commit.Length > 7 ? Commit[..7] : Commit;
}

public record UpgradeResult(bool Success, string Summary, List<string> Log);
