namespace PTScheduler.Application.DTOs;

public class BunnySettingsDto
{
    public string? ApiKey { get; set; }        // Account-level API key (Bunny Stream Library API key works)
    public string? LibraryId { get; set; }     // Bunny Stream Library ID
    public string? CdnHostname { get; set; }   // e.g. vz-a1b2c3d4-e5f.b-cdn.net
    public string? PullZoneUrl { get; set; }   // optional, for HLS
    public bool Enabled { get; set; }
}
