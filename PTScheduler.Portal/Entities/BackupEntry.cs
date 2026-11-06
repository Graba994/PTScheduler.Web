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

    /// <summary>Kiedy kopię próbnie odtworzono do tymczasowej bazy.</summary>
    public DateTime? VerifiedAt { get; set; }
    public bool? VerifyOk { get; set; }
    /// <summary>Np. „52 tabele · ostatnia migracja 20260929_AccountSettings” albo opis błędu.</summary>
    public string? VerifyInfo { get; set; }

    /// <summary>Kiedy kopię wysłano poza serwer (SFTP / Google Drive).</summary>
    public DateTime? OffsiteAt { get; set; }
    public bool? OffsiteOk { get; set; }
    public string? OffsiteInfo { get; set; }
}

public enum BackupKind
{
    Manual,
    Scheduled,
    /// <summary>Stan sprzed odtworzenia kopii — żeby dało się cofnąć.</summary>
    PreRestore,
    /// <summary>Zapasowa kopia bazy Portalu zrobiona przez Guardiana.</summary>
    Guardian,
    /// <summary>Plik dodany ręcznie albo pobrany spoza serwera.</summary>
    Imported
}

public enum BackupStatus
{
    Running,
    Completed,
    Failed
}
