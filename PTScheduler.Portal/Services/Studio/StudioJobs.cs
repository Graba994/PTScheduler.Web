using System.Net;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>Wykonawca jednego rodzaju zadania kolejki Studia (write, voice, render, qa, screens, sounds…).</summary>
public interface IStudioJobHandler
{
    string Type { get; }
    Task HandleAsync(StudioJob job, CancellationToken ct);
}

/// <summary>Błąd, którego ponawianie nic nie da (np. brak klucza) — zadanie od razu kończy się porażką.</summary>
public class StudioJobFatalException(string message) : Exception(message);

/// <summary>
/// Kolejka zadań Studia w bazie (tabela StudioJobs). Jedno zadanie naraz, 3 próby z odstępem 2 i 4 min.
/// Po ostatniej porażce film dostaje status Failed z opisem błędu, a administrator — powiadomienie.
/// </summary>
public class StudioJobs(
    IDbContextFactory<PortalDbContext> dbFactory,
    IEnumerable<IStudioJobHandler> handlers,
    StudioNotifier notifier,
    ILogger<StudioJobs> logger)
{
    public const int MaxAttempts = 3;

    /// <summary>Kiedy ponowić po <paramref name="attempts"/> nieudanych próbach; null = koniec prób.</summary>
    public static DateTime? RetryAt(int attempts, DateTime now) => attempts >= MaxAttempts ? null : now.AddMinutes(2 * attempts);

    /// <summary>Dodaje zadanie. Jeśli takie samo (rodzaj + film) już czeka albo trwa — zwraca istniejące.</summary>
    public Task<int> EnqueueAsync(string type, int? videoId = null, string? payloadJson = null, DateTime? runAfter = null) =>
        EnqueueAsync(dbFactory, type, videoId, payloadJson, runAfter);

    /// <summary>To samo bez całej kolejki — dla usług, które tylko zlecają (np. po przebudowie wiedzy).</summary>
    public static async Task<int> EnqueueAsync(IDbContextFactory<PortalDbContext> dbFactory, string type, int? videoId = null,
        string? payloadJson = null, DateTime? runAfter = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.StudioJobs.Where(j => j.Type == type && j.VideoId == videoId
            && (j.Status == StudioJobStatus.Pending || j.Status == StudioJobStatus.Running)).Select(j => (int?)j.Id).FirstOrDefaultAsync();
        if (existing is { } id) return id;
        var job = new StudioJob { Type = type, VideoId = videoId, PayloadJson = payloadJson, RunAfter = runAfter };
        db.StudioJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>Czy zadanie danego rodzaju czeka albo trwa (np. do blokowania przycisku w panelu).</summary>
    public async Task<bool> IsQueuedAsync(string type, int? videoId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StudioJobs.AnyAsync(j => j.Type == type && j.VideoId == videoId
            && (j.Status == StudioJobStatus.Pending || j.Status == StudioJobStatus.Running));
    }

    /// <summary>Ostatnie zadanie danego rodzaju (stan i błąd do pokazania w panelu).</summary>
    public async Task<StudioJob?> LastAsync(string type, int? videoId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StudioJobs.AsNoTracking().Where(j => j.Type == type && j.VideoId == videoId)
            .OrderByDescending(j => j.Id).FirstOrDefaultAsync();
    }

    /// <summary>Po restarcie Portalu: zadania przerwane w trakcie wracają do kolejki (bez liczenia próby).</summary>
    public async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await db.StudioJobs.Where(j => j.Status == StudioJobStatus.Running).ExecuteUpdateAsync(u => u
            .SetProperty(j => j.Status, StudioJobStatus.Pending)
            .SetProperty(j => j.Attempts, j => j.Attempts > 0 ? j.Attempts - 1 : 0), ct);
        if (n > 0) logger.LogInformation("Studio: {Count} przerwanych zadań wraca do kolejki.", n);
    }

    /// <summary>Wykonuje jedno zadanie. Zwraca true, gdy coś zrobiła.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        StudioJob? job;
        var now = DateTime.UtcNow;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            job = await db.StudioJobs.Where(j => j.Status == StudioJobStatus.Pending && (j.RunAfter == null || j.RunAfter <= now))
                .OrderBy(j => j.RunAfter ?? j.CreatedAt).ThenBy(j => j.Id).FirstOrDefaultAsync(ct);
            if (job is null) return false;
            job.Status = StudioJobStatus.Running;
            job.Attempts++;
            job.StartedAt = now;
            await db.SaveChangesAsync(ct);
        }

        string? error = null;
        var fatal = false;
        var handler = handlers.FirstOrDefault(h => h.Type == job.Type);
        if (handler is null) { error = $"Nieznany rodzaj zadania „{job.Type}”."; fatal = true; }
        else
        {
            try { await handler.HandleAsync(job, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } // zamykanie Portalu — RecoverAsync wznowi
            catch (StudioJobFatalException ex) { error = ex.Message; fatal = true; }
            catch (Exception ex)
            {
                error = ex is RendererException or HttpRequestException or InvalidOperationException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                logger.LogWarning(ex, "Studio: zadanie {Type} #{Id} (próba {Attempt}) nie powiodło się.", job.Type, job.Id, job.Attempts);
            }
        }

        var retry = fatal ? null : RetryAt(job.Attempts, DateTime.UtcNow);
        await using (var db = await dbFactory.CreateDbContextAsync(CancellationToken.None))
        {
            var j = await db.StudioJobs.FirstAsync(x => x.Id == job.Id, CancellationToken.None);
            j.FinishedAt = DateTime.UtcNow;
            j.Error = Cut(error, 2000);
            j.Status = error is null ? StudioJobStatus.Done : retry is null ? StudioJobStatus.Failed : StudioJobStatus.Pending;
            j.RunAfter = error is not null ? retry : j.RunAfter;
            if (j.Status == StudioJobStatus.Failed && j.VideoId is { } videoId
                && await db.StudioVideos.FirstOrDefaultAsync(v => v.Id == videoId, CancellationToken.None) is { } video)
            {
                video.Status = StudioVideoStatus.Failed;
                video.LastError = Cut($"{JobLabel(j.Type)}: {error}", 2000);
                video.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(CancellationToken.None);
            if (j.Status == StudioJobStatus.Failed)
                await notifier.NotifyAsync($"Studio reklam: {JobLabel(j.Type).ToLowerInvariant()} — błąd",
                    $"Zadanie nie powiodło się{(fatal ? "" : $" po {j.Attempts} próbach")}: {error}", j.VideoId);
        }
        return true;
    }

    public static string JobLabel(string type) => type switch
    {
        "write" => "Scenariusz",
        "voice" => "Głos",
        "render" => "Render",
        "qa" => "Kontrola klatek",
        "screens" => "Zrzuty aplikacji",
        "sounds" => "Biblioteka dźwięków",
        _ => type,
    };

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

/// <summary>Powiadomienia Studia: e-mail na adres administratora z Konfiguracji (ten sam co przy innych alertach Portalu).</summary>
public class StudioNotifier(SiteSettingsService settings, EmailService email, IConfiguration config, ILogger<StudioNotifier> logger)
{
    public async Task NotifyAsync(string subject, string text, int? videoId = null)
    {
        try
        {
            var to = (await settings.GetAsync(K.AdminNotificationEmail)).Trim();
            if (to.Length == 0) return;
            var url = $"{PortalUrls.Public(config)}/panel/studio{(videoId is { } id ? $"/film/{id}" : "")}";
            var html = $"""
                <!DOCTYPE html>
                <html lang="pl"><body style="margin:0;background:#f1f5f9;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,sans-serif;color:#0f172a">
                <div style="max-width:560px;margin:0 auto;padding:24px 16px">
                  <div style="background:#fff;border-radius:16px;padding:28px">
                    <div style="margin-bottom:12px">{EmailService.LogoMarker}</div>
                    <h1 style="font-size:20px;margin:0 0 12px">{WebUtility.HtmlEncode(subject)}</h1>
                    <div style="font-size:15px;line-height:1.6">{WebUtility.HtmlEncode(text)}</div>
                    <p style="text-align:center;margin:24px 0 8px"><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#7C3AED;color:#fff;text-decoration:none;padding:12px 24px;border-radius:10px;font-weight:600">Otwórz Studio reklam</a></p>
                  </div>
                  <p style="text-align:center;color:#94a3b8;font-size:12px;margin-top:16px">Hantly · wiadomość wysłana automatycznie</p>
                </div></body></html>
                """;
            await email.SendAsync(to, subject, html);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Studio: powiadomienie nie wysłane."); }
    }
}

/// <summary>
/// Pętla kolejki filmów — osobno od kolejki tekstów (StudioBackgroundService), żeby kilkuminutowy render
/// nie wstrzymywał pisania reklam i postów. Co 10 s sprawdza, czy jest zadanie.
/// </summary>
public class StudioJobsBackgroundService(IServiceScopeFactory scopes, ILogger<StudioJobsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(40), ct); } catch (OperationCanceledException) { return; }
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<StudioJobs>().RecoverAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Studio: wznowienie kolejki nie powiodło się."); }

        while (!ct.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                using var scope = scopes.CreateScope();
                worked = await scope.ServiceProvider.GetRequiredService<StudioJobs>().ProcessNextAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Studio: przebieg kolejki filmów nie powiódł się."); }
            try { await Task.Delay(worked ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10), ct); } catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>Zadanie „sounds”: generuje brakujące efekty i muzykę do biblioteki (ElevenLabs, raz).</summary>
public class SoundsJobHandler(StudioMedia media) : IStudioJobHandler
{
    public string Type => "sounds";

    public async Task HandleAsync(StudioJob job, CancellationToken ct)
    {
        var (created, errors) = await media.GenerateMissingSoundsAsync(ct);
        if (errors.Count > 0 && created == 0)
        {
            var msg = string.Join("; ", errors.Take(3));
            if (errors.Any(e => e.Contains("Brak klucza") || e.Contains("uprawnienie") || e.Contains("identyfikator"))) throw new StudioJobFatalException(msg);
            throw new InvalidOperationException(msg);
        }
    }
}
