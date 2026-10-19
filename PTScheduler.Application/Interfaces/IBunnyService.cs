namespace PTScheduler.Application.Interfaces;

/// <summary>Stan hostingu wideo instancji (z Portalu): czy działa, limit z pakietu i ile zajęto.</summary>
public sealed record VideoHubStatus(bool Enabled, string? Reason, string? LibraryId, long QuotaBytes, long UsedBytes, int Videos, string? LegacyLibraryId)
{
    public static readonly VideoHubStatus Off = new(false, "Hosting wideo działa w aplikacjach na platformie Hantly.", null, 0, 0, 0, null);
    public long FreeBytes => QuotaBytes == long.MaxValue ? long.MaxValue : Math.Max(0, QuotaBytes - UsedBytes);
    public int Percent => QuotaBytes is <= 0 or long.MaxValue ? 0 : (int)Math.Min(100, 100 * UsedBytes / QuotaBytes);
}

public sealed record VideoItem(string VideoRef, string Kind, string Title, long SizeBytes, DateTime CreatedAt);

/// <summary>Foldery (kolekcje Bunny) trenera w centralnej bibliotece.</summary>
public static class VideoKinds
{
    public const string Courses = "kursy";
    public const string Exercises = "cwiczenia";
}

/// <summary>
/// Wideo w centralnej bibliotece Bunny Stream platformy. Klucz biblioteki zostaje w Portalu: aplikacja prosi o nowy film,
/// dostaje jednorazowy podpis i wysyła plik prosto do Bunny (TUS). Limit miejsca liczy Portal — tyle, ile trener opłaca.
/// Odnośnik do filmu zapisujemy jako „{libraryId}/{guid}” (stare filmy: sam GUID ze starej, osobnej biblioteki).
/// </summary>
public interface IBunnyService
{
    Task<VideoHubStatus> GetStatusAsync(bool refresh = false);

    Task<(bool Ok, string? VideoRef, string? Error)> UploadAsync(string title, string kind, Stream file, long sizeBytes,
        IProgress<long>? progress = null, CancellationToken ct = default);

    Task<(bool Ok, string? Error)> DeleteAsync(string videoRef);

    Task<BunnyVideoInfo?> GetVideoInfoAsync(string videoRef);

    Task<List<VideoItem>> ListAsync();

    /// <summary>RODO: usuwa wszystkie filmy instancji, jej foldery i ćwiczenia udostępnione społeczności.</summary>
    Task<(bool Ok, int Removed, string? Error)> DeleteAllAsync();

    Task<string?> EmbedUrlAsync(string? videoRef);
}

/// <summary>Odnośnik do filmu: „{libraryId}/{guid}” albo sam GUID (stara biblioteka).</summary>
public static class VideoRef
{
    public static string Make(string libraryId, string guid) => $"{libraryId}/{guid}";

    public static (string? LibraryId, string Guid) Parse(string videoRef)
    {
        var s = videoRef.Trim();
        var i = s.IndexOf('/');
        return i > 0 ? (s[..i], s[(i + 1)..]) : (null, s);
    }

    public static string Guid(string videoRef) => Parse(videoRef).Guid;

    /// <summary>Adres odtwarzacza Bunny; dla starego odnośnika (bez biblioteki) — biblioteka zapasowa.</summary>
    public static string? Embed(string? videoRef, string? fallbackLibraryId)
    {
        if (string.IsNullOrWhiteSpace(videoRef)) return null;
        var (lib, guid) = Parse(videoRef);
        lib ??= fallbackLibraryId;
        if (string.IsNullOrWhiteSpace(lib) || !IsSafe(lib) || !IsSafe(guid)) return null;
        return $"https://iframe.mediadelivery.net/embed/{lib}/{guid}";
    }

    private static bool IsSafe(string s) => s.Length is > 0 and <= 64 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
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
        0 => "Utworzone",
        1 => "Przesłane",
        2 => "Przetwarzanie",
        3 => "Kodowanie",
        4 => "Gotowe",
        5 => "Błąd",
        6 => "Przesyłanie nieudane",
        _ => "Nieznany"
    };
    public bool IsReady => Status == 4;
}
