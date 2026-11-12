using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;

namespace PTScheduler.Web.Services;

/// <summary>
/// Program testerów w aplikacji: karta z ankietą na pulpicie (gdy Portal mówi, że czeka opinia za bieżący tydzień)
/// i raz w tygodniu push do właściciela. Wysłany tydzień zapamiętujemy lokalnie — karta znika od razu, bez czekania na Portal.
/// </summary>
public class TesterFeedbackService(EntitlementService entitlements, IWebRootPathProvider webRoot, IHttpClientFactory httpFactory, ILogger<TesterFeedbackService> logger)
{
    private sealed class State
    {
        public int SubmittedWeek { get; set; }
        public int RemindedWeek { get; set; }
    }

    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private string FilePath => PrivateStorage.SettingsFile(webRoot, "tester-feedback.json");

    public TesterInfo? Info => entitlements.Current.Tester;

    public async Task<bool> IsDueAsync() =>
        Info is { Active: true, FeedbackDue: true, Week: >= 1 } t && (await ReadAsync()).SubmittedWeek != t.Week;

    public async Task<(bool Ok, string? Error)> SubmitAsync(int rating, string? annoyed, string? missing)
    {
        if (Info is not { } t) return (false, "Studio nie jest w programie testerów.");
        var portal = Environment.GetEnvironmentVariable("PORTAL_URL");
        var slug = Environment.GetEnvironmentVariable("TENANT_SLUG");
        var secret = Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");
        if (string.IsNullOrEmpty(portal) || string.IsNullOrEmpty(slug)) return (false, "Brak połączenia z platformą.");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{portal.TrimEnd('/')}/api/internal/tenants/{Uri.EscapeDataString(slug)}/tester-feedback")
            {
                Content = JsonContent.Create(new { week = t.Week, rating, annoyed, missing })
            };
            if (!string.IsNullOrEmpty(secret)) req.Headers.Add("X-Internal-Secret", secret);
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Portal nie przyjął opinii testera ({Status}).", (int)resp.StatusCode);
                return (false, "Nie udało się wysłać — spróbuj za chwilę.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Opinia testera nie wysłana.");
            return (false, "Brak połączenia z platformą — spróbuj za chwilę.");
        }
        await UpdateAsync(s => s.SubmittedWeek = t.Week);
        return (true, null);
    }

    /// <summary>Push „czeka ankieta” — raz na tydzień programu, w ciągu dnia.</summary>
    public async Task<bool> ShouldRemindAsync(DateTime localNow) =>
        localNow.Hour is >= 10 and < 19 && await IsDueAsync() && (await ReadAsync()).RemindedWeek != Info!.Week;

    public Task MarkRemindedAsync() => UpdateAsync(s => s.RemindedWeek = Info?.Week ?? 0);

    private async Task<State> ReadAsync()
    {
        try
        {
            if (!File.Exists(FilePath)) return new State();
            return JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(FilePath)) ?? new State();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new State(); }
    }

    private async Task UpdateAsync(Action<State> change)
    {
        await FileLock.WaitAsync();
        try
        {
            var s = await ReadAsync();
            change(s);
            await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(s));
        }
        catch (IOException ex) { logger.LogWarning(ex, "Nie zapisano stanu ankiety testera."); }
        finally { FileLock.Release(); }
    }
}

/// <summary>Co godzinę: gdy czeka ankieta testera, a w tym tygodniu jeszcze nie przypomnieliśmy — push do właściciela.</summary>
public class TesterReminderBackgroundService(IServiceScopeFactory scopes, ILogger<TesterReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(5), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var testers = scope.ServiceProvider.GetRequiredService<TesterFeedbackService>();
                var clock = scope.ServiceProvider.GetRequiredService<IAppClock>();
                if (await testers.ShouldRemindAsync(clock.LocalNow))
                {
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                    var push = scope.ServiceProvider.GetRequiredService<IWebPushService>();
                    var roots = (await users.GetUsersInRoleAsync(Roles.Root)).Select(u => u.Id).ToHashSet();
                    var owners = (await users.GetUsersInRoleAsync(Roles.Admin)).Where(u => !roots.Contains(u.Id)).ToList();
                    if (owners.Count == 0) owners = (await users.GetUsersInRoleAsync(Roles.Trainer)).ToList();
                    var week = testers.Info!.Week;
                    foreach (var u in owners)
                        await push.SendAsync(u.Id, new PushMessageDto
                        {
                            Title = $"Tydzień {week} z aplikacją — jak poszło?",
                            Body = "3 krótkie pytania, minuta. Twoja opinia zmienia aplikację.",
                            Url = "/app"
                        });
                    await testers.MarkRemindedAsync();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Przypomnienie o ankiecie testera nie powiodło się."); }
            try { await Task.Delay(TimeSpan.FromHours(1), ct); } catch (OperationCanceledException) { return; }
        }
    }
}
