using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Prywatny katalog na trwałym wolumenie tenanta (<c>wwwroot/branding/_private</c>).
/// Jedyny trwały wolumen kontenera to <c>/app/wwwroot/branding</c>, więc sekrety
/// (klucze Bunny, token Google) i prywatne pliki klientów muszą leżeć na nim —
/// ale NIE mogą być serwowane statycznie. Program.cs blokuje ścieżkę
/// <c>/branding/_private</c> (oraz każdy <c>/branding/*.json</c>) przed UseStaticFiles.
/// </summary>
public static class PrivateStorage
{
    public const string DirectoryName = "_private";

    public static string Root(IWebRootPathProvider webRoot) =>
        Path.Combine(webRoot.WebRootPath, "branding", DirectoryName);

    /// <summary>
    /// Ścieżka pliku w katalogu prywatnym. Jeśli plik o tej nazwie leży jeszcze
    /// w publicznym <c>branding/</c> (starsze wersje), zostaje przeniesiony.
    /// </summary>
    public static string SettingsFile(IWebRootPathProvider webRoot, string fileName)
    {
        var dir = Root(webRoot);
        var target = Path.Combine(dir, fileName);
        var legacy = Path.Combine(webRoot.WebRootPath, "branding", fileName);
        try
        {
            if (!File.Exists(target) && File.Exists(legacy))
            {
                Directory.CreateDirectory(dir);
                File.Move(legacy, target);
            }
        }
        catch (IOException) { /* równoległe przeniesienie — plik już jest na miejscu */ }
        return target;
    }
}
