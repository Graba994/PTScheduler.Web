using System.Text.Json;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Services;

namespace PTScheduler.Web.Services;

/// <summary>
/// Publiczne demo Hantly: ta instancja służy jako pokaz dla odwiedzających hantly.pl. Włącza i wyłącza je Portal
/// (POST /internal/demo/mode). W trybie demo nic nie wychodzi na zewnątrz — e-maile, SMS-y, powiadomienia push,
/// płatności i KSeF są wstrzymane (DemoGuards), część stron jest zablokowana (PublicDemoRules), a u góry widać baner.
/// Stan leży w prywatnym pliku ustawień, więc przetrwa restart kontenera.
/// </summary>
public sealed class PublicDemoService(IWebRootPathProvider webRoot, ILogger<PublicDemoService> logger)
{
    private sealed class Settings
    {
        public bool Enabled { get; set; }
        public string? RegisterUrl { get; set; }
        public DateTime? ChangedAt { get; set; }
    }

    private readonly object _lock = new();
    private Settings? _cache;

    private string FilePath => PrivateStorage.SettingsFile(webRoot, "public-demo.json");

    private Settings Read()
    {
        lock (_lock)
        {
            if (_cache is not null) return _cache;
            try { _cache = File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new(); }
            catch (Exception ex) when (ex is IOException or JsonException) { logger.LogWarning(ex, "Nie udało się odczytać ustawień demo."); _cache = new(); }
            return _cache;
        }
    }

    public bool IsOn => Read().Enabled;

    /// <summary>Dokąd prowadzi „Zbuduj swoją aplikację” z banera (kreator na hantly.pl).</summary>
    public string RegisterUrl => Read().RegisterUrl is { Length: > 0 } u ? u : "https://hantly.pl/register";

    public void Set(bool on, string? registerUrl)
    {
        lock (_lock)
        {
            var s = new Settings
            {
                Enabled = on,
                RegisterUrl = registerUrl is { Length: > 0 } u && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? u : Read().RegisterUrl,
                ChangedAt = DateTime.UtcNow
            };
            Directory.CreateDirectory(PrivateStorage.Root(webRoot));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s));
            _cache = s;
        }
        logger.LogInformation("Publiczne demo {Stan}.", on ? "włączone" : "wyłączone");
    }
}
