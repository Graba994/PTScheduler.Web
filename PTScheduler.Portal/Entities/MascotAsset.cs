namespace PTScheduler.Portal.Entities;

/// <summary>Własna grafika maskotki dla jednego nastroju (podmienia wbudowany SVG z MascotArt).</summary>
public class MascotAsset
{
    /// <summary>Klucz nastroju: hello, happy, celebrate, think, remind, sleep.</summary>
    public string Mood { get; set; } = "";
    public string ContentType { get; set; } = "image/svg+xml";
    public byte[] Data { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
