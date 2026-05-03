using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

public class HintStateService
{
    private bool _loaded;
    private bool _showHints = true;

    public bool ShowHints => _showHints;

    public async Task EnsureLoadedAsync(string userId, INotificationPreferencesService svc)
    {
        if (_loaded) return;
        _loaded = true;
        if (string.IsNullOrEmpty(userId)) return;
        var prefs = await svc.GetAsync(userId);
        _showHints = prefs.ShowHints;
    }

    public void Set(bool value) => _showHints = value;
}
