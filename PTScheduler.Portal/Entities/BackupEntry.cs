namespace PTScheduler.Portal.Entities;

public class BackupEntry
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty; // "portal" for portal DB
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public BackupKind Kind { get; set; }
    public BackupStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan? Duration { get; set; }
}

public enum BackupKind
{
    Manual,
    Scheduled
}

public enum BackupStatus
{
    Running,
    Completed,
    Failed
}
