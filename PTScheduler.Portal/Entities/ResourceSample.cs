namespace PTScheduler.Portal.Entities;

/// <summary>
/// Próbka zużycia zasobów: instancji trenera (TenantId) albo całego serwera (TenantId = null).
/// Co 5 minut procesor i pamięć, co godzinę rozmiar bazy i plików. Po 2 dniach zostaje jedna próbka na godzinę.
/// </summary>
public class ResourceSample
{
    public long Id { get; set; }
    public int? TenantId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;

    /// <summary>Trener: rdzenie zajęte przez aplikację i bazę. Serwer: średnie obciążenie (load 1 min).</summary>
    public double? CpuCores { get; set; }
    /// <summary>Trener: suma limitów procesora kontenerów. Serwer: liczba rdzeni.</summary>
    public double? CpuLimitCores { get; set; }

    /// <summary>Trener: pamięć aplikacji + bazy. Serwer: pamięć zajęta (bez pamięci podręcznej).</summary>
    public long? MemoryBytes { get; set; }
    /// <summary>Trener: suma limitów pamięci kontenerów. Serwer: pamięć całkowita.</summary>
    public long? MemoryLimitBytes { get; set; }
    public long? WebMemoryBytes { get; set; }
    public long? DbMemoryBytes { get; set; }

    /// <summary>Trener: rozmiar bazy i plików (logo, zdjęcia, dokumenty) — mierzone co godzinę.</summary>
    public long? DbSizeBytes { get; set; }
    public long? FilesBytes { get; set; }

    /// <summary>Serwer: dysk Dockera (obrazy, kontenery, wolumeny) i dysk z kopiami.</summary>
    public long? DiskUsedBytes { get; set; }
    public long? DiskTotalBytes { get; set; }
    public long? BackupDiskUsedBytes { get; set; }
    public long? BackupDiskTotalBytes { get; set; }
}
