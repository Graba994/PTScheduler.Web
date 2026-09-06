using Docker.DotNet;
using Docker.DotNet.Models;

namespace PTScheduler.Portal.Services;

public class DockerService : IDisposable
{
    private readonly DockerClient _client = new DockerClientConfiguration(
        new Uri("unix:///var/run/docker.sock")).CreateClient();

    private readonly ILogger<DockerService> _logger;

    public DockerService(ILogger<DockerService> logger) => _logger = logger;

    public async Task<ContainerInfo?> GetContainerInfoAsync(string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName)) return null;
        try
        {
            var response = await _client.Containers.InspectContainerAsync(containerName);
            return new ContainerInfo
            {
                Id = response.ID[..12],
                Name = containerName,
                Status = response.State.Status,
                Running = response.State.Running,
                StartedAt = response.State.StartedAt,
                Image = response.Config.Image,
                MemoryUsage = 0
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<TenantContainerStatus> GetTenantStatusAsync(string slug,
        string? webContainerName = null, string? dbContainerName = null)
    {
        var webName = webContainerName ?? $"pt-{slug}-web";
        var dbName = dbContainerName ?? $"pt-{slug}-db";
        var web = await GetContainerInfoAsync(webName);
        var db = await GetContainerInfoAsync(dbName);

        return new TenantContainerStatus
        {
            Slug = slug,
            Web = web,
            Db = db,
            IsHealthy = web?.Running == true || db?.Running == true
        };
    }

    public async Task<List<TenantContainerStatus>> GetAllTenantsStatusAsync(
        IEnumerable<(string Slug, string? WebName, string? DbName)> tenants)
    {
        var results = new List<TenantContainerStatus>();
        foreach (var (slug, webName, dbName) in tenants)
            results.Add(await GetTenantStatusAsync(slug, webName, dbName));
        return results;
    }

    public async Task StartContainerAsync(string containerName)
    {
        await _client.Containers.StartContainerAsync(containerName, new ContainerStartParameters());
    }

    public async Task StopContainerAsync(string containerName)
    {
        await _client.Containers.StopContainerAsync(containerName,
            new ContainerStopParameters { WaitBeforeKillSeconds = 10 });
    }

    public async Task RemoveContainerAsync(string containerName)
    {
        await _client.Containers.RemoveContainerAsync(containerName,
            new ContainerRemoveParameters { Force = true });
    }

    public async Task RestartContainerAsync(string containerName)
    {
        await _client.Containers.RestartContainerAsync(containerName,
            new ContainerRestartParameters { WaitBeforeKillSeconds = 10 });
    }

    public async Task<string> GetContainerLogsAsync(string containerName, int tail = 100)
    {
        var parameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Tail = tail.ToString()
        };

        using var stream = await _client.Containers.GetContainerLogsAsync(containerName, false, parameters);
        var sb = new System.Text.StringBuilder();
        var buffer = new byte[8192];
        while (true)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, default);
            if (result.Count == 0) break;
            sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        return sb.ToString();
    }

    public async Task<SystemInfo> GetSystemInfoAsync()
    {
        var info = await _client.System.GetSystemInfoAsync();
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true });

        return new SystemInfo
        {
            TotalContainers = containers.Count,
            RunningContainers = containers.Count(c => c.State == "running"),
            TotalMemoryMB = info.MemTotal / 1024 / 1024,
            CpuCount = info.NCPU,
            DockerVersion = info.ServerVersion,
            OsType = info.OSType
        };
    }

    public async Task<List<DockerContainer>> ListAllContainersAsync()
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true });

        return containers.Select(c => new DockerContainer
        {
            Id = c.ID[..12],
            Name = c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID[..12],
            Image = c.Image,
            State = c.State,
            Status = c.Status,
            Ports = string.Join(", ", c.Ports
                .Where(p => p.PublicPort > 0)
                .Select(p => $"{p.PublicPort}→{p.PrivatePort}")),
            Created = c.Created
        }).OrderBy(c => c.Name).ToList();
    }

    public async Task ProvisionTenantAsync(string slug, string dbPassword, int appPort,
        string webImage, string tenantDomain, string? entitlementsJson = null,
        string? portalUrl = null, string? internalSecret = null)
    {
        var webName = $"pt-{slug}-web";
        var dbName = $"pt-{slug}-db";
        var networkName = $"pt-{slug}-net";
        var pgVolume = $"pt-{slug}-pgdata";
        var brandingVolume = $"pt-{slug}-branding";

        var existingNetworks = await _client.Networks.ListNetworksAsync(
            new NetworksListParameters { Filters = new Dictionary<string, IDictionary<string, bool>>
                { ["name"] = new Dictionary<string, bool> { [networkName] = true } } });
        if (!existingNetworks.Any(n => n.Name == networkName))
        {
            await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = networkName,
                Driver = "bridge"
            });
        }

        try { await _client.Volumes.InspectAsync(pgVolume); }
        catch { await _client.Volumes.CreateAsync(new VolumesCreateParameters { Name = pgVolume }); }

        try { await _client.Volumes.InspectAsync(brandingVolume); }
        catch { await _client.Volumes.CreateAsync(new VolumesCreateParameters { Name = brandingVolume }); }

        await EnsureImagePulledAsync("postgres:17-alpine");

        var dbExisting = await GetContainerInfoAsync(dbName);
        if (dbExisting is null)
        {
            await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = dbName,
                Image = "postgres:17-alpine",
                Env = new List<string>
                {
                    "POSTGRES_DB=ptscheduler",
                    "POSTGRES_USER=ptscheduler",
                    $"POSTGRES_PASSWORD={dbPassword}"
                },
                HostConfig = new HostConfig
                {
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                    NetworkMode = networkName,
                    Mounts = new List<Mount>
                    {
                        new() { Type = "volume", Source = pgVolume, Target = "/var/lib/postgresql/data" }
                    }
                }
            });
            await _client.Containers.StartContainerAsync(dbName, new ContainerStartParameters());
        }
        else if (!dbExisting.Running)
        {
            await _client.Containers.StartContainerAsync(dbName, new ContainerStartParameters());
        }

        // POSTGRES_PASSWORD is only honoured by the official postgres image on first init —
        // if the data directory (volume) already existed (leftover from an earlier attempt,
        // an import, or a manually recreated container), Postgres silently keeps whatever
        // password was baked in at that time and ignores the env var on subsequent starts.
        // Force it to match tenant.DbPassword every single time so the web container's
        // connection string is guaranteed to work, regardless of the volume's history.
        var (syncOk, syncError) = await EnsureDbPasswordAsync(dbName, dbPassword);
        if (!syncOk)
            throw new InvalidOperationException($"Nie udało się zsynchronizować hasła bazy danych: {syncError}");

        var webExisting = await GetContainerInfoAsync(webName);
        if (webExisting is null)
        {
            var env = new List<string>
            {
                $"ConnectionStrings__DefaultConnection=Host={dbName};Port=5432;Database=ptscheduler;Username=ptscheduler;Password={dbPassword}",
                "ASPNETCORE_ENVIRONMENT=Production",
                $"TENANT_SLUG={slug}",
                $"TENANT_DOMAIN={tenantDomain}"
            };
            if (!string.IsNullOrWhiteSpace(entitlementsJson))
                env.Add($"TENANT_ENTITLEMENTS={entitlementsJson}");
            if (!string.IsNullOrWhiteSpace(portalUrl))
                env.Add($"PORTAL_URL={portalUrl}");
            if (!string.IsNullOrWhiteSpace(internalSecret))
                env.Add($"TENANT_INTERNAL_SECRET={internalSecret}");

            await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = webName,
                Image = webImage,
                Env = env,
                ExposedPorts = new Dictionary<string, EmptyStruct> { ["8080/tcp"] = default },
                HostConfig = new HostConfig
                {
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                    NetworkMode = networkName,
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        ["8080/tcp"] = new List<PortBinding>
                        {
                            new() { HostPort = appPort.ToString() }
                        }
                    },
                    Mounts = new List<Mount>
                    {
                        new() { Type = "volume", Source = brandingVolume, Target = "/app/wwwroot/branding" }
                    }
                }
            });
            await _client.Containers.StartContainerAsync(webName, new ContainerStartParameters());
        }
    }

    public async Task<bool> ImageExistsAsync(string image)
    {
        try { await _client.Images.InspectImageAsync(image); return true; }
        catch { return false; }
    }

    public async Task EnsureImagePulledAsync(string image)
    {
        if (await ImageExistsAsync(image)) return;
        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = image },
            null,
            new Progress<JSONMessage>());
    }

    public async Task RecreateWebContainerAsync(string slug, string dbPassword, int appPort,
        string webImage, string tenantDomain, string entitlementsJson,
        string? portalUrl = null, string? internalSecret = null)
    {
        var webName = $"pt-{slug}-web";
        try { await _client.Containers.StopContainerAsync(webName, new ContainerStopParameters()); } catch (Exception ex) { _logger.LogDebug(ex, "Nie zatrzymano {Container} przed reprovisioningiem (może nie istnieć).", webName); }
        try { await _client.Containers.RemoveContainerAsync(webName, new ContainerRemoveParameters { Force = true }); } catch (Exception ex) { _logger.LogDebug(ex, "Nie usunięto {Container} przed reprovisioningiem (może nie istnieć).", webName); }
        await ProvisionTenantAsync(slug, dbPassword, appPort, webImage, tenantDomain, entitlementsJson, portalUrl, internalSecret);
    }

    /// <summary>
    /// Forces the Postgres "ptscheduler" role's password inside the given DB container to
    /// match <paramref name="password"/>. Retries for a few seconds since a just-started
    /// container needs a moment before it accepts connections. Safe to call unconditionally —
    /// it's a plain ALTER USER, not a destructive operation.
    /// </summary>
    public async Task<(bool Success, string? Error)> EnsureDbPasswordAsync(string dbContainerName, string password)
    {
        var sql = $"ALTER USER ptscheduler WITH PASSWORD '{password.Replace("'", "''")}';";
        Exception? lastError = null;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var exec = await _client.Exec.ExecCreateContainerAsync(dbContainerName, new ContainerExecCreateParameters
                {
                    AttachStdout = true,
                    AttachStderr = true,
                    Cmd = new List<string> { "psql", "-U", "ptscheduler", "-d", "ptscheduler", "-v", "ON_ERROR_STOP=1", "-c", sql }
                });

                using var stream = await _client.Exec.StartAndAttachContainerExecAsync(exec.ID, false);
                var (stdout, stderr) = await stream.ReadOutputToEndAsync(CancellationToken.None);

                var inspect = await _client.Exec.InspectContainerExecAsync(exec.ID);
                if (inspect.ExitCode == 0) return (true, null);

                lastError = new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(1000);
        }

        return (false, lastError?.Message ?? "Nieznany błąd synchronizacji hasła.");
    }

    public async Task<ContainerInspectResponse?> InspectAsync(string containerName)
    {
        try { return await _client.Containers.InspectContainerAsync(containerName); }
        catch { return null; }
    }

    public async Task<string?> StartDetachedAsync(CreateContainerParameters spec)
    {
        var created = await _client.Containers.CreateContainerAsync(spec);
        await _client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters());
        return created.ID;
    }

    public async Task RemoveTenantResourcesAsync(string slug)
    {
        var networkName = $"pt-{slug}-net";
        var pgVolume = $"pt-{slug}-pgdata";
        var brandingVolume = $"pt-{slug}-branding";

        try { await _client.Networks.DeleteNetworkAsync(networkName); } catch (Exception ex) { _logger.LogDebug(ex, "Nie usunięto sieci {Network} (może nie istnieć).", networkName); }
        try { await _client.Volumes.RemoveAsync(pgVolume); } catch (Exception ex) { _logger.LogDebug(ex, "Nie usunięto wolumenu {Volume} (może nie istnieć).", pgVolume); }
        try { await _client.Volumes.RemoveAsync(brandingVolume); } catch (Exception ex) { _logger.LogDebug(ex, "Nie usunięto wolumenu {Volume} (może nie istnieć).", brandingVolume); }
    }

    public void Dispose() => _client.Dispose();
}

public class ContainerInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Running { get; set; }
    public string? StartedAt { get; set; }
    public string? Image { get; set; }
    public long MemoryUsage { get; set; }
}

public class TenantContainerStatus
{
    public string Slug { get; set; } = "";
    public ContainerInfo? Web { get; set; }
    public ContainerInfo? Db { get; set; }
    public bool IsHealthy { get; set; }
}

public class DockerContainer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string State { get; set; } = "";
    public string Status { get; set; } = "";
    public string Ports { get; set; } = "";
    public DateTime Created { get; set; }
}

public class SystemInfo
{
    public int TotalContainers { get; set; }
    public int RunningContainers { get; set; }
    public long TotalMemoryMB { get; set; }
    public long CpuCount { get; set; }
    public string DockerVersion { get; set; } = "";
    public string OsType { get; set; } = "";
}
