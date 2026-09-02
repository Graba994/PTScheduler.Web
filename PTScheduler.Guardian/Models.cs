using System.Text.Json.Serialization;

namespace PTScheduler.Guardian;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpgradeTarget { Portal, Tenant }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpgradeStage { Queued, Pulling, Building, Testing, Swapping, Verifying, Done }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpgradeStatus { Running, Success, Failed, RolledBack }

public class UpgradeJob
{
    public string Id { get; set; } = "";
    public UpgradeTarget Target { get; set; }
    public UpgradeStage Stage { get; set; }
    public UpgradeStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CommitBefore { get; set; } = "";
    public string CommitAfter { get; set; } = "";
    public string? Error { get; set; }
    public List<LogEntry> Log { get; set; } = [];
    public bool RebuildImage { get; set; }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Stage { get; set; } = "";
    public string Message { get; set; } = "";
    public string Level { get; set; } = "info";
}

public class GuardianStatus
{
    public bool Healthy { get; set; } = true;
    public string Uptime { get; set; } = "";
    public bool PortalHealthy { get; set; }
    public DateTime? PortalLastChecked { get; set; }
    public UpgradeJob? ActiveJob { get; set; }
    public int TotalJobs { get; set; }
}
