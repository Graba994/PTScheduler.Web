using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class DemoDataService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager) : IDemoDataService
{
    private const string TrainerEmail = "jan.kowalski@demo.pl";
    private const string AdminResetEmail = "root@admin.local";

    // ─── PUBLIC API ────────────────────────────────────────────────────────────

    public async Task<DemoSeedResult> SeedAsync()
    {
        if (await userManager.FindByEmailAsync(TrainerEmail) is not null)
            return new DemoSeedResult { AlreadySeeded = true };

        var result = new DemoSeedResult();

        // 1. Users
        var trainer = await CreateUserAsync("Jan", "Kowalski", TrainerEmail, "trener1", Roles.Trainer);
        result.Users.Add(new() { Role = "Trener", FullName = "Jan Kowalski", Email = TrainerEmail, Password = "trener1" });

        var manager = await CreateUserAsync("Anna", "Wiśniewska", "anna@demo.pl", "menedzer1", Roles.Subordinate);
        manager.SupervisorId = trainer.Id;
        await userManager.UpdateAsync(manager);
        result.Users.Add(new() { Role = "Menadżer", FullName = "Anna Wiśniewska", Email = "anna@demo.pl", Password = "menedzer1" });

        // 2. Session types (get existing or create)
        var st1 = await GetOrCreateSessionTypeAsync("Trening personalny", 60);
        var st2 = await GetOrCreateSessionTypeAsync("Pilates", 45);
        var st3 = await GetOrCreateSessionTypeAsync("Stretching", 30);

        // 3. Trainer availability
        AddAvailability(trainer.Id, DayOfWeek.Monday,    new TimeOnly(8, 0), new TimeOnly(18, 0));
        AddAvailability(trainer.Id, DayOfWeek.Tuesday,   new TimeOnly(8, 0), new TimeOnly(18, 0));
        AddAvailability(trainer.Id, DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(18, 0));
        AddAvailability(trainer.Id, DayOfWeek.Thursday,  new TimeOnly(8, 0), new TimeOnly(18, 0));
        AddAvailability(trainer.Id, DayOfWeek.Friday,    new TimeOnly(8, 0), new TimeOnly(18, 0));
        AddAvailability(trainer.Id, DayOfWeek.Saturday,  new TimeOnly(9, 0), new TimeOnly(13, 0));
        db.TrainerConfigs.Add(new TrainerConfig
        {
            TrainerUserId = trainer.Id,
            BreakAfterSessionMinutes = 15,
            SlotGranularityMinutes = 30,
            AllowClientsDiscoverPeers = true
        });
        await db.SaveChangesAsync();

        // 4. Clients
        var (c1, u1) = await CreateClientAsync("Marek",     "Nowak",      "marek@demo.pl",      "klient1", trainer.Id, allowSelfBooking: true);
        var (c2, u2) = await CreateClientAsync("Katarzyna", "Zielińska",  "katarzyna@demo.pl",  "klient1", trainer.Id, allowSelfBooking: true);
        var (c3, u3) = await CreateClientAsync("Piotr",     "Wiśniewski", "piotr@demo.pl",      "klient1", trainer.Id, allowSelfBooking: false);
        var (c4, u4) = await CreateClientAsync("Alicja",    "Kowalska",   "alicja@demo.pl",     "klient1", trainer.Id, allowSelfBooking: false);

        result.Users.Add(new() { Role = "Klient", FullName = "Marek Nowak",       Email = "marek@demo.pl",      Password = "klient1" });
        result.Users.Add(new() { Role = "Klient", FullName = "Katarzyna Zielińska", Email = "katarzyna@demo.pl", Password = "klient1" });
        result.Users.Add(new() { Role = "Klient", FullName = "Piotr Wiśniewski",  Email = "piotr@demo.pl",      Password = "klient1" });
        result.Users.Add(new() { Role = "Klient", FullName = "Alicja Kowalska",   Email = "alicja@demo.pl",     Password = "klient1" });

        // 5. Packages
        var pkg1 = new SessionPackage
        {
            ClientId = c1.Id, CreatedByUserId = trainer.Id,
            Name = "Pakiet Start", SessionTypeId = st1.Id,
            TotalSessions = 10, UsedSessions = 4,
            PricePerSession = 120m, IsPaid = true,
            PaidAt = DateTime.Now.AddDays(-50),
            PurchasedAt = DateTime.Now.AddDays(-50),
            Status = PackageStatus.Active
        };
        var pkg2 = new SessionPackage
        {
            ClientId = c2.Id, CreatedByUserId = trainer.Id,
            Name = "Pakiet Pro", SessionTypeId = st2.Id,
            TotalSessions = 20, UsedSessions = 8,
            PricePerSession = 100m, IsPaid = true,
            PaidAt = DateTime.Now.AddDays(-60),
            PurchasedAt = DateTime.Now.AddDays(-60),
            Status = PackageStatus.Active
        };
        var pkg3 = new SessionPackage
        {
            ClientId = c3.Id, CreatedByUserId = trainer.Id,
            Name = "Pakiet Standard", SessionTypeId = st1.Id,
            TotalSessions = 10, UsedSessions = 10,
            PricePerSession = 120m, IsPaid = true,
            PaidAt = DateTime.Now.AddDays(-90),
            PurchasedAt = DateTime.Now.AddDays(-90),
            ExpiresAt = DateTime.Now.AddDays(-10),
            Status = PackageStatus.Expired
        };
        db.SessionPackages.AddRange(pkg1, pkg2, pkg3);
        await db.SaveChangesAsync();

        // 6. Sessions — Marek (Trening personalny, co poniedziałek)
        AddSession(c1.Id, trainer.Id, st1.Id, Past(DayOfWeek.Monday, 6), SessionStatus.Completed, pkg1.Id);
        AddSession(c1.Id, trainer.Id, st1.Id, Past(DayOfWeek.Monday, 5), SessionStatus.Completed, pkg1.Id);
        AddSession(c1.Id, trainer.Id, st1.Id, Past(DayOfWeek.Monday, 4), SessionStatus.Completed, pkg1.Id);
        AddSession(c1.Id, trainer.Id, st1.Id, Past(DayOfWeek.Monday, 3), SessionStatus.NoShow,    pkg1.Id,
            cancellationReason: "Klient nie dał znaku życia");
        AddSession(c1.Id, trainer.Id, st1.Id, Future(DayOfWeek.Monday, 1), SessionStatus.Scheduled, pkg1.Id);
        AddSession(c1.Id, trainer.Id, st1.Id, Future(DayOfWeek.Monday, 2), SessionStatus.Scheduled, pkg1.Id);

        // Sessions — Katarzyna (Pilates, co wtorek)
        AddSession(c2.Id, trainer.Id, st2.Id, Past(DayOfWeek.Tuesday, 5), SessionStatus.Completed, pkg2.Id);
        AddSession(c2.Id, trainer.Id, st2.Id, Past(DayOfWeek.Tuesday, 4), SessionStatus.Completed, pkg2.Id);
        AddSession(c2.Id, trainer.Id, st2.Id, Past(DayOfWeek.Tuesday, 3), SessionStatus.Completed, pkg2.Id);
        AddSession(c2.Id, trainer.Id, st2.Id, Past(DayOfWeek.Tuesday, 2), SessionStatus.Completed, pkg2.Id);
        AddSession(c2.Id, trainer.Id, st2.Id, Past(DayOfWeek.Tuesday, 1), SessionStatus.Cancelled, pkg2.Id,
            cancellationReason: "Choroba trenera");
        AddSession(c2.Id, trainer.Id, st2.Id, Future(DayOfWeek.Tuesday, 1), SessionStatus.Scheduled, pkg2.Id);
        AddSession(c2.Id, trainer.Id, st2.Id, Future(DayOfWeek.Tuesday, 2), SessionStatus.Scheduled, pkg2.Id);
        AddSession(c2.Id, trainer.Id, st2.Id, Future(DayOfWeek.Tuesday, 3), SessionStatus.Scheduled, pkg2.Id);

        // Sessions — Piotr (Trening, pakiet wyczerpany)
        AddSession(c3.Id, trainer.Id, st1.Id, Past(DayOfWeek.Wednesday, 4), SessionStatus.Completed, pkg3.Id);
        AddSession(c3.Id, trainer.Id, st1.Id, Past(DayOfWeek.Wednesday, 2), SessionStatus.Completed, pkg3.Id);
        AddSession(c3.Id, trainer.Id, st1.Id, Future(DayOfWeek.Wednesday, 1), SessionStatus.AwaitingPackage);
        AddSession(c3.Id, trainer.Id, st1.Id, Future(DayOfWeek.Wednesday, 2), SessionStatus.AwaitingPackage);

        // Sessions — Alicja (intro, stretching)
        AddSession(c4.Id, trainer.Id, st3.Id, Future(DayOfWeek.Thursday, 1).AddHours(2), SessionStatus.Scheduled,
            notes: "Pierwsza wizyta – wywiad wstępny");

        await db.SaveChangesAsync();

        // 7. Body measurements — Marek
        db.BodyMeasurements.AddRange(
            new BodyMeasurement { ClientId = c1.Id, MeasurementDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-90)), WeightKg = 88.5m, BodyFatPercent = 22.1m, WaistCm = 94m, ChestCm = 102m },
            new BodyMeasurement { ClientId = c1.Id, MeasurementDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-60)), WeightKg = 86.0m, BodyFatPercent = 20.8m, WaistCm = 91m, ChestCm = 103m },
            new BodyMeasurement { ClientId = c1.Id, MeasurementDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-30)), WeightKg = 83.2m, BodyFatPercent = 19.3m, WaistCm = 88m, ChestCm = 104m }
        );
        // Body measurements — Katarzyna
        db.BodyMeasurements.AddRange(
            new BodyMeasurement { ClientId = c2.Id, MeasurementDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-60)), WeightKg = 62.0m, BodyFatPercent = 26.5m, WaistCm = 72m, HipsCm = 96m },
            new BodyMeasurement { ClientId = c2.Id, MeasurementDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-30)), WeightKg = 60.5m, BodyFatPercent = 25.1m, WaistCm = 70m, HipsCm = 94m }
        );

        // 8. Trainer notes
        db.TrainerNotes.AddRange(
            new TrainerNote { ClientId = c1.Id, TrainerUserId = trainer.Id, Content = "Skupiamy się na redukcji tkanki tłuszczowej. Dieta ketogeniczna od 3 tygodni.", CreatedAt = DateTime.Now.AddDays(-45) },
            new TrainerNote { ClientId = c1.Id, TrainerUserId = trainer.Id, Content = "Dobra progresja siłowa – zwiększyć obciążenie na przysiadach do 80 kg.", CreatedAt = DateTime.Now.AddDays(-7) },
            new TrainerNote { ClientId = c2.Id, TrainerUserId = trainer.Id, Content = "Klientka bardzo zmotywowana. Skupiamy się na elastyczności i core.", CreatedAt = DateTime.Now.AddDays(-30) },
            new TrainerNote { ClientId = c3.Id, TrainerUserId = trainer.Id, Content = "Pakiet wyczerpany. Wysłać propozycję przedłużenia. Klient zainteresowany pakietem 20-sesyjnym.", CreatedAt = DateTime.Now.AddDays(-5) },
            new TrainerNote { ClientId = c4.Id, TrainerUserId = trainer.Id, Content = "Nowy klient – brak doświadczenia z treningiem. Zaczynamy od podstaw.", CreatedAt = DateTime.Now.AddDays(-2) }
        );

        // 9. Contact pair — Marek & Katarzyna (trening duet opcjonalnie)
        var minId = Math.Min(c1.Id, c2.Id);
        var maxId = Math.Max(c1.Id, c2.Id);
        db.ClientContacts.Add(new ClientContact { TrainerUserId = trainer.Id, Client1Id = minId, Client2Id = maxId, CreatedAt = DateTime.Now });

        await db.SaveChangesAsync();

        return result;
    }

    public async Task ResetToAdminAsync()
    {
        // Delete in FK-safe order
        await db.AuditLogs.ExecuteDeleteAsync();
        await db.SessionInvitations.ExecuteDeleteAsync();

        // Null nullable FKs so dependent tables can be deleted without FK violations
        await db.Sessions
            .Where(s => s.SeriesId != null || s.PackageId != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SeriesId, (int?)null)
                .SetProperty(x => x.PackageId, (int?)null));

        await db.Sessions.ExecuteDeleteAsync();
        await db.SessionSeries.ExecuteDeleteAsync();
        await db.SessionPackages.ExecuteDeleteAsync();
        await db.ClientContacts.ExecuteDeleteAsync();
        await db.BodyMeasurements.ExecuteDeleteAsync();
        await db.TrainerNotes.ExecuteDeleteAsync();
        await db.Clients.ExecuteDeleteAsync();
        await db.TrainerAvailabilities.ExecuteDeleteAsync();
        await db.TrainerConfigs.ExecuteDeleteAsync();
        await db.IntroSessionConfigs.ExecuteDeleteAsync();

        // Delete all identity users
        var allUsers = await userManager.Users.ToListAsync();
        foreach (var u in allUsers)
            await userManager.DeleteAsync(u);

        // Create admin
        var admin = new ApplicationUser
        {
            UserName = AdminResetEmail,
            Email = AdminResetEmail,
            NormalizedEmail = AdminResetEmail.ToUpperInvariant(),
            NormalizedUserName = AdminResetEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = "Admin",
            LastName = "System",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        var createResult = await userManager.CreateAsync(admin);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(e => e.Description)));

        // Set password directly (bypasses validators — intentional for reset)
        admin.PasswordHash = userManager.PasswordHasher.HashPassword(admin, "password");
        await userManager.UpdateAsync(admin);
        await userManager.AddToRoleAsync(admin, Roles.Admin);
    }

    // ─── HELPERS ──────────────────────────────────────────────────────────────

    private async Task<ApplicationUser> CreateUserAsync(
        string firstName, string lastName, string email, string password, string role)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Błąd tworzenia użytkownika {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, password);
        await userManager.UpdateAsync(user);
        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    private async Task<(Client client, ApplicationUser user)> CreateClientAsync(
        string firstName, string lastName, string email, string password,
        string trainerUserId, bool allowSelfBooking)
    {
        var user = await CreateUserAsync(firstName, lastName, email, password, Roles.Client);
        var client = new Client
        {
            ApplicationUserId = user.Id,
            FirstName = firstName,
            LastName = lastName,
            TrainerUserId = trainerUserId,
            Status = ClientStatus.Active,
            AllowSelfBooking = allowSelfBooking,
            TrainingGoal = firstName switch
            {
                "Marek"     => "Redukcja tkanki tłuszczowej i poprawa kondycji",
                "Katarzyna" => "Poprawa elastyczności i siły mięśni core",
                "Piotr"     => "Budowa masy mięśniowej",
                "Alicja"    => "Ogólna sprawność fizyczna",
                _           => null
            },
            DateOfBirth = firstName switch
            {
                "Marek"     => new DateOnly(1988, 5, 12),
                "Katarzyna" => new DateOnly(1993, 3, 22),
                "Piotr"     => new DateOnly(1985, 11, 8),
                "Alicja"    => new DateOnly(1996, 7, 30),
                _           => null
            },
            CreatedAt = DateTime.Now.AddDays(-90)
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return (client, user);
    }

    private async Task<SessionType> GetOrCreateSessionTypeAsync(string name, int durationMinutes)
    {
        var existing = await db.SessionTypes.FirstOrDefaultAsync(t => t.Name == name);
        if (existing is not null) return existing;

        var st = new SessionType { Name = name, DurationMinutes = durationMinutes, IsActive = true };
        db.SessionTypes.Add(st);
        await db.SaveChangesAsync();
        return st;
    }

    private void AddAvailability(string trainerUserId, DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = trainerUserId,
            DayOfWeek = day,
            StartTime = start,
            EndTime = end,
            IsActive = true,
            CreatedAt = DateTime.Now
        });
    }

    private void AddSession(
        int clientId, string trainerUserId, int sessionTypeId, DateTime startTime,
        SessionStatus status, int? packageId = null, string? cancellationReason = null, string? notes = null)
    {
        db.Sessions.Add(new Session
        {
            ClientId = clientId,
            TrainerUserId = trainerUserId,
            SessionTypeId = sessionTypeId,
            StartTime = startTime,
            Status = status,
            PackageId = packageId,
            Notes = notes,
            CancellationReason = cancellationReason,
            CancelledAt = cancellationReason is not null ? startTime.AddDays(-1) : null,
            CreatedAt = DateTime.Now.AddDays(-7)
        });
    }

    // Returns the most recent past occurrence of the given weekday, N weeks ago
    private static DateTime Past(DayOfWeek day, int weeksAgo)
    {
        var today = DateTime.Today;
        var daysBack = ((int)today.DayOfWeek - (int)day + 7) % 7;
        if (daysBack == 0) daysBack = 7;
        return today.AddDays(-(daysBack + (weeksAgo - 1) * 7)).AddHours(9);
    }

    // Returns the next upcoming occurrence of the given weekday, N weeks ahead
    private static DateTime Future(DayOfWeek day, int weeksAhead)
    {
        var today = DateTime.Today;
        var daysAhead = ((int)day - (int)today.DayOfWeek + 7) % 7;
        if (daysAhead == 0) daysAhead = 7;
        return today.AddDays(daysAhead + (weeksAhead - 1) * 7).AddHours(9);
    }
}
