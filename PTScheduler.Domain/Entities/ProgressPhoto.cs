using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>
/// Zdjęcie sylwetki klienta. Plik leży w prywatnym katalogu tenanta
/// (<c>branding/_private/photos/{ClientId}/</c>), nigdy nie jest serwowany
/// statycznie — tylko przez endpoint sprawdzający, że patrzy klient albo jego trener.
/// </summary>
public class ProgressPhoto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public DateOnly TakenOn { get; set; }
    public PhotoPose Pose { get; set; }
    public string? Note { get; set; }

    /// <summary>Losowa nazwa pliku (bez ścieżki), np. <c>3f2a….webp</c>; miniatura ma sufiks <c>_t</c>.</summary>
    public string FileName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }

    public string UploadedByUserId { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
