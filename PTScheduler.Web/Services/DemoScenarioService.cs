using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Demo;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using G = PTScheduler.Domain.Rules.PlanGeneratorRules;

namespace PTScheduler.Web.Services;

/// <summary>
/// Dane testowe „kilka miesięcy pracy trenera” według scenariusza (<see cref="DemoScenarios"/>). Uruchamia je wyłącznie
/// Portal (nagłówek z sekretem instancji) i tylko dla kont testowych — w aplikacji trenera nie ma do tego przycisku.
/// Dane trafiają do profilu trenera właściciela; zakres zależy od pakietu (moduł treningowy, kursy, płatności online,
/// kupony, serie, zespół, polecenia). Klienci demo mają adresy w domenie „.invalid”, więc żaden e-mail do nich nie wyjdzie.
/// Wszystko, co powstało, jest zapisane w pliku stanu — „Usuń dane demo” kasuje dokładnie to, nic więcej.
/// </summary>
public class DemoScenarioService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    EntitlementService entitlements,
    MedalService medals,
    IWebRootPathProvider webRoot,
    IAppClock clock,
    ILogger<DemoScenarioService> logger)
{
    public const string ClientPassword = "Klient123!";
    public const string TeamPassword = "Trener123!";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // Zależności dla klasy przebiegu (parametry konstruktora głównego nie są widoczne przez referencję).
    private IDbContextFactory<ApplicationDbContext> Db => dbFactory;
    private UserManager<ApplicationUser> Users => userManager;
    private EntitlementService Ent => entitlements;
    private MedalService Medals => medals;
    private IAppClock Clock => clock;

    public sealed record Result(bool Ok, string Message, Dictionary<string, int> Counts, List<string> Skipped, List<string> Logins);

    public sealed record Status(bool Loaded, string? Scenario, string? ScenarioName, DateTime? LoadedAt, Dictionary<string, int> Counts);

    /// <summary>Co powstało — do dokładnego sprzątania.</summary>
    private sealed class State
    {
        public string Scenario { get; set; } = "";
        public DateTime LoadedAt { get; set; }
        public int Seed { get; set; }
        public List<string> UserIds { get; set; } = [];
        public List<int> SessionTypeIds { get; set; } = [];
        public List<int> OfferIds { get; set; } = [];
        public List<int> CourseIds { get; set; } = [];
        public List<int> CouponIds { get; set; } = [];
        public List<int> MembershipPlanIds { get; set; } = [];
        public List<int> VoucherIds { get; set; } = [];
        public List<int> TemplatePlanIds { get; set; } = [];
        public List<int> AvailabilityIds { get; set; } = [];
        public Dictionary<string, int> Counts { get; set; } = [];
    }

    private string StatePath => PrivateStorage.SettingsFile(webRoot, "demo-state.json");

    private State? ReadState()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) : null; }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    private void WriteState(State? s)
    {
        Directory.CreateDirectory(PrivateStorage.Root(webRoot));
        if (s is null) { if (File.Exists(StatePath)) File.Delete(StatePath); return; }
        File.WriteAllText(StatePath, JsonSerializer.Serialize(s));
    }

    public Status GetStatus()
    {
        var s = ReadState();
        return s is null
            ? new Status(false, null, null, null, [])
            : new Status(true, s.Scenario, DemoScenarios.Find(s.Scenario)?.Name, s.LoadedAt, s.Counts);
    }

    // ── Ładowanie ──────────────────────────────────────────────────────────────────

    public async Task<Result> LoadAsync(string scenarioKey, int? seed = null, CancellationToken ct = default)
    {
        var scenario = DemoScenarios.Find(scenarioKey);
        if (scenario is null) return new(false, "Nieznany scenariusz.", [], [], []);
        if (!await Gate.WaitAsync(0, ct)) return new(false, "Dane demo właśnie się ładują — poczekaj chwilę.", [], [], []);
        try
        {
            if (ReadState() is not null) await ClearCoreAsync();
            var state = new State { Scenario = scenario.Key, LoadedAt = DateTime.UtcNow, Seed = seed ?? Random.Shared.Next(1, 1_000_000) };
            var run = new Run(this, scenario, state, new Random(state.Seed));
            try
            {
                await run.ExecuteAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dane demo: błąd scenariusza {Scenario} — sprzątam.", scenario.Key);
                WriteState(state);
                await ClearCoreAsync();
                return new(false, "Nie udało się załadować danych demo: " + ex.Message, [], [], []);
            }
            state.Counts = run.Counts;
            WriteState(state);
            logger.LogInformation("Dane demo: scenariusz {Scenario} załadowany ({Counts}).", scenario.Key,
                string.Join(", ", run.Counts.Select(c => $"{c.Key}={c.Value}")));
            return new(true, $"Załadowano scenariusz „{scenario.Name}”.", run.Counts, run.Skipped, run.Logins);
        }
        finally { Gate.Release(); }
    }

    public async Task<Result> ClearAsync()
    {
        if (!await Gate.WaitAsync(0)) return new(false, "Trwa inna operacja na danych demo.", [], [], []);
        try
        {
            if (ReadState() is null) return new(true, "Brak danych demo do usunięcia.", [], [], []);
            var removed = await ClearCoreAsync();
            return new(true, $"Usunięto dane demo ({removed} klientów).", new() { ["klienci"] = removed }, [], []);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Kasuje klientów demo (adresy w domenie .invalid) z całą historią oraz wszystko z pliku stanu.</summary>
    private async Task<int> ClearCoreAsync()
    {
        var state = ReadState() ?? new State();
        await using var db = await dbFactory.CreateDbContextAsync();
        var demoUsers = await db.Users.Where(u => u.Email != null && u.Email.EndsWith("@" + DemoScenarios.EmailDomain)).Select(u => u.Id).ToListAsync();
        var userIds = demoUsers.Union(state.UserIds).ToList();
        var clientIds = await db.Clients.Where(c => userIds.Contains(c.ApplicationUserId)).Select(c => c.Id).ToListAsync();

        // Kolejność zgodna z kluczami obcymi.
        await db.SessionInvitations.Where(i => clientIds.Contains(i.InvitedClientId) || clientIds.Contains(i.Session.ClientId)).ExecuteDeleteAsync();
        await db.WorkoutSetLogs.Where(s => clientIds.Contains(s.WorkoutLog!.ClientId)).ExecuteDeleteAsync();
        await db.WorkoutLogs.Where(l => clientIds.Contains(l.ClientId)).ExecuteDeleteAsync();
        await db.WorkoutComments.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.WorkoutSessions.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.SurveyResponses.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.ChatMessages.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.BodyMeasurements.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.TrainerNotes.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.ProgressPhotos.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.ClientMedals.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.ClientReviews.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.SlotWatches.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.SlotInvitations.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.AutomationLogs.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.DocumentAcceptances.Where(c => clientIds.Contains(c.ClientId)).ExecuteDeleteAsync();
        await db.Referrals.Where(r => clientIds.Contains(r.ReferrerClientId) || clientIds.Contains(r.ReferredClientId)).ExecuteDeleteAsync();
        await db.ClientContacts.Where(c => clientIds.Contains(c.Client1Id) || clientIds.Contains(c.Client2Id)).ExecuteDeleteAsync();
        await db.CouponRedemptions.Where(r => (r.UserId != null && userIds.Contains(r.UserId)) || state.CouponIds.Contains(r.CouponId)).ExecuteDeleteAsync();
        await db.Orders.Where(o => userIds.Contains(o.ApplicationUserId)).ExecuteDeleteAsync();
        await db.GiftVouchers.Where(v => state.VoucherIds.Contains(v.Id) || (v.RedeemedByClientId != null && clientIds.Contains(v.RedeemedByClientId.Value))).ExecuteDeleteAsync();
        await db.MembershipPeriods.Where(p => clientIds.Contains(p.Membership.ClientId)).ExecuteDeleteAsync();
        await db.Memberships.Where(m => clientIds.Contains(m.ClientId)).ExecuteDeleteAsync();
        await db.Sessions.Where(s => clientIds.Contains(s.ClientId)).ExecuteDeleteAsync();
        var planIds = await db.TrainingPlans.Where(p => (p.ClientId != null && clientIds.Contains(p.ClientId.Value)) || state.TemplatePlanIds.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        await db.Sessions.Where(s => s.PlanDayId != null && planIds.Contains(s.PlanDay!.PlanId)).ExecuteUpdateAsync(u => u.SetProperty(s => s.PlanDayId, (int?)null));
        await db.WorkoutLogs.Where(l => l.PlanExerciseId != null && planIds.Contains(l.PlanExercise!.PlanDay!.PlanId)).ExecuteUpdateAsync(u => u.SetProperty(l => l.PlanExerciseId, (int?)null));
        await db.PlanExercises.Where(e => planIds.Contains(e.PlanDay!.PlanId)).ExecuteDeleteAsync();
        await db.PlanDays.Where(d => planIds.Contains(d.PlanId)).ExecuteDeleteAsync();
        await db.TrainingPlans.Where(p => planIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.SessionSeries.Where(s => clientIds.Contains(s.ClientId)).ExecuteDeleteAsync();
        await db.SessionPackages.Where(p => clientIds.Contains(p.ClientId) || (p.PartnerClientId != null && clientIds.Contains(p.PartnerClientId.Value))).ExecuteDeleteAsync();
        await db.QuizAttempts.Where(a => userIds.Contains(a.ApplicationUserId)).ExecuteDeleteAsync();
        await db.LessonProgress.Where(a => userIds.Contains(a.ApplicationUserId)).ExecuteDeleteAsync();
        await db.CourseEnrollments.Where(a => userIds.Contains(a.ApplicationUserId) || state.CourseIds.Contains(a.CourseId)).ExecuteDeleteAsync();
        await db.Clients.Where(c => clientIds.Contains(c.Id)).ExecuteDeleteAsync();

        // Elementy globalne, które założyło demo.
        if (state.CourseIds.Count > 0)
        {
            await db.Orders.Where(o => o.CourseId != null && state.CourseIds.Contains(o.CourseId.Value)).ExecuteDeleteAsync();
            await db.QuizAttempts.Where(a => state.CourseIds.Contains(a.Lesson.Module.CourseId)).ExecuteDeleteAsync();
            await db.LessonProgress.Where(a => state.CourseIds.Contains(a.Lesson.Module.CourseId)).ExecuteDeleteAsync();
            await db.QuizOptions.Where(o => state.CourseIds.Contains(o.Question.Lesson.Module.CourseId)).ExecuteDeleteAsync();
            await db.QuizQuestions.Where(q => state.CourseIds.Contains(q.Lesson.Module.CourseId)).ExecuteDeleteAsync();
            await db.Lessons.Where(l => state.CourseIds.Contains(l.Module.CourseId)).ExecuteDeleteAsync();
            await db.CourseModules.Where(m => state.CourseIds.Contains(m.CourseId)).ExecuteDeleteAsync();
            await db.Courses.Where(c => state.CourseIds.Contains(c.Id)).ExecuteDeleteAsync();
        }
        await db.Coupons.Where(c => state.CouponIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.MembershipPlans.Where(p => state.MembershipPlanIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.Orders.Where(o => o.PackageOfferId != null && state.OfferIds.Contains(o.PackageOfferId.Value)).ExecuteDeleteAsync();
        await db.PackageOffers.Where(o => state.OfferIds.Contains(o.Id)).ExecuteDeleteAsync();
        await db.TrainerAvailabilities.Where(a => state.AvailabilityIds.Contains(a.Id)).ExecuteDeleteAsync();
        foreach (var typeId in state.SessionTypeIds)
            if (!await db.Sessions.AnyAsync(s => s.SessionTypeId == typeId) && !await db.SessionPackages.AnyAsync(p => p.SessionTypeId == typeId))
                await db.SessionTypes.Where(t => t.Id == typeId).ExecuteDeleteAsync();

        foreach (var id in userIds)
            if (await userManager.FindByIdAsync(id) is { } u && (DemoScenarios.IsDemoEmail(u.Email) || state.UserIds.Contains(id)))
                await userManager.DeleteAsync(u);

        WriteState(null);
        return clientIds.Count;
    }

    // ── Jeden przebieg scenariusza ─────────────────────────────────────────────────

    private enum Pay { Package, Membership, Single }

    private sealed class Persona
    {
        public Client Client = null!;
        public ApplicationUser User = null!;
        public string TrainerId = "";
        public bool Female;
        public DateOnly Start;
        public DateOnly? End;
        public bool Silent;
        public double Rate;
        public DayOfWeek[] Days = [];
        public int Hour;
        public Pay Pay;
        public bool App;
        public bool Surveys;
        public decimal Price;
        public G.Focus Focus;
        public TrainingPlan? Plan;
        public List<PlanDay> PlanDays = [];
        public List<Session> Sessions = [];
    }

    private sealed class Run(DemoScenarioService svc, DemoScenario sc, State state, Random rng)
    {
        public readonly Dictionary<string, int> Counts = [];
        public readonly List<string> Skipped = [];
        public readonly List<string> Logins = [];
        private readonly Entitlements _e = svc.Ent.Current;
        private DateOnly _today;
        private DateTime _now;
        private DateOnly _from;
        private string _ownerTrainerId = "";
        private readonly List<string> _trainers = [];
        private SessionType _personal = null!, _intro = null!, _online = null!;
        private readonly List<Persona> _people = [];
        private List<G.Candidate> _catalog = [];
        private MembershipPlan? _membershipPlan;
        private readonly HashSet<string> _usedEmails = new(StringComparer.OrdinalIgnoreCase);

        private void Count(string key, int n = 1) => Counts[key] = Counts.GetValueOrDefault(key) + n;
        private DateTime Utc(DateOnly d, int hour = 12, int minute = 0) => DateTime.SpecifyKind(d.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Utc);
        private bool Downturn(DateOnly d) => sc.Downturn && d >= _today.AddDays(-42);
        private bool Chance(double p) => rng.NextDouble() < p;

        public async Task ExecuteAsync(CancellationToken ct)
        {
            _today = svc.Clock.Today;
            _now = svc.Clock.LocalNow;
            _from = _today.AddMonths(-sc.Months);

            await TrainersAsync();
            await using var db = await svc.Db.CreateDbContextAsync(ct);
            await SetupAsync(db);
            await ClientsAsync(db);
            BuildSessions();
            foreach (var p in _people) db.Sessions.AddRange(p.Sessions);
            await db.SaveChangesAsync(ct);
            AllocatePayments(db);
            await db.SaveChangesAsync(ct);

            if (_e.TrainingPlansEnabled) await TrainingAsync(db); else Skipped.Add("plany i treningi (moduł treningowy poza pakietem)");
            await db.SaveChangesAsync(ct);
            Surveys(db);
            if (_e.BodyMeasurements) Measurements(db); else Skipped.Add("pomiary (poza pakietem)");
            NotesChatReviews(db);
            await db.SaveChangesAsync(ct);

            if (_e.CoursesEnabled && _e.MaxCourses > 0) await CoursesAsync(db); else Skipped.Add("kursy (poza pakietem)");
            if (_e.PaymentsEnabled) await CommerceAsync(db); else Skipped.Add("płatności online, karnety i vouchery (poza pakietem)");
            if (_e.Coupons) await CouponsAsync(db); else Skipped.Add("kupony (poza pakietem)");
            if (_e.ReferralProgram) Referrals(db); else Skipped.Add("polecenia (poza pakietem)");
            SlotWatches(db);
            await db.SaveChangesAsync(ct);

            // Medale z historii (bez fanfar — przy dużej historii zapisują się jako już widziane).
            if (_e.TrainingPlansEnabled)
            {
                foreach (var p in _people) await svc.Medals.SyncAsync(p.Client.Id);
                var ids = _people.Select(p => p.Client.Id).ToList();
                Counts["medale"] = await db.ClientMedals.CountAsync(m => ids.Contains(m.ClientId), ct);
            }
        }

        // ── Zespół ──────────────────────────────────────────────────────────────

        private async Task TrainersAsync()
        {
            var trainers = await svc.Users.GetUsersInRoleAsync(Roles.Trainer);
            var owner = trainers.Where(t => !DemoScenarios.IsDemoEmail(t.Email)).OrderBy(t => t.Email).FirstOrDefault();
            if (owner is null)
            {
                owner = await CreateUserAsync("Jan", "Trenerski", Roles.Trainer, TeamPassword);
                Logins.Add($"Trener: {owner.Email} / {TeamPassword}");
            }
            _ownerTrainerId = owner.Id;
            _trainers.Add(owner.Id);

            if (sc.TeamTrainers > 0)
            {
                var room = Math.Max(0, Math.Max(_e.MaxTrainers, _e.MaxSubordinates) - trainers.Count);
                var team = Math.Min(sc.TeamTrainers, room);
                (string F, string L)[] names = [("Ola", "Sprawna"), ("Kuba", "Mocny"), ("Ewa", "Zwinna")];
                for (var i = 0; i < team; i++)
                {
                    var u = await CreateUserAsync(names[i].F, names[i].L, Roles.Trainer, TeamPassword);
                    _trainers.Add(u.Id);
                    Logins.Add($"Trener w zespole: {u.Email} / {TeamPassword}");
                    Count("trenerzy w zespole");
                }
                if (team < sc.TeamTrainers) Skipped.Add($"zespół: pakiet pozwala na {team} z {sc.TeamTrainers} dodatkowych trenerów");
            }
        }

        private async Task<ApplicationUser> CreateUserAsync(string first, string last, string role, string password)
        {
            var baseEmail = Slug(first) + "." + Slug(last);
            var email = $"{baseEmail}@{DemoScenarios.EmailDomain}";
            for (var n = 2; _usedEmails.Contains(email) || await svc.Users.FindByEmailAsync(email) is not null; n++)
                email = $"{baseEmail}{n}@{DemoScenarios.EmailDomain}";
            _usedEmails.Add(email);
            var user = new ApplicationUser
            {
                UserName = email, Email = email, EmailConfirmed = true, FirstName = first, LastName = last,
                SecurityStamp = Guid.NewGuid().ToString(), MustChangePassword = false
            };
            user.PasswordHash = svc.Users.PasswordHasher.HashPassword(user, password);
            var created = await svc.Users.CreateAsync(user);
            if (!created.Succeeded) throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
            await svc.Users.AddToRoleAsync(user, role);
            state.UserIds.Add(user.Id);
            return user;
        }

        private static string Slug(string s)
        {
            var map = new Dictionary<char, char> { ['ą'] = 'a', ['ć'] = 'c', ['ę'] = 'e', ['ł'] = 'l', ['ń'] = 'n', ['ó'] = 'o', ['ś'] = 's', ['ź'] = 'z', ['ż'] = 'z' };
            return new string(s.ToLowerInvariant().Select(c => map.GetValueOrDefault(c, c)).Where(char.IsAsciiLetterOrDigit).ToArray());
        }

        // ── Ustawienia: rodzaje wizyt, grafik, oferta ──────────────────────────

        private async Task SetupAsync(ApplicationDbContext db)
        {
            _personal = await TypeAsync(db, "Trening personalny", 60, 150m);
            _intro = await TypeAsync(db, "Pierwsze spotkanie", 60, 99m);
            _online = await TypeAsync(db, "Trening online", 45, 110m);

            foreach (var t in _trainers)
            {
                if (await db.TrainerAvailabilities.AnyAsync(a => a.TrainerUserId == t)) continue;
                foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
                {
                    var a = new TrainerAvailability { TrainerUserId = t, DayOfWeek = day, StartTime = new(7, 0), EndTime = new(21, 0), IsActive = true, CreatedAt = Utc(_from) };
                    db.TrainerAvailabilities.Add(a);
                }
                db.TrainerAvailabilities.Add(new TrainerAvailability { TrainerUserId = t, DayOfWeek = DayOfWeek.Saturday, StartTime = new(8, 0), EndTime = new(13, 0), IsActive = true, CreatedAt = Utc(_from) });
            }

            if (!await db.PackageOffers.AnyAsync(o => o.IsActive))
            {
                (string N, int S, decimal P, bool F)[] offers = [("Pakiet 4 treningów", 4, 560m, false), ("Pakiet 8 treningów", 8, 1040m, true), ("Pakiet 12 treningów", 12, 1440m, false)];
                var i = 0;
                foreach (var (n, s, p, f) in offers)
                    db.PackageOffers.Add(new PackageOffer { Name = n, SessionTypeId = _personal.Id, SessionsCount = s, Price = p, Currency = "PLN", ValidDays = s * 10, IsActive = true, IsFeatured = f, SortOrder = i++, CreatedByUserId = _ownerTrainerId, CreatedAt = Utc(_from) });
            }
            await db.SaveChangesAsync();
            state.AvailabilityIds.AddRange(await db.TrainerAvailabilities.Where(a => _trainers.Contains(a.TrainerUserId) && a.CreatedAt == Utc(_from)).Select(a => a.Id).ToListAsync());
            state.OfferIds.AddRange(await db.PackageOffers.Where(o => o.CreatedAt == Utc(_from) && o.CreatedByUserId == _ownerTrainerId).Select(o => o.Id).ToListAsync());

            if (_e.TrainingPlansEnabled)
                _catalog = (await db.Exercises.AsNoTracking().Where(e => e.Visibility == ExerciseVisibility.Public)
                        .Select(e => new { e.Id, e.NamePl, e.PrimaryMuscles, e.SecondaryMuscles, e.Category, e.Equipment, e.Force, e.Mechanic, e.Level, e.Tracking }).ToListAsync())
                    .Select(e => new G.Candidate(e.Id, e.NamePl, e.PrimaryMuscles, e.SecondaryMuscles, e.Category, e.Equipment, e.Force, e.Mechanic, e.Level, e.Tracking))
                    .ToList();
        }

        private async Task<SessionType> TypeAsync(ApplicationDbContext db, string name, int minutes, decimal price)
        {
            var t = await db.SessionTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (t is not null)
            {
                // Wizyty poza pakietem liczą się po cenie rodzaju wizyty — bez niej Finanse pokazałyby „bez ceny”.
                if (t.SinglePrice is null) { t.SinglePrice = price; await db.SaveChangesAsync(); }
                return t;
            }
            t = new SessionType { Name = name, DurationMinutes = minutes, IsActive = true, SinglePrice = price };
            db.SessionTypes.Add(t);
            await db.SaveChangesAsync();
            state.SessionTypeIds.Add(t.Id);
            return t;
        }

        // ── Klienci ─────────────────────────────────────────────────────────────

        private async Task ClientsAsync(ApplicationDbContext db)
        {
            var limit = _e.MaxClients <= 0 ? 0 : _e.MaxClients - await db.Clients.CountAsync();
            var n = Math.Min(sc.Clients, Math.Max(0, limit));
            if (n < sc.Clients) Skipped.Add($"klienci: limit pakietu pozwala na {n} z {sc.Clients}");
            var span = Math.Max(14, _today.DayNumber - _from.DayNumber);
            var surnames = DemoScenarios.Surnames.OrderBy(_ => rng.Next()).ToList();

            for (var i = 0; i < n; i++)
            {
                var female = Chance(0.58);
                var first = DemoScenarios.Pick(rng, female ? DemoScenarios.FemaleNames : DemoScenarios.MaleNames);
                var last = DemoScenarios.Surname(surnames[i % surnames.Count], female);
                // Starsi klienci od początku okresu, potem stały dopływ; dwie osoby całkiem nowe.
                var offset = i == 0 ? 0 : i >= n - 2 ? span - rng.Next(2, 10) : (int)(span * Math.Pow(rng.NextDouble(), 1.6) * 0.92);
                var start = _from.AddDays(Math.Clamp(offset, 0, span - 1));
                var user = await CreateUserAsync(first, last, Roles.Client, ClientPassword);
                var p = new Persona
                {
                    User = user, Female = female, Start = start,
                    TrainerId = _trainers.Count == 1 || Chance(0.45) ? _ownerTrainerId : _trainers[1 + rng.Next(_trainers.Count - 1)],
                    Rate = Math.Clamp(sc.VisitsPerWeek * (0.45 + rng.NextDouble() * 1.1), 0.5, 4),
                    Hour = DemoScenarios.VisitHour(rng),
                    App = _e.TrainingPlansEnabled && Chance(sc.AppWorkoutRate),
                    Surveys = Chance(sc.SurveyRate),
                    Price = new[] { 120m, 130m, 140m, 150m, 160m }[rng.Next(5)]
                };
                p.Pay = _e.PaymentsEnabled && Chance(0.18) ? Pay.Membership : Chance(0.2) ? Pay.Single : Pay.Package;
                var days = Enum.GetValues<DayOfWeek>().Where(d => d != DayOfWeek.Sunday).OrderBy(_ => rng.Next()).Take(Math.Clamp((int)Math.Ceiling(p.Rate), 1, 4)).ToArray();
                p.Days = days;
                if (start < _today.AddDays(-50) && Chance(sc.ChurnRate))
                    p.End = start.AddDays(rng.Next(30, Math.Max(31, _today.DayNumber - start.DayNumber - 20)));
                if (sc.Downturn && p.End is null && Chance(0.3)) { p.Silent = true; p.End = _today.AddDays(-rng.Next(18, 35)); }

                var goal = DemoScenarios.Pick(rng, DemoScenarios.Goals);
                p.Focus = G.FocusFromGoal(goal);
                p.Client = new Client
                {
                    ApplicationUserId = user.Id, FirstName = first, LastName = last, TrainerUserId = p.TrainerId, TrainingGoal = goal,
                    DateOfBirth = new DateOnly(_today.Year - rng.Next(19, 62), rng.Next(1, 13), rng.Next(1, 28)),
                    CreatedAt = Utc(start, 9), Status = p.End is not null && !p.Silent && Chance(0.5) ? ClientStatus.Inactive : ClientStatus.Active,
                    AllowSelfBooking = Chance(0.6), TrustedForDeferredPayment = Chance(0.15), TermsAcceptedAt = Utc(start, 10)
                };
                db.Clients.Add(p.Client);
                _people.Add(p);
            }
            await db.SaveChangesAsync();
            Count("klienci", _people.Count);
            if (_people.FirstOrDefault(p => p.End is null) is { } sample)
                Logins.Add($"Klient: {sample.User.Email} / {ClientPassword}");
        }

        // ── Wizyty ──────────────────────────────────────────────────────────────

        private void BuildSessions()
        {
            var horizon = _today.AddDays(21);
            foreach (var p in _people)
            {
                // Pierwsze spotkanie.
                p.Sessions.Add(NewSession(p, p.Start, p.Hour, _intro));
                var last = p.End ?? horizon;
                var weekStart = p.Start.AddDays(1);
                for (var d = weekStart; d <= last; d = d.AddDays(1))
                {
                    if (!p.Days.Contains(d.DayOfWeek)) continue;
                    var weekly = p.Rate / p.Days.Length;
                    if (Downturn(d)) weekly *= 0.75;
                    if (!Chance(Math.Min(0.97, weekly))) continue;
                    var hour = Chance(0.15) ? Math.Clamp(p.Hour + (Chance(0.5) ? 1 : -1), 7, 20) : p.Hour;
                    p.Sessions.Add(NewSession(p, d, hour, Chance(0.07) ? _online : _personal));
                }
                if (p.Sessions.Count(s => s.Status == SessionStatus.Completed) == 0 && p.Start < _today)
                    p.Sessions[0].Status = SessionStatus.Completed;
            }

            // Serie cykliczne dla stałych klientów.
            if (_e.RecurringSessions)
                foreach (var p in _people.Where(p => p.End is null && p.Rate >= 1.2 && p.Days.Length >= 1).Take(Math.Max(1, _people.Count / 2)))
                {
                    var series = new SessionSeries
                    {
                        Client = p.Client, TrainerUserId = p.TrainerId, SessionTypeId = _personal.Id,
                        RecurrenceDays = string.Join(",", p.Days.Select(d => (int)d)), StartTime = new TimeOnly(p.Hour, 0),
                        StartDate = p.Start.AddDays(7), IsActive = true, CreatedAt = Utc(p.Start.AddDays(7), 9), CreatedByUserId = p.TrainerId
                    };
                    foreach (var s in p.Sessions.Where(s => s.SessionTypeId == _personal.Id && DateOnly.FromDateTime(s.StartTime) >= series.StartDate))
                        s.Series = series;
                    Count("serie");
                }

            // Kilka próśb „zapłacę u trenera” czekających na akceptację.
            foreach (var s in _people.Where(p => p.Pay == Pay.Single).SelectMany(p => p.Sessions).Where(s => s.Status == SessionStatus.Scheduled).Take(3))
            {
                s.AwaitingApproval = true;
                s.OffPackagePayment = OffPackageRules.PayAtTrainer;
            }
            Count("wizyty", _people.Sum(p => p.Sessions.Count));
        }

        private Session NewSession(Persona p, DateOnly d, int hour, SessionType type)
        {
            var start = d.ToDateTime(new TimeOnly(hour, rng.Next(2) == 0 ? 0 : 30));
            var status = SessionStatus.Scheduled;
            string? reason = null;
            var late = false;
            if (start < _now)
            {
                var cancel = sc.CancelRate * (Downturn(d) ? 3 : 1);
                var noShow = sc.NoShowRate * (Downturn(d) ? 3 : 1);
                var r = rng.NextDouble();
                if (r < cancel)
                {
                    status = SessionStatus.Cancelled;
                    late = rng.NextDouble() < sc.LateCancelRate / Math.Max(0.01, sc.CancelRate);
                    reason = late ? "Odwołane w ostatniej chwili" : DemoScenarios.Pick(rng, ["Choroba", "Wyjazd służbowy", "Sprawy rodzinne", "Przełożone na inny termin"]);
                }
                else if (r < cancel + noShow) { status = SessionStatus.NoShow; reason = "Klient nie przyszedł"; }
                else status = SessionStatus.Completed;
            }
            else if (Chance(0.03)) { status = SessionStatus.Cancelled; reason = "Odwołane z wyprzedzeniem"; }

            var s = new Session
            {
                Client = p.Client, TrainerUserId = p.TrainerId, SessionTypeId = type.Id, StartTime = start, Status = status,
                CancellationReason = reason, IsLateCancellation = late,
                CancelledAt = status == SessionStatus.Cancelled ? DateTime.SpecifyKind(start.AddHours(late ? -3 : -30), DateTimeKind.Utc) : null,
                CreatedAt = DateTime.SpecifyKind(start.AddDays(-rng.Next(2, 15)), DateTimeKind.Utc),
                ReminderSentAt = start < _now ? DateTime.SpecifyKind(start.AddHours(-24), DateTimeKind.Utc) : null,
                MeetingUrl = type == _online ? "https://meet.google.com/demo-" + rng.Next(100, 999) : null
            };
            return s;
        }

        // ── Płatności: pakiety, karnety, wizyty pojedyncze ──────────────────────

        private void AllocatePayments(ApplicationDbContext db)
        {
            string[] methods = ["przelew", "gotówka", "karta", "online"];
            foreach (var p in _people)
            {
                var ordered = p.Sessions.OrderBy(s => s.StartTime).ToList();
                if (p.Pay == Pay.Single)
                {
                    foreach (var s in ordered)
                    {
                        s.OffPackagePayment ??= Chance(0.7) ? OffPackageRules.PayAtTrainer : OffPackageRules.PayOnline;
                        if (s.Status is SessionStatus.Completed or SessionStatus.NoShow || s.IsLateCancellation)
                        {
                            var unpaid = Chance(sc.UnpaidRate * (Downturn(DateOnly.FromDateTime(s.StartTime)) ? 2.5 : 1));
                            if (!unpaid)
                            {
                                s.PaidAt = DateTime.SpecifyKind(s.StartTime.AddHours(rng.Next(0, 48)), DateTimeKind.Utc);
                                s.PaidVia = s.OffPackagePayment == OffPackageRules.PayOnline ? "online" : DemoScenarios.Pick(rng, methods[..3]);
                                Count("płatności za wizyty");
                            }
                            else Count("wizyty niezapłacone");
                        }
                    }
                    continue;
                }

                if (p.Pay == Pay.Membership)
                {
                    MembershipFor(db, p, ordered);
                    continue;
                }

                SessionPackage? current = null;
                var packages = 0;
                foreach (var s in ordered.Where(s => s.SessionTypeId != _intro.Id))
                {
                    if (current is null || current.UsedSessions >= current.TotalSessions)
                    {
                        var d = DateOnly.FromDateTime(s.StartTime);
                        var bought = d > _today ? _today : d.AddDays(-rng.Next(0, 4));
                        var size = new[] { 8, 10, 12, 4, 8 }[rng.Next(5)];
                        var lastOne = d >= _today.AddDays(-14);
                        var unpaid = lastOne && Chance(sc.UnpaidRate * (sc.Downturn ? 2.5 : 1) + 0.05);
                        current = new SessionPackage
                        {
                            Client = p.Client, CreatedByUserId = p.TrainerId, SessionTypeId = _personal.Id, Name = $"Pakiet {size} treningów",
                            TotalSessions = size, PricePerSession = p.Price - (size >= 10 ? 10 : 0), IsPaid = !unpaid,
                            PaidAt = unpaid ? null : Utc(bought.AddDays(rng.Next(0, 3)), 11),
                            PaidVia = unpaid ? null : DemoScenarios.Pick(rng, methods),
                            PaymentReference = unpaid ? null : $"DEMO/{bought:yyyyMM}/{rng.Next(1000, 9999)}",
                            PurchasedAt = Utc(bought, 10), ExpiresAt = Utc(bought.AddDays(size * 12), 23), Status = PackageStatus.Active
                        };
                        db.SessionPackages.Add(current);
                        packages++;
                        Count(unpaid ? "pakiety niezapłacone" : "pakiety opłacone");
                    }
                    s.Package = current;
                    if (s.Status == SessionStatus.Cancelled && !s.IsLateCancellation) { s.PackageRefunded = true; continue; }
                    current.UsedSessions++;
                }
                foreach (var pkg in db.ChangeTracker.Entries<SessionPackage>().Select(e => e.Entity).Where(x => x.Client == p.Client))
                    pkg.Status = pkg.UsedSessions >= pkg.TotalSessions ? PackageStatus.Depleted
                        : pkg.ExpiresAt < DateTime.UtcNow ? PackageStatus.Expired : PackageStatus.Active;
                // Wizyta zapoznawcza płatna osobno.
                foreach (var s in ordered.Where(s => s.SessionTypeId == _intro.Id && s.Status == SessionStatus.Completed))
                {
                    s.OffPackagePayment = OffPackageRules.PayAtTrainer;
                    s.PaidAt = DateTime.SpecifyKind(s.StartTime.AddHours(1), DateTimeKind.Utc);
                    s.PaidVia = "gotówka";
                }
            }
        }

        private void MembershipFor(ApplicationDbContext db, Persona p, List<Session> ordered)
        {
            _membershipPlan ??= NewMembershipPlan(db);
            var start = p.Start;
            var m = new Membership
            {
                Client = p.Client, Plan = _membershipPlan, Status = MembershipStatus.Active, CurrentPeriodStart = start,
                NextBillingDate = start.AddMonths(1), CreatedAt = Utc(start, 9)
            };
            db.Memberships.Add(m);
            Count("karnety");
            var end = p.End ?? _today.AddDays(21);
            for (var ps = start; ps <= end; ps = ps.AddMonths(1))
            {
                var pe = ps.AddMonths(1).AddDays(-1);
                var due = ps > _today.AddDays(-20) && Chance(sc.UnpaidRate * 2 + 0.1);
                var pkg = new SessionPackage
                {
                    Client = p.Client, CreatedByUserId = p.TrainerId, SessionTypeId = _personal.Id, Name = $"{_membershipPlan.Name} — {ps:MM.yyyy}",
                    TotalSessions = _membershipPlan.SessionsPerPeriod, PricePerSession = Math.Round(_membershipPlan.Price / _membershipPlan.SessionsPerPeriod, 2),
                    IsPaid = !due, PaidAt = due ? null : Utc(ps, 8), PaidVia = due ? null : "online", PurchasedAt = Utc(ps, 8),
                    ExpiresAt = Utc(pe, 23), Status = pe < _today ? PackageStatus.Expired : PackageStatus.Active
                };
                db.SessionPackages.Add(pkg);
                db.MembershipPeriods.Add(new MembershipPeriod
                {
                    Membership = m, PeriodStart = ps, PeriodEnd = pe, Amount = _membershipPlan.Price, DueDate = ps,
                    Status = due ? MembershipPeriodStatus.Due : MembershipPeriodStatus.Paid, PaidAt = due ? null : Utc(ps, 8),
                    PaymentReference = due ? null : $"KARNET/{ps:yyyyMM}/{p.Client.FirstName[..1]}{rng.Next(100, 999)}", Package = pkg
                });
                foreach (var s in ordered.Where(s => DateOnly.FromDateTime(s.StartTime) >= ps && DateOnly.FromDateTime(s.StartTime) <= pe && s.SessionTypeId != _intro.Id))
                {
                    s.Package = pkg;
                    if (s.Status == SessionStatus.Cancelled && !s.IsLateCancellation) { s.PackageRefunded = true; continue; }
                    pkg.UsedSessions = Math.Min(pkg.TotalSessions, pkg.UsedSessions + 1);
                }
                if (pkg.UsedSessions >= pkg.TotalSessions) pkg.Status = PackageStatus.Depleted;
                if (due && ps <= _today) m.Status = MembershipStatus.PastDue;
                m.CurrentPeriodStart = ps;
                m.NextBillingDate = pe.AddDays(1);
            }
            if (p.End is not null) { m.Status = MembershipStatus.Cancelled; m.CancelledAt = Utc(p.End.Value, 12); }
        }

        private MembershipPlan NewMembershipPlan(ApplicationDbContext db)
        {
            var plan = new MembershipPlan
            {
                Name = "Karnet 8 treningów / miesiąc", Description = "Stała cena co miesiąc, niewykorzystane treningi przechodzą na kolejny miesiąc.",
                SessionTypeId = _personal.Id, SessionsPerPeriod = 8, PeriodMonths = 1, Price = 960m, Currency = "PLN", CarryOverUnused = true,
                AvailableInShop = true, IsActive = true, CreatedByUserId = _ownerTrainerId, CreatedAt = Utc(_from)
            };
            db.MembershipPlans.Add(plan);
            db.SaveChanges();
            state.MembershipPlanIds.Add(plan.Id);
            return plan;
        }

        // ── Plany i treningi ────────────────────────────────────────────────────

        private async Task TrainingAsync(ApplicationDbContext db)
        {
            if (_catalog.Count < 20) { Skipped.Add("plany (brak katalogu ćwiczeń)"); return; }
            var history = new Dictionary<int, G.History>();

            // Szablony trenera.
            foreach (var (name, opt) in new[] { ("Szablon: całe ciało 2× w tygodniu", new G.Options(2, 60, G.Focus.General, G.Kit.Gym)),
                                                ("Szablon: redukcja 3×", new G.Options(3, 45, G.Focus.FatLoss, G.Kit.Gym)),
                                                ("Szablon: trening w domu", new G.Options(3, 30, G.Focus.General, G.Kit.Bodyweight)) })
            {
                var t = SavePlan(db, G.Build(opt, _catalog, history, _ => null), _ownerTrainerId, null, name, template: true);
                await db.SaveChangesAsync();
                state.TemplatePlanIds.Add(t.Id);
            }

            foreach (var p in _people)
            {
                var days = Math.Clamp((int)Math.Round(p.Rate + (p.App ? 1 : 0)), 1, 4);
                var kit = p.App && Chance(0.3) ? G.Kit.Dumbbells : G.Kit.Gym;
                var result = G.Build(new G.Options(days, Chance(0.3) ? 45 : 60, p.Focus, kit), _catalog, history, _ => null);
                if (result.Days.Count == 0) continue;
                p.Plan = SavePlan(db, result, p.TrainerId, p.Client, null, template: false);
                p.Plan.CreatedAt = Utc(p.Start.AddDays(2), 10);
                p.PlanDays = p.Plan.Days.OrderBy(d => d.Order).ToList();
                Count("plany");
            }
            await db.SaveChangesAsync();

            // Treningi: z trenerem (ukończone wizyty) i samodzielne z aplikacji; ciężary rosną, w gorszym okresie stoją.
            var exById = _catalog.ToDictionary(c => c.Id);
            foreach (var p in _people.Where(p => p.Plan is not null))
            {
                var dates = p.Sessions.Where(s => s.Status == SessionStatus.Completed).Select(s => (Date: DateOnly.FromDateTime(s.StartTime), Session: (Session?)s)).ToList();
                if (p.App)
                    for (var d = p.Start.AddDays(3); d < _today && d <= (p.End ?? _today); d = d.AddDays(rng.Next(4, 9)))
                        if (dates.All(x => x.Date != d) && !(p.Silent && d > p.End)) dates.Add((d, null));
                dates = dates.OrderBy(x => x.Date).ToList();
                var weights = new Dictionary<int, decimal>();
                var dayIndex = 0;
                foreach (var (date, session) in dates)
                {
                    var day = p.PlanDays[dayIndex++ % p.PlanDays.Count];
                    if (session is not null) session.PlanDay = day;
                    var weeks = (date.DayNumber - p.Start.DayNumber) / 7.0;
                    foreach (var pe in day.Exercises.OrderBy(e => e.Order))
                    {
                        exById.TryGetValue(pe.ExerciseId, out var cand);
                        var tracking = cand?.Tracking ?? ExerciseTracking.WeightReps;
                        var log = new WorkoutLog { ClientId = p.Client.Id, PlanExerciseId = pe.Id, ExerciseId = pe.ExerciseId, WorkoutDate = date, CreatedAt = Utc(date, p.Hour + 1) };
                        if (!weights.TryGetValue(pe.ExerciseId, out var w)) w = StartWeight(cand, p.Female);
                        if (!Downturn(date) && weeks > 0 && Chance(0.35)) w += ProgressionRules.Increment(w, cand?.PrimaryMuscles, cand?.Mechanic, cand?.Equipment) / 2m;
                        w = Math.Round(w * 2, MidpointRounding.AwayFromZero) / 2;
                        weights[pe.ExerciseId] = w;
                        for (var set = 1; set <= Math.Max(1, pe.Sets); set++)
                        {
                            var reps = tracking == ExerciseTracking.Time ? 0 : Math.Max(4, (ProgressionRules.ParseReps(pe.Reps)?.Max ?? 10) - rng.Next(0, 3) - (set > 2 ? 1 : 0));
                            log.Sets.Add(new WorkoutSetLog
                            {
                                SetNumber = set, Reps = reps,
                                WeightKg = tracking is ExerciseTracking.WeightReps or ExerciseTracking.WeightDistance ? w : 0,
                                DurationSeconds = tracking == ExerciseTracking.Time ? (pe.TargetDurationSeconds ?? 40) + rng.Next(-5, 15) : null
                            });
                        }
                        db.WorkoutLogs.Add(log);
                        Count("wpisy treningowe");
                    }
                    if (Chance(0.12))
                        db.WorkoutComments.Add(new WorkoutComment
                        {
                            ClientId = p.Client.Id, WorkoutDate = date, AuthorUserId = Chance(0.6) ? p.TrainerId : p.User.Id, ByTrainer = true,
                            Text = Chance(0.5) ? "Świetna technika, następnym razem dokładamy 2,5 kg." : "Pamiętaj o pełnym zakresie ruchu w przysiadzie.",
                            CreatedAt = Utc(date, 21), ReadAt = Utc(date.AddDays(1), 8)
                        });
                    Count(session is null ? "treningi w aplikacji" : "treningi z trenerem z planem");
                }
                foreach (var pe in p.PlanDays.SelectMany(d => d.Exercises)) if (weights.TryGetValue(pe.ExerciseId, out var tw) && tw > 0) pe.TargetWeightKg = tw;
            }
        }

        private TrainingPlan SavePlan(ApplicationDbContext db, G.Result r, string trainerId, Client? client, string? name, bool template)
        {
            var plan = new TrainingPlan
            {
                TrainerUserId = trainerId, Client = client, Name = name ?? r.Name, Purpose = r.Purpose, IsTemplate = template,
                Notes = template ? "Szablon do szybkiego przypisania." : null, CreatedAt = Utc(_from, 9)
            };
            var order = 0;
            foreach (var d in r.Days)
            {
                var day = new PlanDay { Plan = plan, Order = order++, Label = d.Label };
                var eo = 0;
                foreach (var x in d.Exercises)
                    day.Exercises.Add(new PlanExercise { ExerciseId = x.ExerciseId, Order = eo++, Sets = x.Sets, Reps = x.Reps, TargetDurationSeconds = x.DurationSeconds, RestSeconds = x.RestSeconds });
                plan.Days.Add(day);
            }
            db.TrainingPlans.Add(plan);
            return plan;
        }

        private decimal StartWeight(G.Candidate? c, bool female)
        {
            var eq = c?.Equipment ?? "";
            var compound = string.Equals(c?.Mechanic, "compound", StringComparison.OrdinalIgnoreCase);
            var scale = female ? 0.6m : 1m;
            decimal w = eq switch
            {
                "barbell" => compound ? 40 : 20,
                "dumbbell" or "kettlebells" => compound ? 14 : 8,
                "machine" or "cable" => compound ? 35 : 20,
                "e-z curl bar" => 15,
                _ => 0
            };
            return w * scale * (0.8m + (decimal)rng.NextDouble() * 0.5m);
        }

        // ── Ankiety ─────────────────────────────────────────────────────────────

        private void Surveys(ApplicationDbContext db)
        {
            var intake = SurveyDefaults.HealthIntake();
            var post = SurveyDefaults.PostWorkout();
            foreach (var p in _people)
            {
                var injured = Chance(0.18);
                var inputs = new Dictionary<string, SurveyAnswerInput>();
                foreach (var q in intake.Questions)
                {
                    inputs[q.Key] = q.Type switch
                    {
                        SurveyQuestionType.YesNo => new SurveyAnswerInput { Bool = q.MustBeYes || (q.FlagOnYes && q.Key != "pregnancy" && Chance(0.04)) },
                        SurveyQuestionType.Scale or SurveyQuestionType.Number => new SurveyAnswerInput { Number = rng.Next(Math.Max(q.Min, 3), Math.Min(q.Max, 8) + 1) },
                        SurveyQuestionType.SingleChoice when q.Options.Count > 0 => new SurveyAnswerInput { Choices = [DemoScenarios.Pick(rng, q.Options)] },
                        SurveyQuestionType.MultiChoice when q.Options.Count > 0 => new SurveyAnswerInput { Choices = q.Options.OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)).ToList() },
                        _ => new SurveyAnswerInput { Text = q.Key switch
                        {
                            "injuries" => injured ? DemoScenarios.Pick(rng, ["Kolano lewe — skręcenie 2 lata temu", "Bark prawy, przeciążenie", "Ból dolnego odcinka pleców przy długim siedzeniu"]) : null,
                            "emergency_contact" => "Osoba bliska, tel. w dokumentach",
                            _ => null
                        } }
                    };
                }
                var (_, intakeAnswers) = SurveyEvaluator.Evaluate(intake.Questions, inputs);
                db.SurveyResponses.Add(new SurveyResponse
                {
                    Kind = SurveyKind.HealthIntake, Client = p.Client, SubmittedAt = Utc(p.Start, 8), AnswersJson = SurveyJson.Serialize(intakeAnswers),
                    FlagCount = intakeAnswers.Count(a => a.Flagged), HealthDataConsentAt = Utc(p.Start, 8),
                    ReviewedAt = Utc(p.Start, 12), ReviewedByUserId = p.TrainerId
                });
                Count("ankiety zdrowotne");

                if (!p.Surveys) continue;
                var workoutDays = p.Sessions.Where(s => s.Status == SessionStatus.Completed).Select(s => DateOnly.FromDateTime(s.StartTime)).Distinct();
                foreach (var d in workoutDays.Where(_ => Chance(0.7)))
                {
                    var bad = Downturn(d);
                    var pain = Chance(bad ? 0.2 : 0.05);
                    var answers = new Dictionary<string, SurveyAnswerInput>
                    {
                        [SurveyKeys.Rpe] = new() { Number = Math.Clamp(rng.Next(5, 9) + (bad ? 1 : 0), 0, 10) },
                        [SurveyKeys.Duration] = new() { Number = rng.Next(45, 75) },
                        [SurveyKeys.Energy] = new() { Number = Math.Clamp(rng.Next(3, 6) - (bad ? 2 : 0), 1, 5) },
                        [SurveyKeys.Sleep] = new() { Number = Math.Clamp(rng.Next(3, 6) - (bad ? 1 : 0), 1, 5) },
                        [SurveyKeys.Soreness] = new() { Number = Math.Clamp(rng.Next(1, 4) + (bad ? 1 : 0), 1, 5) },
                        [SurveyKeys.Pain] = new() { Bool = pain },
                        [SurveyKeys.PainLocation] = new() { Text = pain ? DemoScenarios.Pick(rng, ["kolano", "bark", "lędźwie", "nadgarstek"]) : null },
                        [SurveyKeys.Satisfaction] = new() { Number = Math.Clamp(rng.Next(4, 6) - (bad ? 1 : 0), 1, 5) },
                        [SurveyKeys.Comment] = new() { Text = Chance(0.25) ? DemoScenarios.Pick(rng, DemoScenarios.ClientComments) : null }
                    };
                    var (_, a) = SurveyEvaluator.Evaluate(post.Questions, answers);
                    db.SurveyResponses.Add(new SurveyResponse
                    {
                        Kind = SurveyKind.PostWorkout, Client = p.Client, WorkoutDate = d, SubmittedAt = Utc(d, 21), AnswersJson = SurveyJson.Serialize(a),
                        FlagCount = a.Count(x => x.Flagged), ReviewedAt = d < _today.AddDays(-3) ? Utc(d.AddDays(1), 9) : null,
                        ReviewedByUserId = d < _today.AddDays(-3) ? p.TrainerId : null
                    });
                    Count("ankiety po treningu");
                }
            }
        }

        // ── Pomiary, notatki, czat, opinie ──────────────────────────────────────

        private void Measurements(ApplicationDbContext db)
        {
            foreach (var p in _people.Where(_ => Chance(0.7)))
            {
                var weight = (decimal)(p.Female ? rng.Next(56, 86) : rng.Next(72, 108));
                var fat = (decimal)(p.Female ? rng.Next(24, 34) : rng.Next(16, 28));
                var waist = weight * 0.95m;
                var trend = p.Focus switch { G.Focus.FatLoss => -0.9m, G.Focus.Hypertrophy => 0.35m, _ => -0.3m };
                for (var d = p.Start; d <= (p.End ?? _today); d = d.AddDays(rng.Next(25, 35)))
                {
                    db.BodyMeasurements.Add(new BodyMeasurement
                    {
                        Client = p.Client, MeasurementDate = d, WeightKg = Math.Round(weight, 1), BodyFatPercent = Math.Round(fat, 1),
                        WaistCm = Math.Round(waist, 1), HipsCm = Math.Round(waist * 1.12m, 1), ChestCm = Math.Round(waist * 1.08m, 1),
                        ThighCm = Math.Round(weight * 0.7m, 1), ArmCm = Math.Round(weight * 0.38m, 1), AddedByClient = Chance(0.3)
                    });
                    weight += trend + (decimal)(rng.NextDouble() - 0.5);
                    fat += trend / 3 + (decimal)(rng.NextDouble() - 0.5) / 3;
                    waist += trend * 0.8m;
                    Count("pomiary");
                }
            }
        }

        private void NotesChatReviews(ApplicationDbContext db)
        {
            foreach (var p in _people)
            {
                foreach (var _ in Enumerable.Range(0, rng.Next(0, 4)))
                {
                    var d = p.Start.AddDays(rng.Next(0, Math.Max(1, (p.End ?? _today).DayNumber - p.Start.DayNumber)));
                    db.TrainerNotes.Add(new TrainerNote { Client = p.Client, TrainerUserId = p.TrainerId, Content = DemoScenarios.Pick(rng, DemoScenarios.TrainerNotes), CreatedAt = Utc(d, 19) });
                    Count("notatki");
                }
                if (Chance(0.55))
                {
                    var d = (p.End ?? _today).AddDays(-rng.Next(0, 20));
                    for (var i = 0; i < rng.Next(2, 7); i++)
                    {
                        var fromStaff = i % 2 == 1;
                        var at = Utc(d.AddDays(i / 2), 8 + i, rng.Next(0, 59));
                        db.ChatMessages.Add(new ChatMessage
                        {
                            Client = p.Client, SenderUserId = fromStaff ? p.TrainerId : p.User.Id, FromStaff = fromStaff,
                            Body = DemoScenarios.Pick(rng, fromStaff ? DemoScenarios.ChatFromTrainer : DemoScenarios.ChatFromClient), SentAt = at,
                            ReadAt = d < _today.AddDays(-1) || fromStaff ? at.AddMinutes(rng.Next(5, 240)) : null
                        });
                        Count("wiadomości");
                    }
                }
                if (p.End is null && p.Start < _today.AddDays(-45) && Chance(0.3))
                {
                    var rating = Chance(0.85) ? 5 : 4;
                    db.ClientReviews.Add(new ClientReview
                    {
                        Client = p.Client, Rating = rating, Text = DemoScenarios.Pick(rng, DemoScenarios.Reviews), DisplayName = $"{p.Client.FirstName} {p.Client.LastName[..1]}.",
                        PublishConsent = true, IsPublished = Chance(0.7), CreatedAt = Utc(_today.AddDays(-rng.Next(3, 40)), 20), UpdatedAt = Utc(_today.AddDays(-2), 20)
                    });
                    Count("opinie");
                }
            }
        }

        // ── Kursy ───────────────────────────────────────────────────────────────

        private async Task CoursesAsync(ApplicationDbContext db)
        {
            (string T, string D, decimal Price, string[] Modules)[] defs =
            [
                ("Zdrowy kręgosłup w 4 tygodnie", "Krótkie codzienne sesje mobilności dla osób pracujących przy biurku.", 149m, ["Tydzień 1 — oddech i postawa", "Tydzień 2 — mobilność bioder", "Tydzień 3 — core", "Tydzień 4 — nawyki"]),
                ("Trening w domu bez sprzętu", "Plan 6 tygodni z ćwiczeniami masą ciała i progresją.", 99m, ["Podstawy", "Siła", "Kondycja"]),
                ("Odżywianie dla zabieganych", "Proste zasady, listy zakupów i przepisy na cały tydzień.", 79m, ["Zasady", "Planowanie posiłków"])
            ];
            var max = Math.Min(defs.Length, _e.MaxCourses);
            var videos = new[] { "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube.com/watch?v=ml6cT4AZdqI", "https://www.youtube.com/watch?v=UItWltVZZmE" };
            var courses = new List<Course>();
            for (var i = 0; i < max; i++)
            {
                var (t, d, price, modules) = defs[i];
                var c = new Course
                {
                    Title = t, Description = d, Price = price, IsPublished = true, Level = "Początkujący", Author = "Twój trener", DurationText = $"{modules.Length} tygodnie",
                    DefaultAccessType = CourseAccessType.Lifetime, SortOrder = i, CreatedAt = Utc(_from.AddDays(10 + i * 20), 10)
                };
                var mo = 0;
                foreach (var mt in modules)
                {
                    var m = new CourseModule { Course = c, Title = mt, SortOrder = mo++ };
                    for (var l = 0; l < 3; l++)
                    {
                        var lesson = new Lesson
                        {
                            Module = m, Title = $"{mt.Split('—').Last().Trim()} — lekcja {l + 1}", SortOrder = l, VideoUrl = videos[(mo + l) % videos.Length],
                            ContentHtml = PTScheduler.Application.Training.LessonText.ToHtml("Obejrzyj film i powtórz ćwiczenia 2 razy.\n\nNotuj, jak się czujesz — porozmawiamy o tym na treningu.")
                        };
                        db.Lessons.Add(lesson);
                    }
                    db.CourseModules.Add(m);
                }
                db.Courses.Add(c);
                courses.Add(c);
                Count("kursy");
            }
            await db.SaveChangesAsync();
            state.CourseIds.AddRange(courses.Select(c => c.Id));

            var lessons = await db.Lessons.Where(l => state.CourseIds.Contains(l.Module.CourseId)).Select(l => new { l.Id, l.Module.CourseId }).ToListAsync();
            foreach (var p in _people.Where(_ => Chance(0.35)))
            {
                var course = DemoScenarios.Pick(rng, courses);
                var granted = p.Start.AddDays(rng.Next(0, 30));
                if (granted > _today) granted = _today;
                var purchase = _e.PaymentsEnabled && Chance(0.6);
                db.CourseEnrollments.Add(new CourseEnrollment
                {
                    Course = course, ApplicationUserId = p.User.Id, AccessType = CourseAccessType.Lifetime, Source = purchase ? EnrollmentSource.Purchase : EnrollmentSource.Manual,
                    GrantedAt = Utc(granted, 12), GrantedByUserId = purchase ? null : p.TrainerId, Notes = purchase ? null : "Gratis do pakietu"
                });
                if (purchase)
                    db.Orders.Add(new Order
                    {
                        ApplicationUserId = p.User.Id, Kind = OrderKind.Course, Provider = "PayU", Course = course, ExtOrderId = "DEMO-" + Guid.NewGuid().ToString("N")[..12],
                        Amount = course.Price, Currency = "PLN", Status = OrderStatus.Paid, Description = course.Title, CreatedAt = Utc(granted, 11), PaidAt = Utc(granted, 11, 5)
                    });
                var mine = lessons.Where(l => l.CourseId == course.Id).ToList();
                foreach (var l in mine.Take(rng.Next(0, mine.Count + 1)))
                    db.LessonProgress.Add(new LessonProgress { ApplicationUserId = p.User.Id, LessonId = l.Id, CompletedAt = Utc(granted.AddDays(rng.Next(0, 20)), 20) });
                Count("zapisy na kursy");
            }
        }

        // ── Sprzedaż online, vouchery, kupony, polecenia ────────────────────────

        private async Task CommerceAsync(ApplicationDbContext db)
        {
            var offers = await db.PackageOffers.Where(o => o.IsActive).ToListAsync();
            if (offers.Count > 0)
                foreach (var p in _people.Where(p => p.Pay == Pay.Package && Chance(0.4)))
                {
                    var o = DemoScenarios.Pick(rng, offers);
                    var d = p.Start.AddDays(rng.Next(0, Math.Max(1, _today.DayNumber - p.Start.DayNumber)));
                    var status = Chance(0.88) ? OrderStatus.Paid : DemoScenarios.Pick(rng, [OrderStatus.Pending, OrderStatus.Failed, OrderStatus.Canceled]);
                    db.Orders.Add(new Order
                    {
                        ApplicationUserId = p.User.Id, Kind = OrderKind.Package, Provider = DemoScenarios.Pick(rng, ["PayU", "Przelewy24", "Autopay"]), PackageOfferId = o.Id,
                        ExtOrderId = "DEMO-" + Guid.NewGuid().ToString("N")[..12], Amount = o.Price, Currency = "PLN", Status = status, Description = o.Name,
                        CreatedAt = Utc(d, 18), PaidAt = status == OrderStatus.Paid ? Utc(d, 18, 3) : null,
                        InvoiceNumber = status == OrderStatus.Paid && Chance(0.3) ? $"FV/DEMO/{d:yyyy/MM}/{rng.Next(1, 300)}" : null,
                        InvoiceIssuedAt = null
                    });
                    Count(status == OrderStatus.Paid ? "zamówienia opłacone" : "zamówienia nieudane lub w toku");
                }

            var buyer = _people.FirstOrDefault(p => p.End is null);
            (string Code, GiftVoucherStatus Status, int? Redeemer)[] vouchers =
                [("DEMO-PREZENT-1", GiftVoucherStatus.Active, null), ("DEMO-PREZENT-2", GiftVoucherStatus.Redeemed, _people.LastOrDefault()?.Client.Id), ("DEMO-PREZENT-3", GiftVoucherStatus.Pending, null)];
            foreach (var (code, status, redeemer) in vouchers)
            {
                var v = new GiftVoucher
                {
                    Code = code + "-" + rng.Next(100, 999), Kind = GiftVoucherKind.Amount, Status = status, Value = 300m, Currency = "PLN", Title = "Voucher na treningi",
                    RecipientName = "Ola", FromName = buyer?.Client.FirstName, Message = "Wszystkiego najlepszego!", BuyerUserId = buyer?.User.Id,
                    CreatedAt = Utc(_today.AddDays(-rng.Next(5, 60)), 15), PaidAt = status == GiftVoucherStatus.Pending ? null : Utc(_today.AddDays(-4), 15),
                    ExpiresAt = Utc(_today.AddMonths(6), 23), RedeemedAt = status == GiftVoucherStatus.Redeemed ? Utc(_today.AddDays(-2), 10) : null, RedeemedByClientId = redeemer
                };
                db.GiftVouchers.Add(v);
                await db.SaveChangesAsync();
                state.VoucherIds.Add(v.Id);
                Count("vouchery");
            }
        }

        private async Task CouponsAsync(ApplicationDbContext db)
        {
            var coupons = new[]
            {
                new Coupon { Code = "DEMO-WIOSNA20", Description = "Wiosenna promocja", DiscountType = "percent", DiscountValue = 20, ValidFrom = Utc(_today.AddDays(-30)), ValidUntil = Utc(_today.AddDays(30)), MaxUses = 50, MaxUsesPerUser = 1, Scope = "all", IsActive = true, CreatedAt = Utc(_today.AddDays(-30)) },
                new Coupon { Code = "DEMO-POWROT50", Description = "Wróć do treningów — 50 zł mniej", DiscountType = "amount", DiscountValue = 50, ValidUntil = Utc(_today.AddDays(14)), MaxUses = 20, MaxUsesPerUser = 1, Scope = "all", IsActive = true, CreatedAt = Utc(_today.AddDays(-10)) }
            };
            db.Coupons.AddRange(coupons);
            await db.SaveChangesAsync();
            state.CouponIds.AddRange(coupons.Select(c => c.Id));
            foreach (var p in _people.Where(_ => Chance(0.15)))
            {
                var c = DemoScenarios.Pick(rng, coupons);
                var original = 1040m;
                var discount = c.DiscountType == "percent" ? original * c.DiscountValue / 100 : c.DiscountValue;
                db.CouponRedemptions.Add(new CouponRedemption { CouponId = c.Id, UserId = p.User.Id, UserEmail = p.User.Email, OriginalAmount = original, DiscountAmount = discount, FinalAmount = original - discount, TargetType = "package", RedeemedAt = Utc(_today.AddDays(-rng.Next(1, 25)), 17) });
                c.UsedCount++;
                Count("użyte kupony");
            }
        }

        private void Referrals(ApplicationDbContext db)
        {
            var actives = _people.Where(p => p.End is null).ToList();
            for (var i = 0; i + 1 < actives.Count && i < 6; i += 2)
            {
                var referred = actives[i + 1];
                db.Referrals.Add(new Referral
                {
                    ReferrerClient = actives[i].Client, ReferredClient = referred.Client, Status = i % 4 == 0 ? ReferralStatus.Rewarded : ReferralStatus.Pending,
                    CreatedAt = Utc(referred.Start, 9), RewardedAt = i % 4 == 0 ? Utc(referred.Start.AddDays(14), 9) : null,
                    RewardDescription = i % 4 == 0 ? "1 trening gratis" : null
                });
                Count("polecenia");
            }
        }

        private void SlotWatches(ApplicationDbContext db)
        {
            foreach (var p in _people.Where(p => p.End is null).Take(2))
            {
                db.SlotWatches.Add(new SlotWatch { Client = p.Client, Days = 0b0011111, FromTime = new(17, 0), ToTime = new(20, 0), IsActive = true, CreatedAt = Utc(_today.AddDays(-5), 12) });
                Count("czekający na wolny termin");
            }
        }
    }
}
