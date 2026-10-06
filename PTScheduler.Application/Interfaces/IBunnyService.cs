using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public record CentralizedCdnStatus(bool PlatformCdnEnabled, decimal StorageCredits, decimal BandwidthCredits);

public interface IBunnyService
{
    Task<BunnySettingsDto> GetSettingsAsync();
    Task SaveSettingsAsync(BunnySettingsDto dto);

    Task<(bool Ok, string Message)> TestConnectionAsync(BunnySettingsDto dto);

    Task<(bool Ok, string? VideoId, string? Error)> CreateAndUploadAsync(
        string title, Stream file, CancellationToken ct = default);

    Task<(bool Ok, string? Error)> DeleteAsync(string videoId);

    Task<BunnyVideoInfo?> GetVideoInfoAsync(string videoId);

    string BuildEmbedUrl(string videoId);

    Task<CentralizedCdnStatus?> GetCentralizedStatusAsync();
}

public class BunnyVideoInfo
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public long StorageSize { get; set; }
    public int Length { get; set; } // seconds
    public int Status { get; set; }
    public string StatusName => Status switch
    {
        0 => "Created",
        1 => "Uploaded",
        2 => "Processing",
        3 => "Transcoding",
        4 => "Finished",
        5 => "Error",
        6 => "UploadFailed",
        _ => "Unknown"
    };
    public bool IsReady => Status == 4;
}
