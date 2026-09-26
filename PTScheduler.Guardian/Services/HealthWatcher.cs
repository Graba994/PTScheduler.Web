namespace PTScheduler.Guardian.Services;

public class HealthWatcher : BackgroundService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _portalUrl;
    private readonly ILogger<HealthWatcher> _logger;

    public bool PortalHealthy { get; private set; }
    public DateTime? LastCheckedAt { get; private set; }
    public DateTime? LastHealthyAt { get; private set; }
    public DateTime? DownSinceUtc { get; private set; }
    public string? LastError { get; private set; }
    public int ConsecutiveFailures { get; private set; }

    public HealthWatcher(IConfiguration config, ILogger<HealthWatcher> logger)
    {
        _portalUrl = Environment.GetEnvironmentVariable("GUARDIAN_PORTAL_URL")
            ?? config["Guardian:PortalUrl"]
            ?? "http://ptportal:8081";
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(5_000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var resp = await _http.GetAsync($"{_portalUrl}/health", ct);
                LastCheckedAt = DateTime.UtcNow;

                if (resp.IsSuccessStatusCode)
                {
                    if (!PortalHealthy && ConsecutiveFailures > 0)
                        _logger.LogInformation("Portal recovered after {Failures} failures", ConsecutiveFailures);

                    PortalHealthy = true;
                    LastHealthyAt = DateTime.UtcNow;
                    LastError = null;
                    DownSinceUtc = null;
                    ConsecutiveFailures = 0;
                }
                else
                {
                    MarkUnhealthy($"HTTP {(int)resp.StatusCode}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MarkUnhealthy(ex is HttpRequestException ? ex.Message : ex.GetType().Name);
            }

            await Task.Delay(30_000, ct);
        }
    }

    private void MarkUnhealthy(string error)
    {
        ConsecutiveFailures++;
        LastError = error;
        if (PortalHealthy)
        {
            _logger.LogWarning("Portal became unhealthy: {Error}", error);
            DownSinceUtc = DateTime.UtcNow;
        }
        PortalHealthy = false;
    }

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }
}
