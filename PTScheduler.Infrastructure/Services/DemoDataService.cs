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

        // 14. Package offers (sellable templates) ---------------------------------
        var offerPersonal10 = new PackageOffer
        {
            Name = "Pakiet Personalny 10", Description = "10 sesji treningowych 1-na-1 z trenerem",
            SessionTypeId = stPersonal.Id, SessionsCount = 10, Price = 1200m,
            ValidDays = 90, IsActive = true, IsFeatured = false, SortOrder = 1,
            CreatedByUserId = trainer.Id, CreatedAt = today.AddDays(-90)
        };
        var offerPersonal20 = new PackageOffer
        {
            Name = "Pakiet Personalny 20", Description = "20 sesji — najlepsza wartość! Oszczędzasz 200 zł.",
            SessionTypeId = stPersonal.Id, SessionsCount = 20, Price = 2200m,
            ValidDays = 180, IsActive = true, IsFeatured = true, SortOrder = 2,
            CreatedByUserId = trainer.Id, CreatedAt = today.AddDays(-90)
        };
        var offerPilates12 = new PackageOffer
        {
            Name = "Pakiet Pilates 12", Description = "12 sesji pilates — elastyczność i siła core",
            SessionTypeId = stPilates.Id, SessionsCount = 12, Price = 1260m,
            ValidDays = 120, IsActive = true, IsFeatured = false, SortOrder = 3,
            CreatedByUserId = trainer.Id, CreatedAt = today.AddDays(-75)
        };
        var offerCardio10 = new PackageOffer
        {
            Name = "Pakiet Cardio HIIT 10", Description = "10 intensywnych sesji cardio",
            SessionTypeId = stCardio.Id, SessionsCount = 10, Price = 900m,
            ValidDays = 90, IsActive = true, IsFeatured = false, SortOrder = 4,
            CreatedByUserId = trainer.Id, CreatedAt = today.AddDays(-60)
        };
        var offerStretch4 = new PackageOffer
        {
            Name = "Pakiet Stretching 4", Description = "4 sesje rozciągające — idealny na start",
            SessionTypeId = stStretch.Id, SessionsCount = 4, Price = 320m,
            ValidDays = 60, IsActive = true, IsFeatured = false, SortOrder = 5,
            CreatedByUserId = trainer.Id, CreatedAt = today.AddDays(-60)
        };
        db.PackageOffers.AddRange(offerPersonal10, offerPersonal20, offerPilates12, offerCardio10, offerStretch4);
        await db.SaveChangesAsync();

        // 15. Coupons -------------------------------------------------------------
        var couponWelcome = new Coupon
        {
            Code = "WELCOME10", Description = "10% rabatu na pierwszy pakiet",
            DiscountType = "percent", DiscountValue = 10m,
            ValidFrom = today.AddDays(-60), ValidUntil = today.AddDays(30),
            MaxUses = 0, UsedCount = 3, Scope = "packages",
            IsActive = true, CreatedAt = today.AddDays(-60)
        };
        var couponSummer = new Coupon
        {
            Code = "LATO2026", Description = "20 zł zniżki — promocja letnia",
            DiscountType = "amount", DiscountValue = 20m,
            ValidFrom = today.AddDays(-30), ValidUntil = today.AddDays(60),
            MaxUses = 50, UsedCount = 7, Scope = "all",
            IsActive = true, CreatedAt = today.AddDays(-30)
        };
        var couponReferral = new Coupon
        {
            Code = "POLECENIE50", Description = "50 zł za polecenie — jednorazowy",
            DiscountType = "amount", DiscountValue = 50m,
            ValidFrom = today.AddDays(-90), ValidUntil = today.AddDays(90),
            MaxUses = 10, UsedCount = 1, Scope = "packages",
            IsActive = true, CreatedAt = today.AddDays(-90)
        };
        var couponExpired = new Coupon
        {
            Code = "WIOSNA25", Description = "15% rabatu — kampania wiosenna (wygasła)",
            DiscountType = "percent", DiscountValue = 15m,
            ValidFrom = today.AddDays(-120), ValidUntil = today.AddDays(-30),
            MaxUses = 20, UsedCount = 5, Scope = "all",
            IsActive = false, CreatedAt = today.AddDays(-120)
        };
        db.Coupons.AddRange(couponWelcome, couponSummer, couponReferral, couponExpired);
        await db.SaveChangesAsync();

        // 16. Orders (payment history) --------------------------------------------
        var clientUserIds = new Dictionary<int, string>
        {
            [cMarek.Id] = (await userManager.FindByEmailAsync("marek@demo.pl"))!.Id,
            [cKatarzyna.Id] = (await userManager.FindByEmailAsync("katarzyna@demo.pl"))!.Id,
            [cTomasz.Id] = (await userManager.FindByEmailAsync("tomasz@demo.pl"))!.Id,
            [cMagdalena.Id] = (await userManager.FindByEmailAsync("magdalena@demo.pl"))!.Id,
            [cRobert.Id] = (await userManager.FindByEmailAsync("robert@demo.pl"))!.Id,
            [cAlicja.Id] = (await userManager.FindByEmailAsync("alicja@demo.pl"))!.Id,
            [cPiotr.Id] = (await userManager.FindByEmailAsync("piotr@demo.pl"))!.Id,
        };

        var orders = new List<Order>
        {
            new()
            {
                ApplicationUserId = clientUserIds[cMarek.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerPersonal10.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 1200m,
                Status = OrderStatus.Paid, Description = "Pakiet Personalny 10 — Marek Nowak",
                CreatedAt = today.AddDays(-95), PaidAt = today.AddDays(-95),
                InvoiceNumber = "FV/2026/001", InvoiceIssuedAt = today.AddDays(-95)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cMarek.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerPersonal20.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 1980m,
                OriginalAmount = 2200m, DiscountAmount = 220m,
                CouponId = couponWelcome.Id, CouponCode = "WELCOME10",
                Status = OrderStatus.Paid, Description = "Pakiet Personalny 20 — Marek Nowak (kupon WELCOME10)",
                CreatedAt = today.AddDays(-50), PaidAt = today.AddDays(-50),
                InvoiceNumber = "FV/2026/002", InvoiceIssuedAt = today.AddDays(-50)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cMarek.Id], Kind = OrderKind.Package,
                Provider = "sim", PackageOfferId = offerStretch4.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 320m,
                Status = OrderStatus.Pending, Description = "Doładowanie stretching (4) — Marek Nowak",
                CreatedAt = today.AddDays(-2)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cKatarzyna.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerPilates12.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 1260m,
                Status = OrderStatus.Paid, Description = "Pakiet Pilates 12 — Katarzyna Zielińska",
                CreatedAt = today.AddDays(-75), PaidAt = today.AddDays(-75),
                InvoiceNumber = "FV/2026/003", InvoiceIssuedAt = today.AddDays(-75)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cPiotr.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerPersonal10.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 1200m,
                Status = OrderStatus.Paid, Description = "Pakiet Standard 10 — Piotr Wiśniewski",
                CreatedAt = today.AddDays(-90), PaidAt = today.AddDays(-90),
                InvoiceNumber = "FV/2026/004", InvoiceIssuedAt = today.AddDays(-90)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cAlicja.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerStretch4.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 300m,
                OriginalAmount = 320m, DiscountAmount = 20m,
                CouponId = couponSummer.Id, CouponCode = "LATO2026",
                Status = OrderStatus.Paid, Description = "Pakiet próbny stretching — Alicja Kowalska (kupon LATO2026)",
                CreatedAt = today.AddDays(-8), PaidAt = today.AddDays(-6),
                InvoiceNumber = "FV/2026/005", InvoiceIssuedAt = today.AddDays(-6)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cTomasz.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerPersonal10.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 1200m,
                Status = OrderStatus.Paid, Description = "Pakiet Siłowy 15 — Tomasz Lewandowski",
                CreatedAt = today.AddDays(-95), PaidAt = today.AddDays(-95),
                InvoiceNumber = "FV/2026/006", InvoiceIssuedAt = today.AddDays(-95)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cTomasz.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerCardio10.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 900m,
                Status = OrderStatus.Paid, Description = "Pakiet Cardio HIIT 10 — Tomasz Lewandowski",
                CreatedAt = today.AddDays(-40), PaidAt = today.AddDays(-40),
                InvoiceNumber = "FV/2026/007", InvoiceIssuedAt = today.AddDays(-40)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cMagdalena.Id], Kind = OrderKind.Package,
                Provider = "p24", PackageOfferId = offerPilates12.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 1210m,
                OriginalAmount = 1260m, DiscountAmount = 50m,
                CouponId = couponReferral.Id, CouponCode = "POLECENIE50",
                Status = OrderStatus.Paid, Description = "Pakiet Body 12 — Magdalena Dąbrowska (polecenie)",
                CreatedAt = today.AddDays(-60), PaidAt = today.AddDays(-58),
                InvoiceNumber = "FV/2026/008", InvoiceIssuedAt = today.AddDays(-58)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cRobert.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerPersonal10.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 1200m,
                Status = OrderStatus.Paid, Description = "Pakiet Siłowy Duet 15 — Robert Kamiński",
                CreatedAt = today.AddDays(-95), PaidAt = today.AddDays(-95),
                InvoiceNumber = "FV/2026/009", InvoiceIssuedAt = today.AddDays(-95)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cPiotr.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerPersonal20.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 2200m,
                Status = OrderStatus.Canceled, Description = "Pakiet Personalny 20 — Piotr (anulowany)",
                CreatedAt = today.AddDays(-15)
            },
            new()
            {
                ApplicationUserId = clientUserIds[cKatarzyna.Id], Kind = OrderKind.Package,
                Provider = "payu", PackageOfferId = offerPilates12.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 1260m,
                Status = OrderStatus.Failed, Description = "Pakiet Pilates 12 — Katarzyna (nieudana płatność)",
                CreatedAt = today.AddDays(-10)
            },
        };
        db.Orders.AddRange(orders);

        // Coupon redemptions matching the orders above
        db.CouponRedemptions.AddRange(
            new CouponRedemption
            {
                CouponId = couponWelcome.Id, UserId = clientUserIds[cMarek.Id],
                UserEmail = "marek@demo.pl", OriginalAmount = 2200m,
                DiscountAmount = 220m, FinalAmount = 1980m,
                TargetType = "package", TargetId = offerPersonal20.Id,
                RedeemedAt = today.AddDays(-50)
            },
            new CouponRedemption
            {
                CouponId = couponSummer.Id, UserId = clientUserIds[cAlicja.Id],
                UserEmail = "alicja@demo.pl", OriginalAmount = 320m,
                DiscountAmount = 20m, FinalAmount = 300m,
                TargetType = "package", TargetId = offerStretch4.Id,
                RedeemedAt = today.AddDays(-8)
            },
            new CouponRedemption
            {
                CouponId = couponReferral.Id, UserId = clientUserIds[cMagdalena.Id],
                UserEmail = "magdalena@demo.pl", OriginalAmount = 1260m,
                DiscountAmount = 50m, FinalAmount = 1210m,
                TargetType = "package", TargetId = offerPilates12.Id,
                RedeemedAt = today.AddDays(-60)
            });

        await db.SaveChangesAsync();

        // 17. Course with modules, lessons, quizzes --------------------------------
        var course = new Course
        {
            Title = "Trening siłowy dla początkujących",
            Description = "Kompletny kurs techniki ćwiczeń siłowych — od podstaw do samodzielnego treningu.",
            DescriptionHtml = """
                <h2>Dla kogo jest ten kurs?</h2>
                <p>Jeśli dopiero zaczynasz przygodę z siłownią i chcesz nauczyć się poprawnej techniki,
                ten kurs jest dla Ciebie. W 8 lekcjach poznasz najważniejsze wzorce ruchowe,
                nauczysz się planować trening i unikać kontuzji.</p>
                <h2>Co otrzymasz?</h2>
                <ul>
                  <li>8 lekcji wideo z szczegółowym omówieniem techniki</li>
                  <li>Materiały PDF do wydrukowania</li>
                  <li>Quizy sprawdzające wiedzę po każdym module</li>
                  <li>Dostęp do trenera na czacie</li>
                </ul>
                """,
            DurationText = "4 tygodnie · 8 lekcji",
            Level = "Początkujący",
            Author = "Jan Kowalski",
            IsPublished = true,
            Price = 149m,
            DefaultAccessType = CourseAccessType.Lifetime,
            SortOrder = 1,
            CreatedAt = today.AddDays(-45)
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        // Module 1: Podstawy
        var mod1 = new CourseModule { CourseId = course.Id, Title = "Moduł 1: Podstawy treningu siłowego", SortOrder = 1 };
        var mod2 = new CourseModule { CourseId = course.Id, Title = "Moduł 2: Kluczowe ćwiczenia", SortOrder = 2 };
        var mod3 = new CourseModule { CourseId = course.Id, Title = "Moduł 3: Planowanie i progresja", SortOrder = 3 };
        db.CourseModules.AddRange(mod1, mod2, mod3);
        await db.SaveChangesAsync();

        // Lessons
        var lesson1 = new Lesson
        {
            ModuleId = mod1.Id, Title = "Dlaczego trening siłowy?", SortOrder = 1,
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ContentHtml = "<p>W tej lekcji dowiesz się, dlaczego trening siłowy jest fundamentem zdrowia i sprawności fizycznej.</p>"
        };
        var lesson2 = new Lesson
        {
            ModuleId = mod1.Id, Title = "Rozgrzewka i bezpieczeństwo", SortOrder = 2,
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ContentHtml = "<p>Prawidłowa rozgrzewka zapobiega kontuzjom. Poznasz schemat rozgrzewki na każdy trening.</p>"
        };
        var lesson3 = new Lesson
        {
            ModuleId = mod1.Id, Title = "Sprzęt i wyposażenie", SortOrder = 3,
            ContentHtml = "<p>Co potrzebujesz na start? Omawiamy buty, pasy, paski, magnezję i inne akcesoria.</p>"
        };
        var lesson4 = new Lesson
        {
            ModuleId = mod2.Id, Title = "Przysiad (Squat)", SortOrder = 1,
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ContentHtml = "<p>Król ćwiczeń. Technika high-bar i low-bar, najczęstsze błędy, progresja obciążeń.</p>"
        };
        var lesson5 = new Lesson
        {
            ModuleId = mod2.Id, Title = "Wyciskanie (Bench Press)", SortOrder = 2,
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ContentHtml = "<p>Technika wyciskania na ławce płaskiej — ustawienie, łopatki, tor ruchu sztangi.</p>"
        };
        var lesson6 = new Lesson
        {
            ModuleId = mod2.Id, Title = "Martwy ciąg (Deadlift)", SortOrder = 3,
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ContentHtml = "<p>Konwencjonalny vs sumo. Pozycja startowa, napięcie pleców, lock-out.</p>"
        };
        var lesson7 = new Lesson
        {
            ModuleId = mod3.Id, Title = "Jak ułożyć plan treningowy", SortOrder = 1,
            ContentHtml = "<p>Split treningowy, objętość, intensywność. Planowanie mikrocykli i mezocykli.</p>"
        };
        var lesson8 = new Lesson
        {
            ModuleId = mod3.Id, Title = "Progresja i deload", SortOrder = 2,
            ContentHtml = "<p>Kiedy dodawać obciążenie, kiedy odpocząć. Sygnały przeciążenia i planowanie deloadów.</p>"
        };
        db.Lessons.AddRange(lesson1, lesson2, lesson3, lesson4, lesson5, lesson6, lesson7, lesson8);
        await db.SaveChangesAsync();

        // Quizzes for key lessons
        var quiz1 = new QuizQuestion
        {
            LessonId = lesson2.Id, Text = "Ile powinna trwać rozgrzewka przed treningiem siłowym?",
            Type = QuizQuestionType.SingleChoice, SortOrder = 1
        };
        var quiz2 = new QuizQuestion
        {
            LessonId = lesson2.Id, Text = "Które elementy powinna zawierać rozgrzewka?",
            Type = QuizQuestionType.MultipleChoice, SortOrder = 2
        };
        var quiz3 = new QuizQuestion
        {
            LessonId = lesson4.Id, Text = "W przysiadzie high-bar sztanga spoczywa na:",
            Type = QuizQuestionType.SingleChoice, SortOrder = 1
        };
        var quiz4 = new QuizQuestion
        {
            LessonId = lesson5.Id, Text = "Podczas wyciskania łopatki powinny być:",
            Type = QuizQuestionType.SingleChoice, SortOrder = 1
        };
        var quiz5 = new QuizQuestion
        {
            LessonId = lesson8.Id, Text = "Kiedy najlepiej zaplanować tydzień deload?",
            Type = QuizQuestionType.SingleChoice, SortOrder = 1
        };
        db.QuizQuestions.AddRange(quiz1, quiz2, quiz3, quiz4, quiz5);
        await db.SaveChangesAsync();

        // Quiz options
        db.QuizOptions.AddRange(
            // quiz1 - rozgrzewka czas
            new QuizOption { QuestionId = quiz1.Id, Text = "2-3 minuty", IsCorrect = false, SortOrder = 1 },
            new QuizOption { QuestionId = quiz1.Id, Text = "10-15 minut", IsCorrect = true, SortOrder = 2 },
            new QuizOption { QuestionId = quiz1.Id, Text = "30 minut", IsCorrect = false, SortOrder = 3 },
            new QuizOption { QuestionId = quiz1.Id, Text = "Można pominąć", IsCorrect = false, SortOrder = 4 },
            // quiz2 - elementy rozgrzewki (multi)
            new QuizOption { QuestionId = quiz2.Id, Text = "Cardio niskointensywne", IsCorrect = true, SortOrder = 1 },
            new QuizOption { QuestionId = quiz2.Id, Text = "Mobilność stawów", IsCorrect = true, SortOrder = 2 },
            new QuizOption { QuestionId = quiz2.Id, Text = "Serie rozgrzewkowe z lekkim ciężarem", IsCorrect = true, SortOrder = 3 },
            new QuizOption { QuestionId = quiz2.Id, Text = "Statyczny stretching", IsCorrect = false, SortOrder = 4 },
            // quiz3 - przysiad high-bar
            new QuizOption { QuestionId = quiz3.Id, Text = "Na górze trapezów", IsCorrect = true, SortOrder = 1 },
            new QuizOption { QuestionId = quiz3.Id, Text = "Na tylnych deltach", IsCorrect = false, SortOrder = 2 },
            new QuizOption { QuestionId = quiz3.Id, Text = "Na szyi", IsCorrect = false, SortOrder = 3 },
            // quiz4 - łopatki bench
            new QuizOption { QuestionId = quiz4.Id, Text = "Rozluźnione", IsCorrect = false, SortOrder = 1 },
            new QuizOption { QuestionId = quiz4.Id, Text = "Ściągnięte i obniżone", IsCorrect = true, SortOrder = 2 },
            new QuizOption { QuestionId = quiz4.Id, Text = "Wysunięte do przodu", IsCorrect = false, SortOrder = 3 },
            // quiz5 - deload
            new QuizOption { QuestionId = quiz5.Id, Text = "Co drugi tydzień", IsCorrect = false, SortOrder = 1 },
            new QuizOption { QuestionId = quiz5.Id, Text = "Co 4-6 tygodni lub przy objawach zmęczenia", IsCorrect = true, SortOrder = 2 },
            new QuizOption { QuestionId = quiz5.Id, Text = "Nigdy, deload spowalnia postępy", IsCorrect = false, SortOrder = 3 },
            new QuizOption { QuestionId = quiz5.Id, Text = "Tylko po kontuzji", IsCorrect = false, SortOrder = 4 });
        await db.SaveChangesAsync();

        // Second course (shorter, published)
        var course2 = new Course
        {
            Title = "Mobilność i regeneracja",
            Description = "Stretching, foam rolling i techniki oddechowe dla lepszej regeneracji.",
            DurationText = "2 tygodnie · 5 lekcji",
            Level = "Każdy poziom",
            Author = "Jan Kowalski",
            IsPublished = true,
            Price = 79m,
            DefaultAccessType = CourseAccessType.Timed,
            DefaultAccessDays = 90,
            SortOrder = 2,
            CreatedAt = today.AddDays(-20)
        };
        db.Courses.Add(course2);
        await db.SaveChangesAsync();

        var mod2a = new CourseModule { CourseId = course2.Id, Title = "Teoria regeneracji", SortOrder = 1 };
        var mod2b = new CourseModule { CourseId = course2.Id, Title = "Praktyka", SortOrder = 2 };
        db.CourseModules.AddRange(mod2a, mod2b);
        await db.SaveChangesAsync();

        var les2a = new Lesson { ModuleId = mod2a.Id, Title = "Dlaczego regeneracja jest ważna?", SortOrder = 1,
            ContentHtml = "<p>Sen, stres, odżywianie — trzy filary regeneracji sportowej.</p>" };
        var les2b = new Lesson { ModuleId = mod2a.Id, Title = "Foam rolling — techniki", SortOrder = 2,
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ContentHtml = "<p>Roller na każdą partię mięśniową — uda, plecy, pośladki, łydki.</p>" };
        var les2c = new Lesson { ModuleId = mod2b.Id, Title = "Rozciąganie dynamiczne", SortOrder = 1,
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ContentHtml = "<p>20-minutowa rutyna rozciągania dynamicznego na cały dzień.</p>" };
        var les2d = new Lesson { ModuleId = mod2b.Id, Title = "Oddychanie przeponowe", SortOrder = 2,
            ContentHtml = "<p>Technika box breathing i oddychanie przeponowe dla redukcji stresu.</p>" };
        var les2e = new Lesson { ModuleId = mod2b.Id, Title = "Plan regeneracji na tydzień", SortOrder = 3,
            ContentHtml = "<p>Gotowy szablon tygodniowy — kiedy stretching, kiedy foam rolling, kiedy odpoczynek.</p>" };
        db.Lessons.AddRange(les2a, les2b, les2c, les2d, les2e);
        await db.SaveChangesAsync();

        // 18. Course enrollments + progress ----------------------------------------
        // Marek: enrolled in course 1, completed 6/8 lessons
        var enrollMarek = new CourseEnrollment
        {
            CourseId = course.Id, ApplicationUserId = clientUserIds[cMarek.Id],
            AccessType = CourseAccessType.Lifetime, Source = EnrollmentSource.Purchase,
            GrantedAt = today.AddDays(-30), GrantedByUserId = trainer.Id
        };
        // Katarzyna: enrolled in both courses
        var enrollKat1 = new CourseEnrollment
        {
            CourseId = course.Id, ApplicationUserId = clientUserIds[cKatarzyna.Id],
            AccessType = CourseAccessType.Lifetime, Source = EnrollmentSource.Purchase,
            GrantedAt = today.AddDays(-25), GrantedByUserId = trainer.Id
        };
        var enrollKat2 = new CourseEnrollment
        {
            CourseId = course2.Id, ApplicationUserId = clientUserIds[cKatarzyna.Id],
            AccessType = CourseAccessType.Timed, Source = EnrollmentSource.Purchase,
            GrantedAt = today.AddDays(-15), ExpiresAt = today.AddDays(75),
            GrantedByUserId = trainer.Id
        };
        // Tomasz: enrolled in course 1, completed all lessons
        var enrollTomasz = new CourseEnrollment
        {
            CourseId = course.Id, ApplicationUserId = clientUserIds[cTomasz.Id],
            AccessType = CourseAccessType.Lifetime, Source = EnrollmentSource.Purchase,
            GrantedAt = today.AddDays(-40), GrantedByUserId = trainer.Id
        };
        // Alicja: trial enrollment in course 2
        var enrollAlicja = new CourseEnrollment
        {
            CourseId = course2.Id, ApplicationUserId = clientUserIds[cAlicja.Id],
            AccessType = CourseAccessType.Trial, Source = EnrollmentSource.Manual,
            GrantedAt = today.AddDays(-5), ExpiresAt = today.AddDays(9),
            GrantedByUserId = trainer.Id, Notes = "Darmowy dostęp próbny dla nowej klientki"
        };
        db.CourseEnrollments.AddRange(enrollMarek, enrollKat1, enrollKat2, enrollTomasz, enrollAlicja);
        await db.SaveChangesAsync();

        // Course orders for enrollments
        db.Orders.AddRange(
            new Order
            {
                ApplicationUserId = clientUserIds[cMarek.Id], Kind = OrderKind.Course,
                Provider = "payu", CourseId = course.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 149m,
                Status = OrderStatus.Paid, Description = "Kurs: Trening siłowy — Marek Nowak",
                CreatedAt = today.AddDays(-30), PaidAt = today.AddDays(-30),
                InvoiceNumber = "FV/2026/010", InvoiceIssuedAt = today.AddDays(-30)
            },
            new Order
            {
                ApplicationUserId = clientUserIds[cKatarzyna.Id], Kind = OrderKind.Course,
                Provider = "payu", CourseId = course.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 129m,
                OriginalAmount = 149m, DiscountAmount = 20m,
                CouponId = couponSummer.Id, CouponCode = "LATO2026",
                Status = OrderStatus.Paid, Description = "Kurs: Trening siłowy — Katarzyna (kupon LATO2026)",
                CreatedAt = today.AddDays(-25), PaidAt = today.AddDays(-25),
                InvoiceNumber = "FV/2026/011", InvoiceIssuedAt = today.AddDays(-25)
            },
            new Order
            {
                ApplicationUserId = clientUserIds[cKatarzyna.Id], Kind = OrderKind.Course,
                Provider = "p24", CourseId = course2.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 79m,
                Status = OrderStatus.Paid, Description = "Kurs: Mobilność i regeneracja — Katarzyna",
                CreatedAt = today.AddDays(-15), PaidAt = today.AddDays(-15),
                InvoiceNumber = "FV/2026/012", InvoiceIssuedAt = today.AddDays(-15)
            },
            new Order
            {
                ApplicationUserId = clientUserIds[cTomasz.Id], Kind = OrderKind.Course,
                Provider = "payu", CourseId = course.Id,
                ExtOrderId = $"ORD-{Guid.NewGuid():N}"[..20], Amount = 149m,
                Status = OrderStatus.Paid, Description = "Kurs: Trening siłowy — Tomasz Lewandowski",
                CreatedAt = today.AddDays(-40), PaidAt = today.AddDays(-40),
                InvoiceNumber = "FV/2026/013", InvoiceIssuedAt = today.AddDays(-40)
            });
        await db.SaveChangesAsync();

        // Lesson progress — Marek: 6/8 done
        db.LessonProgress.AddRange(
            new LessonProgress { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson1.Id, CompletedAt = today.AddDays(-28) },
            new LessonProgress { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson2.Id, CompletedAt = today.AddDays(-26) },
            new LessonProgress { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson3.Id, CompletedAt = today.AddDays(-24) },
            new LessonProgress { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson4.Id, CompletedAt = today.AddDays(-20) },
            new LessonProgress { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson5.Id, CompletedAt = today.AddDays(-16) },
            new LessonProgress { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson6.Id, CompletedAt = today.AddDays(-12) });

        // Katarzyna: 4/8 in course 1, 2/5 in course 2
        db.LessonProgress.AddRange(
            new LessonProgress { ApplicationUserId = clientUserIds[cKatarzyna.Id], LessonId = lesson1.Id, CompletedAt = today.AddDays(-23) },
            new LessonProgress { ApplicationUserId = clientUserIds[cKatarzyna.Id], LessonId = lesson2.Id, CompletedAt = today.AddDays(-21) },
            new LessonProgress { ApplicationUserId = clientUserIds[cKatarzyna.Id], LessonId = lesson3.Id, CompletedAt = today.AddDays(-18) },
            new LessonProgress { ApplicationUserId = clientUserIds[cKatarzyna.Id], LessonId = lesson4.Id, CompletedAt = today.AddDays(-14) },
            new LessonProgress { ApplicationUserId = clientUserIds[cKatarzyna.Id], LessonId = les2a.Id, CompletedAt = today.AddDays(-12) },
            new LessonProgress { ApplicationUserId = clientUserIds[cKatarzyna.Id], LessonId = les2b.Id, CompletedAt = today.AddDays(-10) });

        // Tomasz: all 8/8 done
        db.LessonProgress.AddRange(
            new LessonProgress { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson1.Id, CompletedAt = today.AddDays(-38) },
            new LessonProgress { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson2.Id, CompletedAt = today.AddDays(-36) },
            new LessonProgress { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson3.Id, CompletedAt = today.AddDays(-34) },
            new LessonProgress { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson4.Id, CompletedAt = today.AddDays(-30) },
            new LessonProgress { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson5.Id, CompletedAt = today.AddDays(-26) },
            new LessonProgress { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson6.Id, CompletedAt = today.AddDays(-22) },
            new LessonProgress { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson7.Id, CompletedAt = today.AddDays(-18) },
            new LessonProgress { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson8.Id, CompletedAt = today.AddDays(-14) });

        // Alicja: 1/5 in course 2
        db.LessonProgress.Add(
            new LessonProgress { ApplicationUserId = clientUserIds[cAlicja.Id], LessonId = les2a.Id, CompletedAt = today.AddDays(-3) });

        // Quiz attempts
        db.QuizAttempts.AddRange(
            new QuizAttempt { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson2.Id, ScorePercent = 100, Passed = true, AttemptedAt = today.AddDays(-26) },
            new QuizAttempt { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson4.Id, ScorePercent = 100, Passed = true, AttemptedAt = today.AddDays(-20) },
            new QuizAttempt { ApplicationUserId = clientUserIds[cMarek.Id], LessonId = lesson5.Id, ScorePercent = 100, Passed = true, AttemptedAt = today.AddDays(-16) },
            new QuizAttempt { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson2.Id, ScorePercent = 100, Passed = true, AttemptedAt = today.AddDays(-36) },
            new QuizAttempt { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson4.Id, ScorePercent = 100, Passed = true, AttemptedAt = today.AddDays(-30) },
            new QuizAttempt { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson5.Id, ScorePercent = 100, Passed = true, AttemptedAt = today.AddDays(-26) },
            new QuizAttempt { ApplicationUserId = clientUserIds[cTomasz.Id], LessonId = lesson8.Id, ScorePercent = 100, Passed = true, AttemptedAt = today.AddDays(-14) },
            new QuizAttempt { ApplicationUserId = clientUserIds[cKatarzyna.Id], LessonId = lesson2.Id, ScorePercent = 50, Passed = false, AttemptedAt = today.AddDays(-21) },
            new QuizAttempt { ApplicationUserId = clientUserIds[cKatarzyna.Id], LessonId = lesson4.Id, ScorePercent = 100, Passed = true, AttemptedAt = today.AddDays(-14) });
        await db.SaveChangesAsync();

        // 19. Payment settings (sandbox/demo mode) ---------------------------------
        if (!await db.PaymentSettings.AnyAsync())
        {
            db.PaymentSettings.Add(new PaymentSettings
            {
                Enabled = true,
                Sandbox = true,
                Currency = "PLN",
                ProvidersJson = """{"sim":{"enabled":true,"sandbox":true},"payu":{"enabled":true,"sandbox":true,"fields":{"posId":"300746","secondKey":"b6ca15b0d1020e8094f2b5571c1670c","clientId":"300746","clientSecret":"2ee86a66e5d97e3fadc400c9f19b065d"}},"p24":{"enabled":true,"sandbox":true,"fields":{"merchantId":"12345","crc":"demo-crc-key","apiKey":"demo-api-key"}}}"""
            });
        }

        // 20. Finance/tax config ---------------------------------------------------
        if (!await db.FinanceTaxConfigs.AnyAsync())
        {
            db.FinanceTaxConfigs.Add(new FinanceTaxConfig
            {
                Module = "standard",
                VatEnabled = false,
                IncomeTaxType = "lumpsum",
                LumpSumRate = 8.5m,
                ZusEnabled = true, ZusMonthlyAmount = 1600.32m,
                HealthInsuranceEnabled = true, HealthInsuranceMonthly = 381.78m,
                CostDeductionsEnabled = true, MonthlyFixedCosts = 250m,
                InvoiceNumberingEnabled = true, InvoicePrefix = "FV",
                InvoiceNextNumber = 14,
                SellerNip = "1234567890",
                SellerAddress = "ul. Sportowa 15/3",
                SellerCity = "Warszawa",
                SellerPostalCode = "00-001"
            });
        }

        // 21. Branding (demo setup completed) --------------------------------------
        var branding = await db.AppBrandings.FirstOrDefaultAsync();
        if (branding is null)
        {
            db.AppBrandings.Add(new AppBranding
            {
                ThemeName = "violet",
                ThemeMode = "system",
                CompanyName = "Jan Kowalski Fitness",
                PwaShortName = "JK Fitness",
                PwaBannerEnabled = true,
                PwaBannerTitle = "Zainstaluj aplikację",
                PwaBannerBody = "Dodaj JK Fitness do ekranu głównego dla szybkiego dostępu.",
                PwaBannerButton = "Zainstaluj",
                SetupCompleted = true,
                SetupMode = "demo",
                SetupCompletedAt = today.AddDays(-90)
            });
        }

        // 22. Login logs (recent history) ------------------------------------------
        var loginEntries = new List<LoginLog>();
        // Trainer logs in daily for the last 14 days
        for (int d = 13; d >= 0; d--)
        {
            loginEntries.Add(new LoginLog
            {
                UserId = trainer.Id,
                LoginTime = today.AddDays(-d).AddHours(7).AddMinutes(15 + d % 10),
                IpAddress = "192.168.1.100",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/127.0",
                Success = true
            });
        }
        // Client logins scattered
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cMarek.Id], LoginTime = today.AddDays(-1).AddHours(18), IpAddress = "83.24.56.78", UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5) Safari/605.1.15", Success = true });
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cMarek.Id], LoginTime = today.AddDays(-3).AddHours(20), IpAddress = "83.24.56.78", UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5) Safari/605.1.15", Success = true });
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cKatarzyna.Id], LoginTime = today.AddDays(-2).AddHours(16), IpAddress = "89.73.12.45", UserAgent = "Mozilla/5.0 (Linux; Android 14) Chrome/127.0", Success = true });
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cTomasz.Id], LoginTime = today.AddDays(-1).AddHours(19), IpAddress = "78.11.23.99", UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) Safari/605.1.15", Success = true });
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cTomasz.Id], LoginTime = today.AddDays(-4).AddHours(8), IpAddress = "78.11.23.99", UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) Safari/605.1.15", Success = true });
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cMagdalena.Id], LoginTime = today.AddDays(-5).AddHours(14), IpAddress = "156.17.88.12", UserAgent = "Mozilla/5.0 (Linux; Android 14) Chrome/127.0", Success = true });
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cAlicja.Id], LoginTime = today.AddDays(-3).AddHours(9), IpAddress = "91.215.34.67", UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5) Safari/605.1.15", Success = true });
        // Failed logins
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cPiotr.Id], LoginTime = today.AddDays(-7).AddHours(22), IpAddress = "45.56.78.90", UserAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/127.0", Success = false });
        loginEntries.Add(new LoginLog { UserId = clientUserIds[cPiotr.Id], LoginTime = today.AddDays(-7).AddHours(22).AddMinutes(2), IpAddress = "45.56.78.90", UserAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/127.0", Success = true });
        db.LoginLogs.AddRange(loginEntries);

        // 23. Email templates (2 customized by trainer) ----------------------------
        db.EmailTemplates.AddRange(
            new EmailTemplate
            {
                Key = "session-reminder",
                Subject = "Przypomnienie — jutro trening u Jana!",
                HeaderTitle = "Do zobaczenia jutro! 💪",
                HtmlBody = """
                    <p style="color:#374151;font-size:15px">Hej <strong>{{ClientName}}</strong>!</p>
                    <p style="color:#374151;font-size:15px">Tylko przypominam — jutro widzimy się na treningu. Przygotuj się na solidną dawkę endorfin! 🔥</p>
                    <div style="background:#f3f4f6;border-radius:8px;padding:16px;margin:20px 0">
                      <p style="margin:6px 0"><strong>📅 Kiedy:</strong> {{SessionDate}} o {{SessionTime}}</p>
                      <p style="margin:6px 0"><strong>⏱️ Czas:</strong> {{Duration}} min</p>
                      <p style="margin:6px 0"><strong>🏋️ Typ:</strong> {{SessionType}}</p>
                    </div>
                    <p style="color:#6b7280;font-size:13px">Jeśli nie dasz rady — odwołaj w aplikacji minimum 24h wcześniej.</p>
                    """,
                AccentColor = "#7C3AED",
                FooterText = "Jan Kowalski Fitness — wiadomość automatyczna.",
                UpdatedAt = today.AddDays(-60)
            },
            new EmailTemplate
            {
                Key = "package-assigned",
                Subject = "Nowy pakiet treningowy czeka na Ciebie!",
                HeaderTitle = "Masz nowy pakiet! 🎉",
                HtmlBody = """
                    <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
                    <p style="color:#374151;font-size:15px">Właśnie przypisałem Ci nowy pakiet treningowy. Czas wziąć się do roboty! 💪</p>
                    <table style="width:100%;border-collapse:collapse;margin:16px 0">
                      <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Pakiet</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{PackageName}}</td></tr>
                      <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Typ sesji</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionType}}</td></tr>
                      <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Sesji do wykorzystania</td><td style="padding:8px 0;font-size:14px;font-weight:600;color:#16A34A">{{TotalSessions}}</td></tr>
                      {{ExpiresRow}}
                    </table>
                    <p style="color:#374151;font-size:14px">Zarezerwuj pierwszy trening w aplikacji!</p>
                    """,
                AccentColor = "#16A34A",
                FooterText = "Jan Kowalski Fitness — wiadomość automatyczna.",
                UpdatedAt = today.AddDays(-55)
            });

        // 24. More audit log entries for payments/courses --------------------------
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Timestamp = today.AddDays(-50), UserId = trainer.Id,
                UserEmail = TrainerEmail, UserRole = Roles.Trainer,
                Action = "OrderPaid", EntityType = nameof(Order),
                Details = "Zamówienie opłacone: Pakiet Personalny 20 — Marek Nowak (2 200 zł → 1 980 zł, kupon WELCOME10)",
                Severity = AuditSeverity.Info
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-45), UserId = trainer.Id,
                UserEmail = TrainerEmail, UserRole = Roles.Trainer,
                Action = "CourseCreated", EntityType = nameof(Course),
                Details = "Utworzono kurs: Trening siłowy dla początkujących (149 zł)",
                Severity = AuditSeverity.Info
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-30), UserId = trainer.Id,
                UserEmail = TrainerEmail, UserRole = Roles.Trainer,
                Action = "CourseEnrollment", EntityType = nameof(CourseEnrollment),
                Details = "Zapisano na kurs: Marek Nowak → Trening siłowy dla początkujących",
                Severity = AuditSeverity.Info
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-20), UserId = trainer.Id,
                UserEmail = TrainerEmail, UserRole = Roles.Trainer,
                Action = "CouponCreated", EntityType = nameof(Coupon),
                Details = "Utworzono kupon: LATO2026 (20 zł zniżki)",
                Severity = AuditSeverity.Info
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-15), UserId = trainer.Id,
                UserEmail = TrainerEmail, UserRole = Roles.Trainer,
                Action = "OrderPaid", EntityType = nameof(Order),
                Details = "Zamówienie opłacone: Kurs Mobilność i regeneracja — Katarzyna Zielińska (79 zł)",
                Severity = AuditSeverity.Info
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-10), UserId = trainer.Id,
                UserEmail = TrainerEmail, UserRole = Roles.Trainer,
                Action = "PaymentFailed", EntityType = nameof(Order),
                Details = "Nieudana płatność: Pakiet Pilates 12 — Katarzyna Zielińska",
                Severity = AuditSeverity.Warning
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-7), UserId = clientUserIds[cPiotr.Id],
                UserEmail = "piotr@demo.pl", UserRole = Roles.Client,
                Action = "LoginFailed", EntityType = "User",
                Details = "Nieudana próba logowania z IP 45.56.78.90",
                Severity = AuditSeverity.Warning
            },
            new AuditLog
            {
                Timestamp = today.AddDays(-5), UserId = trainer.Id,
                UserEmail = TrainerEmail, UserRole = Roles.Trainer,
                Action = "BrandingUpdated", EntityType = nameof(AppBranding),
                Details = "Zmieniono branding: motyw violet, nazwa 'Jan Kowalski Fitness'",
                Severity = AuditSeverity.Info
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
        await db.LoginLogs.ExecuteDeleteAsync();
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
        await db.RolePermissions.ExecuteDeleteAsync();

        // Payments / commerce — MUSI iść przed Courses: Order ma FK do Courses
        // (a także PackageOffers i Coupons), więc Orders trzeba skasować pierwsze,
        // inaczej DELETE na Courses narusza FK_Orders_Courses_CourseId (23503).
        // CouponRedemption ma FK do Coupon, więc idzie przed Coupons.
        await db.CouponRedemptions.ExecuteDeleteAsync();
        await db.Orders.ExecuteDeleteAsync();
        await db.Coupons.ExecuteDeleteAsync();
        await db.PackageOffers.ExecuteDeleteAsync();

        // Courses / LMS
        await db.QuizAttempts.ExecuteDeleteAsync();
        await db.LessonProgress.ExecuteDeleteAsync();
        await db.QuizOptions.ExecuteDeleteAsync();
        await db.QuizQuestions.ExecuteDeleteAsync();
        await db.Lessons.ExecuteDeleteAsync();
        await db.CourseModules.ExecuteDeleteAsync();
        await db.CourseEnrollments.ExecuteDeleteAsync();
        await db.Courses.ExecuteDeleteAsync();

        // Settings (reset to unconfigured)
        await db.EmailTemplates.ExecuteDeleteAsync();
        await db.PaymentSettings.ExecuteDeleteAsync();
        await db.FinanceTaxConfigs.ExecuteDeleteAsync();
        await db.AppBrandings.ExecuteDeleteAsync();

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
