using System.Diagnostics;
using System.Text.Json;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;

namespace PTScheduler.Portal.Services;

public class UpdateService(
    IConfiguration config,
    IDbContextFactory<PortalDbContext> dbFactory,
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
    private string GitHubOwner => config.GetValue<string>("Portal:GitHubOwner") ?? "graba994";
    private string GitHubRepo => config.GetValue<string>("Portal:GitHubRepo") ?? "ptscheduler.web";
    private string DefaultBranch => Environment.GetEnvironmentVariable("PTS_BUILD_BRANCH") ?? "main";
    private string TenantImage => config.GetValue<string>("Portal:TenantImage") ?? "ptscheduler-web:latest";
    private string PortalContainerName => config.GetValue<string>("Portal:ContainerName") ?? "ptportal";
    private string PortalImage => config.GetValue<string>("Portal:PortalImage") ?? "ptportal:latest";

    public VersionInfo GetCurrent() => new()
    {
        Commit = Environment.GetEnvironmentVariable("PTS_BUILD_COMMIT") ?? "unknown",
        BuildTime = Environment.GetEnvironmentVariable("PTS_BUILD_TIME") ?? "unknown",
        Branch = DefaultBranch
    };

    public async Task<RemoteInfo?> CheckRemoteAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/commits/{DefaultBranch}";
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

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

    public bool IsBehind(VersionInfo current, RemoteInfo? remote) =>
        remote is not null
        && !string.IsNullOrEmpty(remote.Commit)
        && current.Commit != "unknown"
        && !remote.Commit.StartsWith(current.Commit)
        && !current.Commit.StartsWith(remote.Commit);

    public async Task<UpgradeResult> UpgradePortalAsync()
    {
        var log = new List<string>();
        try
        {
            if (!Directory.Exists(RepoDir))
                return new UpgradeResult(false,
                    $"Katalog repo '{RepoDir}' nie zamontowany. Dodaj -v /mnt/user/appdata/ptscheduler-repo:/opt/ptscheduler/repo",
                    log);

            var (fetchOk, fetchOut) = await RunAsync("git", "fetch origin", RepoDir);
            log.Add($"git fetch: {(fetchOk ? "OK" : "FAIL")}");
            log.Add(fetchOut);
            if (!fetchOk) return new UpgradeResult(false, "git fetch nie powiódł się", log);

            var (pullOk, pullOut) = await RunAsync("git", $"pull origin {DefaultBranch}", RepoDir);
            log.Add($"git pull origin {DefaultBranch}: {(pullOk ? "OK" : "FAIL")}");
            log.Add(pullOut);
            if (!pullOk) return new UpgradeResult(false, "git pull nie powiódł się", log);

            var (commitOk, commitOut) = await RunAsync("git", "rev-parse HEAD", RepoDir);
            var newCommit = commitOk ? commitOut.Trim() : "unknown";
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            log.Add("→ Building portal image (ptportal:pending)...");
            var (buildOk, buildOut) = await RunAsync("docker",
                $"build --build-arg BUILD_COMMIT={newCommit} --build-arg BUILD_TIME={now} --build-arg BUILD_BRANCH={DefaultBranch} -t ptportal:pending -f PTScheduler.Portal/Dockerfile .",
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

            // Serialize current portal config as docker run flags for the helper
            var envArgs = string.Join(" ",
                self.Config.Env.Select(e => $"-e {ShellQuote(e)}"));
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

            var script =
                "set -e\n" +
                "sleep 5\n" +
                $"docker stop {PortalContainerName} 2>/dev/null || true\n" +
                $"docker rm {PortalContainerName} 2>/dev/null || true\n" +
                $"docker tag ptportal:pending {PortalImage}\n" +
                $"docker run -d --name {PortalContainerName} --network {netMode} --restart {restart} {envArgs} {bindArgs} {portArgs} {PortalImage}\n";

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

            var (pullOk, pullOut) = await RunAsync("git", $"pull origin {DefaultBranch}", RepoDir);
            log.Add($"git pull origin {DefaultBranch}: {(pullOk ? "OK" : "FAIL")}");
            log.Add(pullOut);
            if (!pullOk) return new UpgradeResult(false, "git pull nie powiódł się", log);

            if (rebuildTenantImage)
            {
                log.Add("→ Building tenant image: ptscheduler-web:latest");
                var (buildOk, buildOut) = await RunAsync("docker",
                    $"build -t {TenantImage} .", RepoDir, timeoutMinutes: 20);
                log.Add(buildOut);
                if (!buildOk)
                {
                    ok = false;
                    log.Add("✖ Build obrazu trenera nie powiódł się.");
                    return new UpgradeResult(false, "docker build zakończył się błędem", log);
                }
                log.Add("✓ Nowy obraz trenera zbudowany");
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
}

public class VersionInfo
{
    public string Commit { get; set; } = "unknown";
    public string BuildTime { get; set; } = "unknown";
    public string Branch { get; set; } = "main";
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
