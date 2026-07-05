using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class DemoDataService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager) : IDemoDataService
{
    private const string TrainerEmail = "jan.kowalski@demo.pl";
    private const string AdminResetEmail = "root@admin.local";

    // Stable "today" anchor (UTC, midnight) used across the whole seed
    private static DateTime Today =>
        DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    // ─── PUBLIC API ────────────────────────────────────────────────────────────

    public async Task<DemoSeedResult> SeedAsync()
    {
        if (await userManager.FindByEmailAsync(TrainerEmail) is not null)
            return new DemoSeedResult { AlreadySeeded = true };

        var result = new DemoSeedResult();
        var today = Today;

        // 1. Users -------------------------------------------------------------
        var trainer = await CreateUserAsync("Jan", "Kowalski", TrainerEmail, "trener1", Roles.Trainer);
        result.Users.Add(new() { Role = "Trener", FullName = "Jan Kowalski", Email = TrainerEmail, Password = "trener1" });

        var manager = await CreateUserAsync("Anna", "Wiśniewska", "anna@demo.pl", "menedzer1", Roles.Subordinate);
        manager.SupervisorId = trainer.Id;
        await userManager.UpdateAsync(manager);
        result.Users.Add(new() { Role = "Menadżer", FullName = "Anna Wiśniewska", Email = "anna@demo.pl", Password = "menedzer1" });

        await using var db = dbFactory.CreateDbContext();

        // 2. Session types -----------------------------------------------------
        var stPersonal = await GetOrCreateSessionTypeAsync(db, "Trening personalny", 60);
        var stPilates  = await GetOrCreateSessionTypeAsync(db, "Pilates", 45);
        var stStretch  = await GetOrCreateSessionTypeAsync(db, "Stretching", 30);
        var stStrength = await GetOrCreateSessionTypeAsync(db, "Trening siłowy", 75);
        var stCardio   = await GetOrCreateSessionTypeAsync(db, "Cardio HIIT", 45);

        // 3. Availability + trainer config + intro config + branding ----------
        AddAvailability(db, trainer.Id, DayOfWeek.Monday,    new(7, 0),  new(20, 0));
        AddAvailability(db, trainer.Id, DayOfWeek.Tuesday,   new(7, 0),  new(20, 0));
        AddAvailability(db, trainer.Id, DayOfWeek.Wednesday, new(7, 0),  new(20, 0));
        AddAvailability(db, trainer.Id, DayOfWeek.Thursday,  new(7, 0),  new(20, 0));
        AddAvailability(db, trainer.Id, DayOfWeek.Friday,    new(7, 0),  new(20, 0));
        AddAvailability(db, trainer.Id, DayOfWeek.Saturday,  new(8, 0),  new(14, 0));
        // Jednorazowe okienko na sobotę za 2 tygodnie (warsztaty)
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = trainer.Id,
            SpecificDate = DateOnly.FromDateTime(today.AddDays(14)),
            StartTime = new(9, 0),
            EndTime = new(17, 0),
            Label = "Warsztaty grupowe",
            IsActive = true,
            CreatedAt = today.AddDays(-30)
        });

        db.TrainerConfigs.Add(new TrainerConfig
        {
            TrainerUserId = trainer.Id,
            BreakAfterSessionMinutes = 15,
            SlotGranularityMinutes = 30,
            AllowClientsDiscoverPeers = true,
            CancellationWindowHours = 24
        });

        db.IntroSessionConfigs.Add(new IntroSessionConfig
        {
            TrainerUserId = trainer.Id,
            DurationMinutes = 60,
            IsFree = false,
            Price = 120m,
            PromoPrice = 60m,
            PromoValidUntil = today.AddDays(30),
            Description = "Pierwsze spotkanie: wywiad, pomiary, plan działania.",
            IsActive = true
        });

        await db.SaveChangesAsync();

        // 4. Clients -----------------------------------------------------------
        var (cMarek, _) = await CreateClientAsync(db,
            "Marek", "Nowak", "marek@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: true, status: ClientStatus.Active,
            goal: "Redukcja tkanki tłuszczowej i poprawa kondycji",
            dob: new(1988, 5, 12), createdDaysAgo: 95);
        result.Users.Add(new() { Role = "Klient", FullName = "Marek Nowak", Email = "marek@demo.pl", Password = "klient1" });

        var (cKatarzyna, _) = await CreateClientAsync(db,
            "Katarzyna", "Zielińska", "katarzyna@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: true, status: ClientStatus.Active,
            goal: "Poprawa elastyczności i siły mięśni core",
            dob: new(1993, 3, 22), createdDaysAgo: 80);
        result.Users.Add(new() { Role = "Klient", FullName = "Katarzyna Zielińska", Email = "katarzyna@demo.pl", Password = "klient1" });

        var (cPiotr, _) = await CreateClientAsync(db,
            "Piotr", "Wiśniewski", "piotr@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: false, status: ClientStatus.Active,
            goal: "Budowa masy mięśniowej",
            dob: new(1985, 11, 8), createdDaysAgo: 100);
        result.Users.Add(new() { Role = "Klient", FullName = "Piotr Wiśniewski", Email = "piotr@demo.pl", Password = "klient1" });

        var (cAlicja, _) = await CreateClientAsync(db,
            "Alicja", "Kowalska", "alicja@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: false, status: ClientStatus.Active,
            goal: "Ogólna sprawność fizyczna",
            dob: new(1996, 7, 30), createdDaysAgo: 8);
        result.Users.Add(new() { Role = "Klient", FullName = "Alicja Kowalska", Email = "alicja@demo.pl", Password = "klient1" });

        var (cTomasz, _) = await CreateClientAsync(db,
            "Tomasz", "Lewandowski", "tomasz@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: true, status: ClientStatus.Active,
            goal: "Przygotowanie do półmaratonu",
            dob: new(1990, 2, 14), createdDaysAgo: 100);
        result.Users.Add(new() { Role = "Klient", FullName = "Tomasz Lewandowski", Email = "tomasz@demo.pl", Password = "klient1" });

        var (cMagdalena, _) = await CreateClientAsync(db,
            "Magdalena", "Dąbrowska", "magdalena@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: true, status: ClientStatus.Active,
            goal: "Rzeźba sylwetki przed sezonem letnim",
            dob: new(1991, 9, 17), createdDaysAgo: 65);
        result.Users.Add(new() { Role = "Klient", FullName = "Magdalena Dąbrowska", Email = "magdalena@demo.pl", Password = "klient1" });

        var (cRobert, _) = await CreateClientAsync(db,
            "Robert", "Kamiński", "robert@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: true, status: ClientStatus.Active,
            goal: "Utrzymanie formy + treningi z bratem (Tomasz)",
            dob: new(1987, 4, 5), createdDaysAgo: 100);
        result.Users.Add(new() { Role = "Klient", FullName = "Robert Kamiński", Email = "robert@demo.pl", Password = "klient1" });

        var (cZofia, _) = await CreateClientAsync(db,
            "Zofia", "Pawlak", "zofia@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: false, status: ClientStatus.Pending,
            goal: "Kontuzja kolana – fizjoterapeuta polecił trening prowadzony",
            dob: new(1979, 12, 1), createdDaysAgo: 1);
        result.Users.Add(new() { Role = "Klient (oczekujący)", FullName = "Zofia Pawlak", Email = "zofia@demo.pl", Password = "klient1" });

        var (cEwa, _) = await CreateClientAsync(db,
            "Ewa", "Sikorska", "ewa@demo.pl", "klient1", trainer.Id,
            allowSelfBooking: false, status: ClientStatus.Inactive,
            goal: null,
            dob: new(1982, 6, 19), createdDaysAgo: 200);
        result.Users.Add(new() { Role = "Klient (archiwum)", FullName = "Ewa Sikorska", Email = "ewa@demo.pl", Password = "klient1" });

        // 5. Packages ----------------------------------------------------------
        // Marek: stary zakończony + aktualny aktywny + nowy zakupiony niedawno
        var pkgMarek1 = MakePackage(cMarek.Id, trainer.Id, stPersonal.Id,
            "Pakiet Start (10)", 10, 10, 120m, paid: true,
            purchasedDaysAgo: 95, paidDaysAgo: 95,
            paymentRef: "P/2026/01/12", status: PackageStatus.Depleted);
        var pkgMarek2 = MakePackage(cMarek.Id, trainer.Id, stPersonal.Id,
            "Pakiet Pro (20)", 20, 11, 110m, paid: true,
            purchasedDaysAgo: 50, paidDaysAgo: 50,
            paymentRef: "P/2026/03/04", status: PackageStatus.Active);
        var pkgMarek3 = MakePackage(cMarek.Id, trainer.Id, stStretch.Id,
            "Doładowanie stretching (4)", 4, 0, 80m, paid: false,
            purchasedDaysAgo: 2, paidDaysAgo: null,
            paymentRef: null, status: PackageStatus.Active);

        // Katarzyna
        var pkgKat1 = MakePackage(cKatarzyna.Id, trainer.Id, stPilates.Id,
            "Pakiet Pilates (20)", 20, 14, 100m, paid: true,
            purchasedDaysAgo: 75, paidDaysAgo: 75,
            paymentRef: "P/2026/02/18", status: PackageStatus.Active);

        // Piotr — wyczerpany
        var pkgPiotr1 = MakePackage(cPiotr.Id, trainer.Id, stPersonal.Id,
            "Pakiet Standard (10)", 10, 10, 120m, paid: true,
            purchasedDaysAgo: 90, paidDaysAgo: 90,
            paymentRef: "P/2026/02/01", status: PackageStatus.Expired,
            expiresDaysFromNow: -7);

        // Alicja — pakiet próbny
        var pkgAlicja1 = MakePackage(cAlicja.Id, trainer.Id, stStretch.Id,
            "Pakiet próbny (4)", 4, 1, 80m, paid: true,
            purchasedDaysAgo: 8, paidDaysAgo: 6,
            paymentRef: "P/2026/04/26", status: PackageStatus.Active);

        // Tomasz — siłowy + cardio
        var pkgTomasz1 = MakePackage(cTomasz.Id, trainer.Id, stStrength.Id,
            "Pakiet Siłowy (15)", 15, 12, 130m, paid: true,
            purchasedDaysAgo: 95, paidDaysAgo: 95,
            paymentRef: "P/2026/01/14", status: PackageStatus.Active);
        var pkgTomasz2 = MakePackage(cTomasz.Id, trainer.Id, stCardio.Id,
            "Pakiet Cardio (10)", 10, 6, 90m, paid: true,
            purchasedDaysAgo: 40, paidDaysAgo: 40,
            paymentRef: "P/2026/03/15", status: PackageStatus.Active);

        // Magdalena
        var pkgMag1 = MakePackage(cMagdalena.Id, trainer.Id, stPilates.Id,
            "Pakiet Body (12)", 12, 7, 105m, paid: true,
            purchasedDaysAgo: 60, paidDaysAgo: 58,
            paymentRef: "P/2026/03/01", status: PackageStatus.Active);

        // Robert — siłowy + duety
        var pkgRob1 = MakePackage(cRobert.Id, trainer.Id, stStrength.Id,
            "Pakiet Siłowy Duet (15)", 15, 11, 100m, paid: true,
            purchasedDaysAgo: 95, paidDaysAgo: 95,
            paymentRef: "P/2026/01/14", status: PackageStatus.Active);

        // Ewa — archiwalny pakiet
        var pkgEwa1 = MakePackage(cEwa.Id, trainer.Id, stPersonal.Id,
            "Pakiet Archiwalny (10)", 10, 8, 100m, paid: true,
            purchasedDaysAgo: 200, paidDaysAgo: 200,
            paymentRef: "P/2025/10/12", status: PackageStatus.Cancelled);

        db.SessionPackages.AddRange(
            pkgMarek1, pkgMarek2, pkgMarek3,
            pkgKat1, pkgPiotr1, pkgAlicja1,
            pkgTomasz1, pkgTomasz2, pkgMag1, pkgRob1, pkgEwa1);
        await db.SaveChangesAsync();

        // 6. Recurring series --------------------------------------------------
        var seriesMarek = new SessionSeries
        {
            ClientId = cMarek.Id,
            TrainerUserId = trainer.Id,
            SessionTypeId = stPersonal.Id,
            RecurrenceDays = "1,3", // pon + śr
            StartTime = new(18, 0),
            StartDate = DateOnly.FromDateTime(today.AddDays(-90)),
            EndDate = null,
            Notes = "Stały slot Marka — pon/śr 18:00",
            IsActive = true,
            CreatedAt = today.AddDays(-90),
            CreatedByUserId = trainer.Id
        };
        var seriesKat = new SessionSeries
        {
            ClientId = cKatarzyna.Id,
            TrainerUserId = trainer.Id,
            SessionTypeId = stPilates.Id,
            RecurrenceDays = "2,4", // wt + czw
            StartTime = new(17, 0),
            StartDate = DateOnly.FromDateTime(today.AddDays(-75)),
            EndDate = null,
            Notes = "Pilates — wt/czw 17:00",
            IsActive = true,
            CreatedAt = today.AddDays(-75),
            CreatedByUserId = trainer.Id
        };
        var seriesTomaszStary = new SessionSeries
        {
            ClientId = cTomasz.Id,
            TrainerUserId = trainer.Id,
            SessionTypeId = stStrength.Id,
            RecurrenceDays = "5", // piątek
            StartTime = new(19, 0),
            StartDate = DateOnly.FromDateTime(today.AddDays(-95)),
            EndDate = DateOnly.FromDateTime(today.AddDays(-30)),
            Notes = "Stara seria — przeniesiony do duetu z Robertem",
            IsActive = false,
            CreatedAt = today.AddDays(-95),
            CreatedByUserId = trainer.Id
        };
        db.SessionSeries.AddRange(seriesMarek, seriesKat, seriesTomaszStary);
        await db.SaveChangesAsync();

        // 7. Sessions ----------------------------------------------------------
        var sessions = new List<Session>();

        // Marek: seria pon/śr od 90 dni do +14 dni → ~26 sesji
        sessions.AddRange(GenerateSeriesSessions(
            seriesMarek, cMarek.Id, trainer.Id, stPersonal.Id,
            startDays: -90, endDays: 14,
            recurrence: [DayOfWeek.Monday, DayOfWeek.Wednesday],
            hour: 18, packageDistribution: [pkgMarek1.Id, pkgMarek2.Id]));

        // Katarzyna: seria wt/czw od 75 dni do +14 dni
        sessions.AddRange(GenerateSeriesSessions(
            seriesKat, cKatarzyna.Id, trainer.Id, stPilates.Id,
            startDays: -75, endDays: 14,
            recurrence: [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            hour: 17, packageDistribution: [pkgKat1.Id]));

        // Piotr: 10 sesji historycznych (pakiet wyczerpany), 2 awaiting
        for (int i = 0; i < 10; i++)
        {
            sessions.Add(MakeSession(
                cPiotr.Id, trainer.Id, stPersonal.Id,
                AtUtc(today, -85 + i * 7, 11),
                SessionStatus.Completed, packageId: pkgPiotr1.Id));
        }
        sessions.Add(MakeSession(cPiotr.Id, trainer.Id, stPersonal.Id,
            AtUtc(today, 7, 11), SessionStatus.AwaitingPackage));
        sessions.Add(MakeSession(cPiotr.Id, trainer.Id, stPersonal.Id,
            AtUtc(today, 14, 11), SessionStatus.AwaitingPackage));

        // Alicja: 1 zrealizowana intro + 1 zaplanowana
        sessions.Add(MakeSession(cAlicja.Id, trainer.Id, stStretch.Id,
            AtUtc(today, -5, 10), SessionStatus.Completed, packageId: pkgAlicja1.Id,
            notes: "Pierwsza wizyta – wywiad wstępny, pomiary, plan."));
        sessions.Add(MakeSession(cAlicja.Id, trainer.Id, stStretch.Id,
            AtUtc(today, 2, 10), SessionStatus.Scheduled, packageId: pkgAlicja1.Id));

        // Tomasz: stara seria piątkowa (zakończona, sesje historyczne)
        for (int week = 0; week < 9; week++) // 9 piątków, ostatni 30 dni temu
        {
            var d = -95 + week * 7;
            if (d > -30) break;
            sessions.Add(MakeSession(cTomasz.Id, trainer.Id, stStrength.Id,
                AtUtc(today, d, 19), SessionStatus.Completed,
                packageId: pkgTomasz1.Id, seriesId: seriesTomaszStary.Id));
        }

        // Tomasz: cardio jednorazowe co tydzień, soboty 9:00, ostatnie 6 tygodni
        for (int week = 0; week < 6; week++)
        {
            var d = -42 + week * 7;
            sessions.Add(MakeSession(cTomasz.Id, trainer.Id, stCardio.Id,
                AtUtc(today, d, 9), SessionStatus.Completed, packageId: pkgTomasz2.Id));
        }
        sessions.Add(MakeSession(cTomasz.Id, trainer.Id, stCardio.Id,
            AtUtc(today, 6, 9), SessionStatus.Scheduled, packageId: pkgTomasz2.Id));

        // Magdalena: pilates 1x/tyg ostatnie 9 tygodni + 2 nadchodzące
        for (int week = 0; week < 9; week++)
        {
            var d = -60 + week * 7;
            var status = week == 4 ? SessionStatus.Cancelled
                       : week == 7 ? SessionStatus.NoShow
                       : SessionStatus.Completed;
            var reason = status == SessionStatus.Cancelled ? "Klientka odwołała — wyjazd służbowy"
                       : status == SessionStatus.NoShow ? "Klientka nie pojawiła się i nie odpisała"
                       : null;
            sessions.Add(MakeSession(cMagdalena.Id, trainer.Id, stPilates.Id,
                AtUtc(today, d, 16), status, packageId: pkgMag1.Id,
                cancellationReason: reason));
        }
        sessions.Add(MakeSession(cMagdalena.Id, trainer.Id, stPilates.Id,
            AtUtc(today, 5, 16), SessionStatus.Scheduled, packageId: pkgMag1.Id));
        sessions.Add(MakeSession(cMagdalena.Id, trainer.Id, stPilates.Id,
            AtUtc(today, 12, 16), SessionStatus.Scheduled, packageId: pkgMag1.Id));

        // Robert: indywidualne treningi siłowe + udział w duetach (poniżej)
        for (int week = 0; week < 8; week++)
        {
            var d = -56 + week * 7;
            sessions.Add(MakeSession(cRobert.Id, trainer.Id, stStrength.Id,
                AtUtc(today, d, 10), SessionStatus.Completed, packageId: pkgRob1.Id));
        }
        sessions.Add(MakeSession(cRobert.Id, trainer.Id, stStrength.Id,
            AtUtc(today, 4, 10), SessionStatus.Scheduled, packageId: pkgRob1.Id));

        // Ewa: 8 historycznych sesji ~6 miesięcy temu
        for (int i = 0; i < 8; i++)
        {
            sessions.Add(MakeSession(cEwa.Id, trainer.Id, stPersonal.Id,
                AtUtc(today, -195 + i * 7, 12), SessionStatus.Completed,
                packageId: pkgEwa1.Id));
        }

        db.Sessions.AddRange(sessions);
        await db.SaveChangesAsync();

        // 8. Joint sessions (treningi wspólne) --------------------------------
        // Duet Marek + Katarzyna: 4 historyczne (Accepted) + 1 przyszła (Pending)
        var duetMK = new List<Session>();
        for (int week = 0; week < 4; week++)
        {
            var d = -50 + week * 14;
            duetMK.Add(MakeSession(cMarek.Id, trainer.Id, stStretch.Id,
                AtUtc(today, d, 19, 30), SessionStatus.Completed, packageId: pkgMarek2.Id,
                notes: "Duet Marek + Katarzyna"));
        }
        duetMK.Add(MakeSession(cMarek.Id, trainer.Id, stStretch.Id,
            AtUtc(today, 9, 19, 30), SessionStatus.Scheduled, packageId: pkgMarek3.Id,
            notes: "Duet Marek + Katarzyna (zaplanowany)"));
        db.Sessions.AddRange(duetMK);
        await db.SaveChangesAsync();
        foreach (var s in duetMK)
        {
            db.SessionInvitations.Add(new SessionInvitation
            {
                SessionId = s.Id,
                InvitedClientId = cKatarzyna.Id,
                Status = s.Status == SessionStatus.Scheduled ? InvitationStatus.Pending : InvitationStatus.Accepted,
                CreatedAt = s.StartTime.AddDays(-3),
                RespondedAt = s.Status == SessionStatus.Scheduled ? null : s.StartTime.AddDays(-2),
                ResponseNote = s.Status == SessionStatus.Scheduled ? null : "Jasne, jestem!",
                CreatedByUserId = trainer.Id
            });
        }

        // Duet Tomasz + Robert (bracia): 5 historycznych, 1 odwołana, 1 przyszła
        var duetTR = new List<Session>();
        for (int week = 0; week < 5; week++)
        {
            var d = -28 + week * 7;
            duetTR.Add(MakeSession(cTomasz.Id, trainer.Id, stStrength.Id,
                AtUtc(today, d, 19), SessionStatus.Completed, packageId: pkgTomasz1.Id,
                notes: "Duet Tomasz + Robert"));
        }
        var cancelledDuet = MakeSession(cTomasz.Id, trainer.Id, stStrength.Id,
            AtUtc(today, -3, 19), SessionStatus.Cancelled, packageId: pkgTomasz1.Id,
            cancellationReason: "Robert kontuzja — odwołanie z 24h wyprzedzeniem",
            notes: "Duet Tomasz + Robert");
        duetTR.Add(cancelledDuet);
        var futureDuet = MakeSession(cTomasz.Id, trainer.Id, stStrength.Id,
            AtUtc(today, 4, 19), SessionStatus.Scheduled, packageId: pkgTomasz1.Id,
            notes: "Duet Tomasz + Robert");
        duetTR.Add(futureDuet);
        db.Sessions.AddRange(duetTR);
        await db.SaveChangesAsync();
        foreach (var s in duetTR)
        {
            db.SessionInvitations.Add(new SessionInvitation
            {
                SessionId = s.Id,
                InvitedClientId = cRobert.Id,
                Status = s.Status switch
                {
                    SessionStatus.Cancelled => InvitationStatus.Declined,
                    SessionStatus.Scheduled => InvitationStatus.Accepted,
                    _ => InvitationStatus.Accepted
                },
                CreatedAt = s.StartTime.AddDays(-5),
                RespondedAt = s.StartTime.AddDays(-4),
                ResponseNote = s.Status == SessionStatus.Cancelled ? "Niestety kontuzja, muszę odwołać." : null,
                CreatedByUserId = trainer.Id
            });
        }

        // Trio: Marek + Katarzyna + Magdalena — 1 zaplanowana grupowa
        var trio = MakeSession(cMarek.Id, trainer.Id, stStretch.Id,
            AtUtc(today, 11, 18), SessionStatus.Scheduled, packageId: pkgMarek3.Id,
            notes: "Sesja grupowa: Marek + Katarzyna + Magdalena");
        db.Sessions.Add(trio);
        await db.SaveChangesAsync();
        db.SessionInvitations.AddRange(
            new SessionInvitation
            {
                SessionId = trio.Id,
                InvitedClientId = cKatarzyna.Id,
                Status = InvitationStatus.Accepted,
                CreatedAt = trio.StartTime.AddDays(-7),
                RespondedAt = trio.StartTime.AddDays(-6),
                ResponseNote = "Brzmi super.",
                CreatedByUserId = trainer.Id
            },
            new SessionInvitation
            {
                SessionId = trio.Id,
                InvitedClientId = cMagdalena.Id,
                Status = InvitationStatus.Pending,
                CreatedAt = trio.StartTime.AddDays(-7),
                CreatedByUserId = trainer.Id
            });

        await db.SaveChangesAsync();

        // 9. Body measurements -------------------------------------------------
        AddMeasurements(db, cMarek.Id, today, [
            (90, 88.5m, 22.1m, 102m, 94m, null, 58m, 36m),
            (75, 87.0m, 21.4m, 102m, 92m, null, 58m, 37m),
            (60, 85.5m, 20.8m, 103m, 90m, null, 57m, 37m),
            (45, 84.0m, 20.0m, 103m, 89m, null, 57m, 37m),
            (30, 82.7m, 19.3m, 104m, 88m, null, 56m, 38m),
            (10, 81.2m, 18.7m, 104m, 86m, null, 56m, 38m),
        ]);
        AddMeasurements(db, cKatarzyna.Id, today, [
            (75, 62.0m, 26.5m, 88m, 72m, 96m, 54m, 27m),
            (45, 61.4m, 25.8m, 89m, 71m, 95m, 54m, 28m),
            (15, 60.5m, 25.1m, 89m, 70m, 94m, 53m, 28m),
        ]);
        AddMeasurements(db, cTomasz.Id, today, [
            (95, 75.0m, 16.5m, 96m, 80m, null, 56m, 33m),
            (70, 76.8m, 16.0m, 98m, 80m, null, 57m, 34m),
            (40, 78.5m, 15.5m, 99m, 80m, null, 58m, 35m),
            (10, 80.1m, 15.0m, 100m, 81m, null, 58m, 36m),
        ]);
        AddMeasurements(db, cMagdalena.Id, today, [
            (60, 68.0m, 28.0m, 92m, 78m, 100m, 56m, 28m),
            (35, 66.2m, 26.3m, 92m, 75m, 98m, 55m, 28m),
            (10, 64.8m, 24.9m, 92m, 73m, 96m, 54m, 28m),
        ]);
        AddMeasurements(db, cRobert.Id, today, [
            (95, 82.0m, 18.0m, 102m, 88m, null, 60m, 35m),
            (50, 82.5m, 17.5m, 103m, 87m, null, 60m, 36m),
            (10, 82.8m, 17.0m, 103m, 87m, null, 60m, 36m),
        ]);
        AddMeasurements(db, cAlicja.Id, today, [
            (5, 58.0m, 24.0m, 86m, 68m, 92m, 52m, 26m),
        ]);
        AddMeasurements(db, cPiotr.Id, today, [
            (90, 78.0m, 19.0m, 100m, 84m, null, 58m, 34m),
            (50, 79.0m, 18.5m, 100m, 84m, null, 58m, 35m),
            (15, 79.5m, 18.2m, 101m, 84m, null, 58m, 35m),
        ]);

        // 10. Trainer notes ----------------------------------------------------
        AddNotes(db, cMarek.Id, trainer.Id, today, [
            (-90, "Wywiad wstępny: cel — redukcja, dieta dotychczas niepilnowana. Plan: 3 treningi/tyg + dieta keto."),
            (-70, "Ruszyła keto. Pierwsze 2 kg zeszły. Energia w normie."),
            (-45, "Progresja siłowa OK — przysiad 70 kg, wyciskanie 60 kg. Zwiększyć obciążenie na nogach."),
            (-20, "Plateau przez 7 dni — zmieniliśmy schemat na 4-dniowy split. Ruszyło."),
            (-5,  "Świetna forma, planujemy wprowadzić HIIT w czwartki."),
        ]);
        AddNotes(db, cKatarzyna.Id, trainer.Id, today, [
            (-70, "Klientka bardzo zmotywowana, regularność 100%. Skupiamy się na elastyczności i core."),
            (-30, "Wprowadziliśmy bardziej zaawansowane warianty plank — radzi sobie."),
            (-7,  "Zgłosiła ból dolnego odcinka pleców — dodamy mobilizację bioder."),
        ]);
        AddNotes(db, cPiotr.Id, trainer.Id, today, [
            (-90, "Wywiad: chce masy. Niewielkie doświadczenie z wolnymi ciężarami — zaczynamy od techniki."),
            (-60, "Wzrost siły wyraźny. Przybyło ~2 kg masy ciała."),
            (-10, "Pakiet wyczerpany. Wysłałem propozycję pakietu 20-sesyjnego z rabatem."),
            (-3,  "Klient zainteresowany pakietem 20-sesyjnym — czekam na decyzję."),
        ]);
        AddNotes(db, cAlicja.Id, trainer.Id, today, [
            (-5, "Pierwsza wizyta. Brak doświadczenia z treningiem. Zaczynamy od podstaw — mobilność i nawyki."),
        ]);
        AddNotes(db, cTomasz.Id, trainer.Id, today, [
            (-100, "Cel: półmaraton za 4 miesiące. Plan: 1 trening siłowy + cardio + bieganie własne."),
            (-60, "Czas na 5 km: 24:30. Postęp dobry."),
            (-25, "Przeszliśmy na duety z bratem (Robert) zamiast piątkowych solo — bardziej go to motywuje."),
            (-3, "Robert kontuzja — duet odwołany, wracamy do solo na 2 tyg."),
        ]);
        AddNotes(db, cMagdalena.Id, trainer.Id, today, [
            (-60, "Cel: rzeźba przed sezonem. Plan: pilates + dieta deficytowa."),
            (-30, "Spadek 1.8 kg w miesiąc — tempo OK, bez utraty mięśni."),
            (-10, "NoShow w zeszłym tygodniu — ostrzegłem o polityce odwołań 24h."),
        ]);
        AddNotes(db, cRobert.Id, trainer.Id, today, [
            (-100, "Trening siłowy dla utrzymania formy. Brat (Tomasz) też ćwiczy — sugestia duetów."),
            (-30, "Duety z Tomaszem — atmosfera świetna, motywują się wzajemnie."),
            (-3, "Kontuzja barku — pauza 2 tyg, wracamy do indywidualnych."),
        ]);
        AddNotes(db, cZofia.Id, trainer.Id, today, [
            (-1, "Pierwszy kontakt. Po kontuzji kolana — fizjoterapeuta polecił trening prowadzony. Czeka na zatwierdzenie."),
        ]);

        // 11. Client contacts (pary) ------------------------------------------
        AddContact(db, trainer.Id, cMarek.Id, cKatarzyna.Id, today.AddDays(-50));
        AddContact(db, trainer.Id, cTomasz.Id, cRobert.Id, today.AddDays(-95));
        AddContact(db, trainer.Id, cMarek.Id, cMagdalena.Id, today.AddDays(-15));

        // 12. Audit log (przykładowe wpisy) ------------------------------------
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Timestamp = today.AddDays(-2),
                UserId = trainer.Id,
                UserEmail = TrainerEmail,
                UserRole = Roles.Trainer,
                Action = "PackageCreated",
                EntityType = nameof(SessionPackage),
                EntityId = pkgMarek3.Id.ToString(),
                Details = "Doładowanie stretching (4) — Marek Nowak"
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-1),
                UserId = trainer.Id,
                UserEmail = TrainerEmail,
                UserRole = Roles.Trainer,
                Action = "ClientPendingApproved",
                EntityType = nameof(Client),
                EntityId = cAlicja.Id.ToString(),
                Details = "Zatwierdzony klient: Alicja Kowalska"
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-3),
                UserId = trainer.Id,
                UserEmail = TrainerEmail,
                UserRole = Roles.Trainer,
                Action = "SessionCancelled",
                EntityType = nameof(Session),
                EntityId = cancelledDuet.Id.ToString(),
                Details = "Duet Tomasz + Robert — kontuzja"
            },
            new AuditLog
            {
                Timestamp = today,
                UserId = trainer.Id,
                UserEmail = TrainerEmail,
                UserRole = Roles.Trainer,
                Action = "ClientCreated",
                EntityType = nameof(Client),
                EntityId = cZofia.Id.ToString(),
                Details = "Nowy klient (oczekujący): Zofia Pawlak"
            });

        // 13. Notification preferences for trainer (jawne)
        db.NotificationPreferences.Add(new NotificationPreferences
        {
            UserId = trainer.Id,
            ShowHints = true
        });

        await db.SaveChangesAsync();

        return result;
    }

    public async Task<DemoSeedResult> ResetAndSeedAsync()
    {
        await ResetToAdminAsync();
        // After reset only admin exists — Seed is safe to run, trainer email isn't taken.
        return await SeedAsync();
    }

    public async Task ResetToAdminAsync()
    {
        await using var db = dbFactory.CreateDbContext();
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
        await db.NotificationPreferences.ExecuteDeleteAsync();

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
        ApplicationDbContext db, string firstName, string lastName, string email, string password,
        string trainerUserId, bool allowSelfBooking, ClientStatus status,
        string? goal, DateOnly? dob, int createdDaysAgo)
    {
        var user = await CreateUserAsync(firstName, lastName, email, password, Roles.Client);
        var client = new Client
        {
            ApplicationUserId = user.Id,
            FirstName = firstName,
            LastName = lastName,
            TrainerUserId = trainerUserId,
            Status = status,
            AllowSelfBooking = allowSelfBooking,
            TrainingGoal = goal,
            DateOfBirth = dob,
            CreatedAt = Today.AddDays(-createdDaysAgo)
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return (client, user);
    }

    private static async Task<SessionType> GetOrCreateSessionTypeAsync(ApplicationDbContext db, string name, int durationMinutes)
    {
        var existing = await db.SessionTypes.FirstOrDefaultAsync(t => t.Name == name);
        if (existing is not null) return existing;

        var st = new SessionType { Name = name, DurationMinutes = durationMinutes, IsActive = true };
        db.SessionTypes.Add(st);
        await db.SaveChangesAsync();
        return st;
    }

    private static void AddAvailability(ApplicationDbContext db, string trainerUserId, DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = trainerUserId,
            DayOfWeek = day,
            StartTime = start,
            EndTime = end,
            IsActive = true,
            CreatedAt = Today.AddDays(-90)
        });
    }

    private static SessionPackage MakePackage(
        int clientId, string createdByUserId, int sessionTypeId,
        string name, int totalSessions, int usedSessions, decimal pricePerSession,
        bool paid, int purchasedDaysAgo, int? paidDaysAgo, string? paymentRef,
        PackageStatus status, int? expiresDaysFromNow = null)
    {
        var today = Today;
        return new SessionPackage
        {
            ClientId = clientId,
            CreatedByUserId = createdByUserId,
            SessionTypeId = sessionTypeId,
            Name = name,
            TotalSessions = totalSessions,
            UsedSessions = usedSessions,
            PricePerSession = pricePerSession,
            IsPaid = paid,
            PaidAt = paidDaysAgo is null ? null : today.AddDays(-paidDaysAgo.Value),
            PaymentReference = paymentRef,
            PurchasedAt = today.AddDays(-purchasedDaysAgo),
            ExpiresAt = expiresDaysFromNow is null ? null : today.AddDays(expiresDaysFromNow.Value),
            Status = status
        };
    }

    private static Session MakeSession(
        int clientId, string trainerUserId, int sessionTypeId, DateTime startTime,
        SessionStatus status, int? packageId = null, int? seriesId = null,
        string? cancellationReason = null, string? notes = null)
        => new()
        {
            ClientId = clientId,
            TrainerUserId = trainerUserId,
            SessionTypeId = sessionTypeId,
            StartTime = startTime,
            Status = status,
            PackageId = packageId,
            SeriesId = seriesId,
            Notes = notes,
            CancellationReason = cancellationReason,
            CancelledAt = cancellationReason is not null
                ? DateTime.SpecifyKind(startTime.AddDays(-1), DateTimeKind.Utc)
                : null,
            CreatedAt = DateTime.SpecifyKind(startTime.AddDays(-7), DateTimeKind.Utc)
        };

    private static List<Session> GenerateSeriesSessions(
        SessionSeries series, int clientId, string trainerUserId, int sessionTypeId,
        int startDays, int endDays, DayOfWeek[] recurrence, int hour,
        int[] packageDistribution)
    {
        var today = Today;
        var sessions = new List<Session>();
        var pkgIdx = 0;
        var pkgUsed = 0;
        for (int d = startDays; d <= endDays; d++)
        {
            var date = today.AddDays(d);
            if (!recurrence.Contains(date.DayOfWeek)) continue;

            var status = d < 0 ? SessionStatus.Completed : SessionStatus.Scheduled;
            // 1 cancelled w środku historii
            if (d == startDays + 21) status = SessionStatus.Cancelled;
            // 1 NoShow
            if (d == startDays + 35) status = SessionStatus.NoShow;

            var reason = status switch
            {
                SessionStatus.Cancelled => "Klient odwołał z 12h wyprzedzeniem",
                SessionStatus.NoShow    => "Klient nie pojawił się",
                _ => null
            };

            // Pakiet "płynący" — przeskocz na kolejny gdy pierwszy zużyty
            int? pkg = null;
            if (status != SessionStatus.Cancelled && status != SessionStatus.NoShow
                && packageDistribution.Length > 0 && pkgIdx < packageDistribution.Length)
            {
                pkg = packageDistribution[pkgIdx];
                pkgUsed++;
                if (pkgUsed >= 10 && pkgIdx < packageDistribution.Length - 1)
                {
                    pkgIdx++;
                    pkgUsed = 0;
                }
            }

            sessions.Add(new Session
            {
                ClientId = clientId,
                TrainerUserId = trainerUserId,
                SessionTypeId = sessionTypeId,
                StartTime = AtUtc(today, d, hour),
                Status = status,
                PackageId = pkg,
                Series = series,
                CancellationReason = reason,
                CancelledAt = reason is not null
                    ? DateTime.SpecifyKind(today.AddDays(d - 1).AddHours(20), DateTimeKind.Utc)
                    : null,
                CreatedAt = DateTime.SpecifyKind(today.AddDays(startDays).AddHours(8), DateTimeKind.Utc)
            });
        }
        return sessions;
    }

    private static void AddMeasurements(
        ApplicationDbContext db, int clientId, DateTime today,
        IEnumerable<(int daysAgo, decimal weight, decimal bodyFat, decimal? chest, decimal? waist, decimal? hips, decimal? thigh, decimal? arm)> rows)
    {
        foreach (var (d, w, bf, ch, wa, hi, th, ar) in rows)
        {
            db.BodyMeasurements.Add(new BodyMeasurement
            {
                ClientId = clientId,
                MeasurementDate = DateOnly.FromDateTime(today.AddDays(-d)),
                WeightKg = w,
                BodyFatPercent = bf,
                ChestCm = ch,
                WaistCm = wa,
                HipsCm = hi,
                ThighCm = th,
                ArmCm = ar
            });
        }
    }

    private static void AddNotes(
        ApplicationDbContext db, int clientId, string trainerUserId, DateTime today,
        IEnumerable<(int daysAgo, string content)> rows)
    {
        foreach (var (d, content) in rows)
        {
            db.TrainerNotes.Add(new TrainerNote
            {
                ClientId = clientId,
                TrainerUserId = trainerUserId,
                Content = content,
                CreatedAt = today.AddDays(d) // d jest ujemne
            });
        }
    }

    private static void AddContact(ApplicationDbContext db, string trainerUserId, int a, int b, DateTime createdAt)
    {
        var min = Math.Min(a, b);
        var max = Math.Max(a, b);
        db.ClientContacts.Add(new ClientContact
        {
            TrainerUserId = trainerUserId,
            Client1Id = min,
            Client2Id = max,
            CreatedAt = createdAt
        });
    }

    // Combine "today (UTC midnight)" + day offset + hour/minute → UTC DateTime
    private static DateTime AtUtc(DateTime today, int daysOffset, int hour, int minute = 0)
        => DateTime.SpecifyKind(today.AddDays(daysOffset).AddHours(hour).AddMinutes(minute), DateTimeKind.Utc);
}
