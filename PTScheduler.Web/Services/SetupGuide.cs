using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Web.Components.Layout;
using Perms = PTScheduler.Domain.Constants.Permissions;

namespace PTScheduler.Web.Services;

/// <summary>Krok samouczka „Pierwsze kroki”.</summary>
public sealed record SetupStep(string Title, string Hint, string Href, string Icon, bool Done);

/// <summary>Stan opcji ustawień: tekst na kafelku i ton (ok — skonfigurowane, warn — do zrobienia, muted — opcjonalne).</summary>
public readonly record struct SetupStatus(string? Text, string Tone)
{
    public bool IsConfigured => Tone == "ok";
}

public sealed class SetupState
{
    public List<SetupStep> Steps { get; init; } = [];
    public Dictionary<string, SetupStatus> Status { get; init; } = [];
    public int DoneCount => Steps.Count(s => s.Done);
    public bool AllDone => Steps.All(s => s.Done);

    /// <summary>Krok, którego dotyczy ta strona (np. /admin/branding → „Dodaj logo i kolory”).</summary>
    public SetupStep? StepFor(string path) =>
        Steps.FirstOrDefault(s => path.StartsWith(s.Href.TrimStart('/'), StringComparison.OrdinalIgnoreCase));

    /// <summary>Następny niezrobiony krok (pomijając bieżący).</summary>
    public SetupStep? NextAfter(SetupStep? current) =>
        Steps.FirstOrDefault(s => !s.Done && s != current);

    public SetupStatus StatusOf(AdminMenuItem item) =>
        item.StatusKey is not null ? Status.GetValueOrDefault(item.StatusKey) : default;
}

/// <summary>
/// Liczy „Pierwsze kroki” i stan opcji ustawień — jedno źródło dla strony „Zarządzanie”
/// i paska samouczka na podstronach.
/// </summary>
public sealed class SetupGuide(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IPaymentSettingsService paymentSettings,
    EntitlementService entitlements,
    ILogger<SetupGuide> logger)
{
    public async Task<SetupState> GetAsync(string userId, AdminAccess access)
    {
        try
        {
            await using var db = dbFactory.CreateDbContext();
            var windows = await db.TrainerAvailabilities.AsNoTracking()
                .Where(a => a.TrainerUserId == userId && a.IsActive && a.DayOfWeek != null)
                .Select(a => new { a.StartTime, a.EndTime }).ToListAsync();
            var weeklyHours = windows.Sum(w => (w.EndTime - w.StartTime).TotalHours);
            var sessionTypes = await db.SessionTypes.CountAsync(t => t.IsActive);
            var packages = await db.PackageOffers.CountAsync(p => p.IsActive);
            var memberships = await db.MembershipPlans.CountAsync();
            var coupons = await db.Coupons.CountAsync(c => c.IsActive);
            var courses = await db.Courses.CountAsync();
            var users = await db.Users.CountAsync();
            var clients = await db.Clients.CountAsync();
            var trainers = await (from ur in db.UserRoles
                                  join r in db.Roles on ur.RoleId equals r.Id
                                  where r.Name == Domain.Constants.Roles.Trainer
                                  select ur.UserId).CountAsync();
            var hasLogo = await db.AppBrandings.AnyAsync(b => b.LogoPath != null && b.LogoPath != "");
            var paymentsEnabled = false;
            try { paymentsEnabled = (await paymentSettings.GetAsync()).Enabled; }
            catch (Exception ex) { logger.LogWarning(ex, "Nie udało się wczytać ustawień płatności."); }

            static SetupStatus Count(int n, string one, string few, string many, string empty = "Brak", string emptyTone = "warn") =>
                n == 0 ? new(empty, emptyTone) : new($"{n} {Pl(n, one, few, many)}", "ok");

            var status = new Dictionary<string, SetupStatus>
            {
                ["availability"] = weeklyHours > 0 ? new($"{weeklyHours:0.#} h w tygodniu", "ok") : new("Do ustawienia", "warn"),
                ["sessionTypes"] = Count(sessionTypes, "rodzaj", "rodzaje", "rodzajów", "Do ustawienia"),
                ["packages"] = Count(packages, "pakiet", "pakiety", "pakietów"),
                ["memberships"] = Count(memberships, "karnet", "karnety", "karnetów", emptyTone: "muted"),
                ["payments"] = paymentsEnabled ? new("Włączone", "ok") : new("Wyłączone", "muted"),
                ["coupons"] = Count(coupons, "aktywny", "aktywne", "aktywnych", emptyTone: "muted"),
                ["courses"] = Count(courses, "kurs", "kursy", "kursów", emptyTone: "muted"),
                ["branding"] = hasLogo ? new("Logo dodane", "ok") : new("Dodaj logo", "warn"),
                ["users"] = new($"{users} {Pl(users, "konto", "konta", "kont")}", "muted")
            };

            // Tylko kroki, które ta osoba może wykonać (asystent bez uprawnień do wyglądu nie dostanie linku do niego).
            var steps = new List<SetupStep>();
            if (access.IsOwner)
            {
                if (access.IsAdmin)
                    steps.Add(new("Dodaj logo i kolory", "Klienci zobaczą Twoją markę", "/admin/branding", "bi-palette", hasLogo));
                // Konto administratora nie prowadzi treningów (nie ma go w zapisach online) —
                // właściciel zakłada sobie profil trenera i tam ustawia godziny pracy.
                if (access.IsAdmin)
                    steps.Add(new("Utwórz swój profil trenera", "Na nim prowadzisz treningi i przyjmujesz zapisy", "/admin/users", "bi-person-badge", trainers > 0));
                else
                    steps.Add(new("Ustaw godziny pracy", "Kiedy klienci mogą się zapisać", "/trainer/availability", "bi-clock", weeklyHours > 0));
                if (access.IsAdmin)
                    steps.Add(new("Dodaj rodzaj treningu", "Np. trening personalny 60 min", "/admin/session-types", "bi-lightning-charge", sessionTypes > 0));
                if (access.Can(Perms.ManageClients))
                    steps.Add(new("Zaproś pierwszego klienta", "Wyślij link albo dodaj ręcznie", "/clients", "bi-person-plus", clients > 0));
                if (access.Can(Perms.ManagePayments) && entitlements.IsAllowed("PaymentsEnabled"))
                    steps.Add(new("Włącz płatności online", "BLIK i karta — pieniądze od razu u Ciebie", "/admin/payments", "bi-credit-card", paymentsEnabled));
            }
            return new SetupState { Steps = steps, Status = status };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nie udało się policzyć stanu ustawień.");
            return new SetupState();
        }
    }

    public static string Pl(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
