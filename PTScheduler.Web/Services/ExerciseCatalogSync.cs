using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Web.Services;

/// <summary>
/// Jedno pobieranie katalogu z wger.de naraz, z postępem widocznym w panelu administratora.
/// Import działa w tle — zamknięcie strony go nie przerywa.
/// </summary>
public sealed class ExerciseCatalogSync(IServiceScopeFactory scopes, ILogger<ExerciseCatalogSync> logger)
{
    private readonly object _gate = new();
    private Task? _running;

    public bool IsRunning { get; private set; }
    public int Done { get; private set; }
    public int Total { get; private set; }
    public string? LastError { get; private set; }
    public WgerImportResult? LastResult { get; private set; }

    /// <summary>Zmiana stanu (postęp, koniec) — strona odświeża się przez InvokeAsync.</summary>
    public event Action? Changed;

    /// <summary>Uruchamia import w tle; false, gdy już trwa.</summary>
    public bool TryStart() => StartOrJoin(out _);

    /// <summary>Uruchamia import (albo dołącza do trwającego) i czeka na koniec.</summary>
    public Task RunAsync()
    {
        StartOrJoin(out var task);
        return task;
    }

    private bool StartOrJoin(out Task task)
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false })
            {
                task = _running;
                return false;
            }
            IsRunning = true;
            Done = 0;
            Total = 0;
            LastError = null;
            task = _running = Task.Run(ImportAsync);
        }
        Notify();
        return true;
    }

    private async Task ImportAsync()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<IWgerCatalogImporter>();
            var modules = scope.ServiceProvider.GetRequiredService<IModuleSettingsService>();

            var lastNotify = DateTime.MinValue;
            var progress = new InlineProgress(p =>
            {
                (Done, Total) = p;
                if (DateTime.UtcNow - lastNotify < TimeSpan.FromMilliseconds(400)) return;
                lastNotify = DateTime.UtcNow;
                Notify();
            });

            var result = await importer.ImportAsync(progress);
            LastResult = result;

            var settings = await modules.GetAsync();
            settings.WgerSyncedAt = DateTime.UtcNow;
            settings.WgerExerciseCount = result.Added + result.Updated;
            await modules.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            LastError = ex switch
            {
                HttpRequestException => "Nie udało się połączyć z wger.de. Sprawdź, czy serwer ma dostęp do internetu, i spróbuj ponownie.",
                TaskCanceledException => "wger.de nie odpowiedziało na czas. Spróbuj ponownie za kilka minut.",
                _ => "Pobieranie z wger.de nie powiodło się. Spróbuj ponownie za kilka minut."
            };
            logger.LogWarning(ex, "Import katalogu ćwiczeń z wger.de nie powiódł się.");
        }
        finally
        {
            IsRunning = false;
            Notify();
        }
    }

    private void Notify()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { logger.LogDebug(ex, "Odbiorca zdarzenia postępu wger zgłosił błąd."); }
    }

    private sealed class InlineProgress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => report(value);
    }
}

/// <summary>
/// Gdy katalogiem jest wger.de, raz w tygodniu pobiera aktualizacje (nowe ćwiczenia,
/// tłumaczenia, zdjęcia). Pierwsze pobranie po zmianie źródła startuje od razu z panelu.
/// </summary>
public sealed class WgerCatalogRefreshService(
    IServiceScopeFactory scopes,
    ExerciseCatalogSync sync,
    ILogger<WgerCatalogRefreshService> logger) : BackgroundService
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var settings = await scope.ServiceProvider.GetRequiredService<IModuleSettingsService>().GetAsync();
                var due = settings.WgerSyncedAt is null
                          || settings.WgerExerciseCount == 0
                          || DateTime.UtcNow - settings.WgerSyncedAt > RefreshEvery;
                if (settings.ExerciseCatalogSource == ExerciseSource.Wger && due)
                    await sync.RunAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "wger: sprawdzenie aktualizacji katalogu nie powiodło się.");
            }
            await Task.Delay(TimeSpan.FromHours(6), ct);
        }
    }
}
