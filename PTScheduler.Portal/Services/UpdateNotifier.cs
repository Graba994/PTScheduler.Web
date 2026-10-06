namespace PTScheduler.Portal.Services;

// Shared singleton — background poller writes here, sidebar reads.
// Prevents every AdminLayout render from hitting GitHub API.
public class UpdateNotifier
{
    public bool UpgradeAvailable { get; set; }
    public string? RemoteCommitShort { get; set; }
    public DateTime? LastCheckedAt { get; set; }
}

public class UpdatePollerService(
    IServiceScopeFactory scopeFactory,
    UpdateNotifier notifier,
    ILogger<UpdatePollerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit after startup so DB is ready
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<UpdateService>();
                var current = await svc.GetCurrentAsync();
                var remote = await svc.CheckRemoteAsync();

                notifier.LastCheckedAt = DateTime.UtcNow;
                notifier.UpgradeAvailable = svc.IsBehind(current, remote);
                notifier.RemoteCommitShort = remote?.CommitShort;

                if (notifier.UpgradeAvailable)
                    logger.LogInformation("Upgrade available: {Local} → {Remote}",
                        current.CommitShort, remote?.CommitShort);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Update check failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
