using System.Text;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace PTScheduler.Guardian.Services;

/// <summary>
/// „Doktor” Guardiana: przegląda kontenery platformy, wykrywa typowe awarie (Portal nie widzi bazy,
/// zatrzymane kontenery, pętle restartów, pozostałości po aktualizacjach) i proponuje naprawę.
/// Naprawy są z zamkniętej listy i dotyczą tylko kontenerów platformy — klient podaje wyłącznie
/// identyfikator problemu, a Guardian sam ustala, co zrobić, na świeżej diagnozie.
/// Bezpieczne naprawy (np. Portal nie widzi bazy) może wykonać sam, gdy Portal nie odpowiada.
/// </summary>
public sealed partial class DoctorService(
    DockerClient docker,
    UpgradeOrchestrator orchestrator,
    HealthWatcher health,
    LogStore logStore,
    IConfiguration config,
    ILogger<DoctorService> logger) : BackgroundService
{
    private static readonly string[] SystemNetworks = ["bridge", "host", "none", "default"];
    private readonly SemaphoreSlim _fixLock = new(1, 1);
    private readonly Dictionary<string, DateTime> _lastAutoFix = [];

    public bool AutoHealEnabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable("GUARDIAN_AUTO_HEAL") ?? config["Guardian:AutoHeal"], "false", StringComparison.OrdinalIgnoreCase);

    private sealed record Plan(DoctorFinding Finding, Func<Action<string>, Task>? Fix);

    // ── Diagnoza ─────────────────────────────────────────────────────

    public async Task<DoctorReport> DiagnoseAsync(CancellationToken ct = default) =>
        new() { Findings = (await PlanAsync(ct)).Select(p => p.Finding).ToList(), AutoHealEnabled = AutoHealEnabled };

    private async Task<List<Plan>> PlanAsync(CancellationToken ct)
    {
        var plans = new List<Plan>();
        IList<ContainerListResponse> all;
        try { all = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct); }
        catch (Exception ex)
        {
            plans.Add(new(new DoctorFinding { Id = "docker", Severity = "danger", Title = "Guardian nie widzi Dockera", Detail = ex.Message }, null));
            return plans;
        }

        string NameOf(ContainerListResponse c) => c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID[..12];
        var byName = all.ToDictionary(NameOf, c => c, StringComparer.OrdinalIgnoreCase);
        var portalName = orchestrator.PortalContainer;

        await CheckPortalAsync(plans, all, byName, portalName, ct);
        await CheckGuardianNetworkAsync(plans, all, byName, portalName, ct);
        await CheckTenantsAsync(plans, all, byName, ct);
        CheckLeftovers(plans, all, byName, portalName);
        await CheckImagesAsync(plans, ct);

        return plans
            .OrderBy(p => p.Finding.Severity switch { "danger" => 0, "warn" => 1, _ => 2 })
            .ToList();
    }

    private async Task CheckPortalAsync(List<Plan> plans, IList<ContainerListResponse> all,
        Dictionary<string, ContainerListResponse> byName, string portalName, CancellationToken ct)
    {
        if (!byName.TryGetValue(portalName, out var portal))
        {
            var backup = all.Where(c => Regex.IsMatch(c.Names.First().TrimStart('/'), $"^{Regex.Escape(portalName)}-prev-\\d+$"))
                .OrderByDescending(c => c.Created).FirstOrDefault();
            if (backup is not null)
            {
                var backupName = backup.Names.First().TrimStart('/');
                plans.Add(new(new DoctorFinding
                {
                    Id = "portal-missing-restore",
                    Severity = "danger",
                    Title = "Nie ma kontenera Portalu",
                    Detail = $"Jest odłożona poprzednia wersja '{backupName}' (z przerwanej aktualizacji).",
                    FixLabel = "Przywróć poprzedni Portal",
                    FixDescription = $"Zmieni nazwę '{backupName}' na '{portalName}' i uruchomi go.",
                    AutoFix = true
                }, async log =>
                {
                    await docker.Containers.RenameContainerAsync(backup.ID, new ContainerRenameParameters { NewName = portalName }, CancellationToken.None);
                    await docker.Containers.StartContainerAsync(backup.ID, new ContainerStartParameters());
                    log($"Przywrócono '{backupName}' jako '{portalName}' i uruchomiono.");
                }));
            }
            else
            {
                plans.Add(new(new DoctorFinding
                {
                    Id = "portal-missing",
                    Severity = "danger",
                    Title = "Nie ma kontenera Portalu",
                    Detail = $"Nie znalazłem kontenera '{portalName}' ani jego kopii. Uruchom Portal z szablonu Unraid albo docker compose."
                }, null));
            }
            return;
        }

        ContainerInspectResponse inspect;
        try { inspect = await docker.Containers.InspectContainerAsync(portal.ID, ct); }
        catch { return; }

        // Baza Portalu z connection stringa — czy działa i czy Portal ją widzi.
        var env = inspect.Config.Env ?? [];
        var cs = env.FirstOrDefault(e => e.StartsWith("ConnectionStrings__DefaultConnection=", StringComparison.Ordinal));
        var dbHost = cs is null ? null : HostFrom(cs[(cs.IndexOf('=') + 1)..]);
        var dbProblem = false;

        if (dbHost is not null && byName.TryGetValue(dbHost, out var db))
        {
            var dbInspect = await docker.Containers.InspectContainerAsync(db.ID, ct);
            if (!dbInspect.State.Running)
            {
                dbProblem = true;
                plans.Add(new(new DoctorFinding
                {
                    Id = $"db-stopped:{dbHost}",
                    Severity = "danger",
                    Title = "Baza Portalu jest zatrzymana",
                    Detail = $"Kontener '{dbHost}' nie działa (stan: {dbInspect.State.Status}). Bez niego Portal nie wystartuje.",
                    FixLabel = "Uruchom bazę",
                    FixDescription = $"Uruchomi '{dbHost}', a potem zrestartuje Portal.",
                    AutoFix = true
                }, async log =>
                {
                    await docker.Containers.StartContainerAsync(db.ID, new ContainerStartParameters());
                    log($"Uruchomiono '{dbHost}'.");
                    await Task.Delay(5_000);
                    await docker.Containers.RestartContainerAsync(portal.ID, new ContainerRestartParameters { WaitBeforeKillSeconds = 15 });
                    log("Zrestartowano Portal.");
                }));
            }
            else
            {
                var portalNets = UserNetworks(inspect);
                var dbNets = UserNetworks(dbInspect);
                var shared = portalNets.Intersect(dbNets).ToList();
                var primary = inspect.HostConfig.NetworkMode ?? "";
                var targetNet = portalNets.Contains(primary) ? primary : portalNets.FirstOrDefault();

                if (shared.Count == 0 && targetNet is not null)
                {
                    dbProblem = true;
                    plans.Add(new(new DoctorFinding
                    {
                        Id = $"db-network:{dbHost}:{targetNet}",
                        Severity = "danger",
                        Title = "Portal i baza są w różnych sieciach",
                        Detail = $"Portal łączy się z '{dbHost}', ale nie mają wspólnej sieci Dockera (Portal: {string.Join(", ", portalNets)}; baza: {string.Join(", ", dbNets.DefaultIfEmpty("tylko bridge"))}).",
                        FixLabel = "Połącz bazę z siecią Portalu",
                        FixDescription = $"Podłączy '{dbHost}' do sieci '{targetNet}' pod nazwą '{dbHost}' i zrestartuje Portal.",
                        AutoFix = true
                    }, async log =>
                    {
                        await docker.Networks.ConnectNetworkAsync(targetNet, new NetworkConnectParameters
                        {
                            Container = db.ID,
                            EndpointConfig = new EndpointSettings { Aliases = [dbHost] }
                        });
                        log($"Podłączono '{dbHost}' do sieci '{targetNet}'.");
                        await docker.Containers.RestartContainerAsync(portal.ID, new ContainerRestartParameters { WaitBeforeKillSeconds = 15 });
                        log("Zrestartowano Portal.");
                    }));
                }
                else if (shared.Count > 0 && inspect.State.Running && !await ResolvesAsync(portal.ID, dbHost, ct))
                {
                    var net = shared.Contains(primary) ? primary : shared[0];
                    dbProblem = true;
                    plans.Add(new(new DoctorFinding
                    {
                        Id = $"db-dns:{dbHost}:{net}",
                        Severity = "danger",
                        Title = "Portal nie rozpoznaje nazwy bazy",
                        Detail = $"Portal i '{dbHost}' są w sieci '{net}', ale nazwa '{dbHost}' nie rozwiązuje się wewnątrz Portalu (DNS Dockera). To typowe po odtworzeniu kontenera.",
                        FixLabel = "Podłącz bazę ponownie pod jej nazwą",
                        FixDescription = $"Odłączy i podłączy '{dbHost}' do sieci '{net}' z aliasem '{dbHost}', potem zrestartuje Portal.",
                        AutoFix = true
                    }, async log =>
                    {
                        try { await docker.Networks.DisconnectNetworkAsync(net, new NetworkDisconnectParameters { Container = db.ID, Force = true }); }
                        catch (Exception ex) { log($"Odłączenie: {ex.Message}"); }
                        await docker.Networks.ConnectNetworkAsync(net, new NetworkConnectParameters
                        {
                            Container = db.ID,
                            EndpointConfig = new EndpointSettings { Aliases = [dbHost] }
                        });
                        log($"Podłączono '{dbHost}' do '{net}' z aliasem '{dbHost}'.");
                        await docker.Containers.RestartContainerAsync(portal.ID, new ContainerRestartParameters { WaitBeforeKillSeconds = 15 });
                        log("Zrestartowano Portal.");
                    }));
                }
            }
        }

        if (!inspect.State.Running && !dbProblem)
        {
            var logs = await TailAsync(portal.ID, 12, ct);
            plans.Add(new(new DoctorFinding
            {
                Id = "portal-stopped",
                Severity = "danger",
                Title = "Portal jest zatrzymany",
                Detail = $"Stan: {inspect.State.Status}, kod wyjścia {inspect.State.ExitCode}. Ostatnie logi:\n{logs}",
                FixLabel = "Uruchom Portal",
                FixDescription = $"Uruchomi kontener '{portalName}'.",
                AutoFix = inspect.State.ExitCode != 0
            }, async log =>
            {
                await docker.Containers.StartContainerAsync(portal.ID, new ContainerStartParameters());
                log("Uruchomiono Portal.");
            }));
        }
        else if (inspect.State.Running && inspect.RestartCount > 2 && !dbProblem)
        {
            var logs = await TailAsync(portal.ID, 15, ct);
            plans.Add(new(new DoctorFinding
            {
                Id = "portal-restarting",
                Severity = "danger",
                Title = $"Portal restartuje się ({inspect.RestartCount}×)",
                Detail = $"Aplikacja pada zaraz po starcie. Ostatnie logi:\n{logs}"
            }, null));
        }
    }

    private async Task CheckGuardianNetworkAsync(List<Plan> plans, IList<ContainerListResponse> all,
        Dictionary<string, ContainerListResponse> byName, string portalName, CancellationToken ct)
    {
        var self = all.FirstOrDefault(c => c.ID.StartsWith(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
        if (self is null || !byName.TryGetValue(portalName, out var portal)) return;
        var me = await docker.Containers.InspectContainerAsync(self.ID, ct);
        var p = await docker.Containers.InspectContainerAsync(portal.ID, ct);
        var portalNets = UserNetworks(p);
        if (portalNets.Count == 0 || UserNetworks(me).Intersect(portalNets).Any()) return;
        var net = portalNets.Contains(p.HostConfig.NetworkMode ?? "") ? p.HostConfig.NetworkMode! : portalNets[0];
        plans.Add(new(new DoctorFinding
        {
            Id = $"guardian-network:{net}",
            Severity = "warn",
            Title = "Guardian nie jest w sieci Portalu",
            Detail = "Guardian nie może sprawdzać Portalu ani pobierać od niego dostępu do GitHuba.",
            FixLabel = "Dołącz Guardiana do sieci Portalu",
            FixDescription = $"Podłączy kontener Guardiana do sieci '{net}' (bez restartu).",
            AutoFix = true
        }, async log =>
        {
            await docker.Networks.ConnectNetworkAsync(net, new NetworkConnectParameters { Container = self.ID });
            log($"Guardian dołączony do sieci '{net}'.");
        }));
    }

    private async Task CheckTenantsAsync(List<Plan> plans, IList<ContainerListResponse> all,
        Dictionary<string, ContainerListResponse> byName, CancellationToken ct)
    {
        foreach (var web in all.Where(c => TenantWeb().IsMatch(c.Names.First().TrimStart('/'))))
        {
            var name = web.Names.First().TrimStart('/');
            var slug = TenantWeb().Match(name).Groups[1].Value;
            var dbName = $"pt-{slug}-db";

            if (byName.TryGetValue(dbName, out var db) && db.State != "running")
            {
                plans.Add(new(new DoctorFinding
                {
                    Id = $"tenant-db-stopped:{slug}",
                    Severity = "warn",
                    Title = $"Baza trenera „{slug}” jest zatrzymana",
                    Detail = $"'{dbName}' nie działa, więc aplikacja trenera nie ma danych. Jeśli trener jest zawieszony w Portalu, to może być celowe.",
                    FixLabel = "Uruchom bazę i aplikację",
                    FixDescription = $"Uruchomi '{dbName}', a potem zrestartuje '{name}'."
                }, async log =>
                {
                    await docker.Containers.StartContainerAsync(db.ID, new ContainerStartParameters());
                    await Task.Delay(3_000);
                    await docker.Containers.RestartContainerAsync(web.ID, new ContainerRestartParameters { WaitBeforeKillSeconds = 15 });
                    log($"Uruchomiono '{dbName}' i zrestartowano '{name}'.");
                }));
                continue;
            }

            if (web.State != "running")
            {
                plans.Add(new(new DoctorFinding
                {
                    Id = $"tenant-stopped:{slug}",
                    Severity = "info",
                    Title = $"Aplikacja trenera „{slug}” jest zatrzymana",
                    Detail = "Jeśli trener jest zawieszony w Portalu, to celowe. Jeśli nie — uruchom ją.",
                    FixLabel = "Uruchom",
                    FixDescription = $"Uruchomi '{name}'."
                }, async log =>
                {
                    await docker.Containers.StartContainerAsync(web.ID, new ContainerStartParameters());
                    log($"Uruchomiono '{name}'.");
                }));
                continue;
            }

            var wi = await docker.Containers.InspectContainerAsync(web.ID, ct);
            if (wi.RestartCount > 3)
            {
                plans.Add(new(new DoctorFinding
                {
                    Id = $"tenant-restarting:{slug}",
                    Severity = "warn",
                    Title = $"Aplikacja trenera „{slug}” restartuje się ({wi.RestartCount}×)",
                    Detail = $"Ostatnie logi:\n{await TailAsync(web.ID, 12, ct)}"
                }, null));
            }

            if (db is not null)
            {
                var di = await docker.Containers.InspectContainerAsync(db.ID, ct);
                var dbNets = UserNetworks(di);
                if (dbNets.Count > 0 && !UserNetworks(wi).Intersect(dbNets).Any())
                {
                    var net = dbNets[0];
                    plans.Add(new(new DoctorFinding
                    {
                        Id = $"tenant-network:{slug}:{net}",
                        Severity = "danger",
                        Title = $"Aplikacja trenera „{slug}” nie widzi swojej bazy",
                        Detail = $"'{name}' i '{dbName}' nie mają wspólnej sieci.",
                        FixLabel = "Połącz z siecią bazy",
                        FixDescription = $"Podłączy '{name}' do sieci '{net}' i zrestartuje aplikację.",
                        AutoFix = true
                    }, async log =>
                    {
                        await docker.Networks.ConnectNetworkAsync(net, new NetworkConnectParameters { Container = web.ID });
                        await docker.Containers.RestartContainerAsync(web.ID, new ContainerRestartParameters { WaitBeforeKillSeconds = 15 });
                        log($"Podłączono '{name}' do '{net}' i zrestartowano.");
                    }));
                }
            }
        }
    }

    private void CheckLeftovers(List<Plan> plans, IList<ContainerListResponse> all,
        Dictionary<string, ContainerListResponse> byName, string portalName)
    {
        // Odłożone kopie po aktualizacjach i kontenery próbne — tylko zatrzymane i starsze niż godzina,
        // a kopie Portalu tylko wtedy, gdy Portal działa (inaczej to jedyna droga powrotu).
        var portalRunning = byName.TryGetValue(portalName, out var portal) && portal.State == "running";
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var leftovers = all.Where(c =>
        {
            var n = c.Names.First().TrimStart('/');
            if (c.State == "running" || c.Created > cutoff) return false;
            if (n.StartsWith("ptportal-test-", StringComparison.Ordinal)) return true;
            if (Regex.IsMatch(n, $"^{Regex.Escape(portalName)}-prev-\\d+$")) return portalRunning;
            return TenantBackup().IsMatch(n);
        }).ToList();
        if (leftovers.Count == 0) return;

        var names = leftovers.Select(c => c.Names.First().TrimStart('/')).ToList();
        plans.Add(new(new DoctorFinding
        {
            Id = "leftovers",
            Severity = "info",
            Title = $"Pozostałości po aktualizacjach: {leftovers.Count}",
            Detail = string.Join(", ", names.Take(8)) + (names.Count > 8 ? "…" : ""),
            FixLabel = "Usuń pozostałości",
            FixDescription = "Usunie zatrzymane kopie zapasowe kontenerów i kontenery próbne (starsze niż godzina)."
        }, async log =>
        {
            foreach (var c in leftovers)
            {
                await docker.Containers.RemoveContainerAsync(c.ID, new ContainerRemoveParameters { Force = true });
                log($"Usunięto {c.Names.First().TrimStart('/')}.");
            }
        }));
    }

    private async Task CheckImagesAsync(List<Plan> plans, CancellationToken ct)
    {
        try
        {
            var dangling = await docker.Images.ListImagesAsync(new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>> { ["dangling"] = new Dictionary<string, bool> { ["true"] = true } }
            }, ct);
            var bytes = dangling.Sum(i => i.Size);
            if (dangling.Count == 0 || bytes < 500L * 1024 * 1024) return;
            plans.Add(new(new DoctorFinding
            {
                Id = "dangling-images",
                Severity = "info",
                Title = $"Stare obrazy zajmują {bytes / 1024 / 1024 / 1024.0:0.0} GB",
                Detail = $"{dangling.Count} obrazów bez nazwy (po poprzednich budowach).",
                FixLabel = "Zwolnij miejsce",
                FixDescription = "Usunie obrazy bez nazwy, których nie używa żaden kontener."
            }, async log =>
            {
                var r = await docker.Images.PruneImagesAsync(new ImagesPruneParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>> { ["dangling"] = new Dictionary<string, bool> { ["true"] = true } }
                });
                log($"Zwolniono {r.SpaceReclaimed / 1024 / 1024} MB.");
            }));
        }
        catch (Exception ex) { logger.LogDebug(ex, "Nie sprawdzono obrazów."); }
    }

    // ── Naprawa ──────────────────────────────────────────────────────

    /// <summary>Wykonuje naprawę problemu o podanym id (na świeżej diagnozie) i zapisuje ją w historii.</summary>
    public async Task<(bool Ok, string Message, string? JobId)> FixAsync(string id, string requestedBy, bool automatic = false)
    {
        if (orchestrator.ActiveJobId is not null)
            return (false, "Trwa aktualizacja — naprawy poczekają na jej koniec.", null);
        if (!await _fixLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return (false, "Trwa inna naprawa.", null);
        try
        {
            var plan = (await PlanAsync(CancellationToken.None)).FirstOrDefault(p => p.Finding.Id == id);
            if (plan is null) return (false, "Tego problemu już nie ma — odśwież diagnozę.", null);
            if (plan.Fix is null) return (false, "Ten problem wymaga ręcznej naprawy — szczegóły w opisie.", null);

            var job = new UpgradeJob
            {
                Id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-fix",
                Target = UpgradeTarget.Maintenance,
                Stage = UpgradeStage.Swapping,
                Status = UpgradeStatus.Running,
                StartedAt = DateTime.UtcNow,
                RequestedBy = automatic ? "automatyczna naprawa" : requestedBy
            };
            void Log(string level, string message)
            {
                lock (job.Log) job.Log.Add(new LogEntry { Level = level, Stage = "Swapping", Message = message });
            }
            Log("info", $"{(automatic ? "Automatyczna naprawa" : "Naprawa")}: {plan.Finding.Title}. {plan.Finding.FixDescription}");
            try
            {
                await plan.Fix(m => Log("success", m));
                job.Status = UpgradeStatus.Success;
                Log("success", "Naprawa wykonana.");
            }
            catch (Exception ex)
            {
                job.Status = UpgradeStatus.Failed;
                job.Error = ex.Message;
                Log("error", $"Naprawa nie powiodła się: {ex.Message}");
            }
            job.Stage = UpgradeStage.Done;
            job.CompletedAt = DateTime.UtcNow;
            logStore.Save(job);
            logger.LogWarning("Doktor: {Title} — {Status} ({By}).", plan.Finding.Title, job.Status, job.RequestedBy);
            return (job.Status == UpgradeStatus.Success,
                job.Status == UpgradeStatus.Success ? "Naprawione." : $"Nie udało się: {job.Error}", job.Id);
        }
        finally { _fixLock.Release(); }
    }

    /// <summary>
    /// Automatyczna naprawa: gdy Portal nie odpowiada od kilku minut i nie trwa aktualizacja,
    /// Guardian stosuje bezpieczne naprawy (AutoFix) — każdą najwyżej raz na 30 minut.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (AutoHealEnabled && !health.PortalHealthy && health.DownSinceUtc is { } since
                    && DateTime.UtcNow - since > TimeSpan.FromMinutes(3) && orchestrator.ActiveJobId is null)
                {
                    foreach (var f in (await DiagnoseAsync(ct)).Findings.Where(f => f.AutoFix && f.FixLabel is not null))
                    {
                        if (_lastAutoFix.TryGetValue(f.Id, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(30)) continue;
                        _lastAutoFix[f.Id] = DateTime.UtcNow;
                        await FixAsync(f.Id, "guardian", automatic: true);
                        break; // po jednej naprawie dajemy Portalowi czas na start
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Doktor: automatyczna diagnoza nie powiodła się."); }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }

    // ── Pomocnicze ───────────────────────────────────────────────────

    private static List<string> UserNetworks(ContainerInspectResponse c) =>
        (c.NetworkSettings?.Networks?.Keys ?? Enumerable.Empty<string>())
            .Where(n => !SystemNetworks.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>Host z connection stringa Npgsql („Host=...;”); null, gdy to adres IP albo brak.</summary>
    internal static string? HostFrom(string connectionString)
    {
        var m = Regex.Match(connectionString, @"(?:^|;)\s*(?:Host|Server)\s*=\s*([^;,]+)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var host = m.Groups[1].Value.Trim();
        return System.Net.IPAddress.TryParse(host, out _) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? null : host;
    }

    /// <summary>Czy nazwa rozwiązuje się wewnątrz kontenera (getent hosts). Brak narzędzia = zakładamy, że tak.</summary>
    private async Task<bool> ResolvesAsync(string containerId, string host, CancellationToken ct)
    {
        try
        {
            var exec = await docker.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                Cmd = ["getent", "hosts", host],
                AttachStdout = true,
                AttachStderr = true
            }, ct);
            using var stream = await docker.Exec.StartAndAttachContainerExecAsync(exec.ID, false, ct);
            await stream.ReadOutputToEndAsync(ct);
            var result = await docker.Exec.InspectContainerExecAsync(exec.ID, ct);
            return result.ExitCode is 0 or 126 or 127; // 126/127: brak getent w obrazie — nie zgadujemy
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Nie sprawdzono DNS w {Container}.", containerId);
            return true;
        }
    }

    private async Task<string> TailAsync(string containerId, int lines, CancellationToken ct)
    {
        try
        {
            using var stream = await docker.Containers.GetContainerLogsAsync(containerId, false,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Tail = lines.ToString() }, ct);
            var (stdout, stderr) = await stream.ReadOutputToEndAsync(ct);
            var text = (stdout + stderr).Trim();
            return text.Length > 1500 ? text[^1500..] : text;
        }
        catch { return "(brak logów)"; }
    }

    [GeneratedRegex("^pt-([a-z0-9][a-z0-9-]*)-web$")]
    private static partial Regex TenantWeb();
    [GeneratedRegex(@"^pt-[a-z0-9-]+-web-prev-\d+$")]
    private static partial Regex TenantBackup();
}
