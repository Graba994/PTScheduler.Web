using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using PTScheduler.Web.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Pakiety od 2026-11: katalog funkcji, podłoga obecnych klientów, zablokowane ceny i okres próbny Pro na start.</summary>
public class PlansV2Tests
{
    private static readonly HashSet<string> NotFeatures = ["SortOrder", "TrialDays", "IsActive", "IsFeatured"];

    [Fact]
    public void Catalog_covers_every_plan_field_with_matching_type()
    {
        foreach (var f in PlanFeatureCatalog.All)
        {
            var prop = typeof(Plan).GetProperty(f.Key);
            prop.Should().NotBeNull($"{f.Key} musi być polem planu");
            prop!.PropertyType.Should().Be(f.Kind switch { PlanFeatureKind.Flag => typeof(bool), PlanFeatureKind.Limit => typeof(int), _ => typeof(string) }, f.Key);
            PlanFeatureCatalog.Groups.Should().Contain(f.Group);
        }
        // Nowe pole planu bez wpisu w katalogu nie trafiłoby do panelu — ten test to wychwyci.
        var catalogued = PlanFeatureCatalog.All.Select(f => f.Key).ToHashSet();
        typeof(Plan).GetProperties()
            .Where(p => (p.PropertyType == typeof(bool) || p.PropertyType == typeof(int)) && !NotFeatures.Contains(p.Name))
            .Select(p => p.Name).Should().OnlyContain(n => catalogued.Contains(n));
        // Pola uprawnień muszą istnieć po stronie studia (te same nazwy).
        foreach (var f in PlanFeatureCatalog.All)
            typeof(Entitlements).GetProperty(f.Key).Should().NotBeNull($"studio musi znać uprawnienie {f.Key}");
    }

    [Fact]
    public void Floor_keeps_what_existing_studios_had_but_plan_can_give_more()
    {
        var solo = new Plan { Id = "starter", Name = "Solo", MaxClients = 20, Coupons = false, SalesTools = false, BrandingTier = "basic", MaxTrainers = 0, PaymentsEnabled = true };
        const string floor = """{"Coupons":true,"MaxClients":15,"MaxTrainers":2,"BrandingTier":"full","SalesTools":true,"PaymentsEnabled":false,"Unknown":1}""";

        var effective = PlanFeatureCatalog.ApplyFloor(solo, floor);

        effective.Coupons.Should().BeTrue("miał kupony przed zmianą pakietów");
        effective.SalesTools.Should().BeTrue();
        effective.MaxClients.Should().Be(20, "nowy plan daje więcej niż dawny");
        effective.MaxTrainers.Should().Be(2);
        effective.BrandingTier.Should().Be("full");
        effective.PaymentsEnabled.Should().BeTrue("podłoga niczego nie zabiera");
        effective.Name.Should().Be("Solo");
        solo.Coupons.Should().BeFalse("plan w bazie zostaje bez zmian");
        PlanFeatureCatalog.ApplyFloor(solo, "nie-json").Should().BeSameAs(solo);
    }

    [Fact]
    public void Gains_list_only_what_target_plan_adds_and_granting_them_reaches_target()
    {
        var solo = new Plan { Id = "starter", Name = "Solo", MaxClients = 20, AiAssistant = false, Coupons = true, BrandingTier = "basic" };
        var pro = new Plan { Id = "pro", Name = "Pro", MaxClients = 75, AiAssistant = true, Coupons = true, BrandingTier = "full" };

        var gains = PlanFeatureCatalog.Gains(solo, pro).ToDictionary(g => g.Feature.Key, g => g.Value);

        gains.Should().ContainKeys("MaxClients", "AiAssistant", "BrandingTier");
        gains.Should().NotContainKey("Coupons");
        gains["MaxClients"].Should().Be(75);
        PlanFeatureCatalog.Gains(pro, solo).Should().BeEmpty();

        var now = DateTime.UtcNow;
        var exceptions = PlanFeatureCatalog.Gains(solo, pro).Select(g => new TenantFeatureException
        {
            Key = g.Feature.Key, ValueJson = PlanFeatureCatalog.ValueJson(g.Feature, g.Value)
        });
        var granted = PlanFeatureCatalog.ApplyExceptions(solo, exceptions, now);
        PlanFeatureCatalog.Gains(granted, pro).Should().BeEmpty();
        granted.BrandingTier.Should().Be("full");
    }

    [Fact]
    public void Trial_plan_applies_only_until_trial_end()
    {
        var start = new Plan { Id = "start", Name = "Start", MaxClients = 3 };
        var pro = new Plan { Id = "pro", Name = "Pro", MaxClients = 75, AiAssistant = true };
        var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var tenant = new Tenant { PlanId = "start", TrialPlanId = "pro", TrialPlanEndsAt = now.AddDays(10) };

        PlanFeatureCatalog.Effective(tenant, start, pro, now).MaxClients.Should().Be(75);
        PlanFeatureCatalog.Effective(tenant, start, pro, now.AddDays(11)).MaxClients.Should().Be(3);
    }

    [Fact]
    public void Lock_icon_names_the_cheapest_plan_with_the_feature()
    {
        var map = PlanFeatureCatalog.UpgradePlans(
        [
            new Plan { Id = "studio", Name = "Business", MonthlyPrice = 249, IsActive = true, WhiteLabel = true, AiAssistant = true, MaxAiActionsPerMonth = 1500 },
            new Plan { Id = "pro", Name = "Pro", MonthlyPrice = 119, IsActive = true, AiAssistant = true, MaxAiActionsPerMonth = 300 },
            new Plan { Id = "old", Name = "Stary", MonthlyPrice = 10, IsActive = false, WhiteLabel = true },
        ]);
        map["AiAssistant"].Should().Be("Pro");
        map["WhiteLabel"].Should().Be("Business", "nieaktywny plan się nie liczy");
        map.Should().NotContainKey("Coupons");
    }

    [Fact]
    public void Price_change_does_not_touch_existing_studios()
    {
        var pro = new Plan { Id = "pro", MonthlyPrice = 99, YearlyPrice = 990 };
        var tenant = new Tenant { PlanId = "pro", BillingInterval = "yearly" };
        PlanPricing.Lock(tenant, pro);

        pro.MonthlyPrice = 119;
        pro.YearlyPrice = 1190;

        PlanPricing.Monthly(tenant, pro).Should().Be(99);
        PlanPricing.ForInterval(tenant, pro).Should().Be(990);
        PlanPricing.Monthly(new Tenant(), pro).Should().Be(119, "nowe studio płaci według cennika");

        var free = new Tenant();
        PlanPricing.Lock(free, new Plan { MonthlyPrice = 0 });
        free.LockedMonthlyPrice.Should().BeNull("darmowego planu nie blokujemy");
    }

    [Fact]
    public async Task Studio_on_free_plan_gets_pro_during_trial_and_keeps_floor()
    {
        var (tenants, db) = await BuildAsync();
        int trialId, oldId;
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var trial = new Tenant { Slug = "nowe", CompanyName = "Nowe", PlanId = "start", TrialPlanId = "pro", TrialPlanEndsAt = DateTime.UtcNow.AddDays(14) };
            var old = new Tenant { Slug = "stare", CompanyName = "Stare", PlanId = "start", FeatureFloor = """{"SalesTools":true,"MaxClients":15}""" };
            ctx.Tenants.AddRange(trial, old);
            await ctx.SaveChangesAsync();
            (trialId, oldId) = (trial.Id, old.Id);
        }
        var start = await (await db.CreateDbContextAsync()).Plans.AsNoTracking().FirstAsync(p => p.Id == "start");

        var t = JsonDocument.Parse(await tenants.EntitlementsJsonAsync(trialId, start)).RootElement;
        t.GetProperty("name").GetString().Should().Be("Pro");
        t.GetProperty("aiAssistant").GetBoolean().Should().BeTrue();
        t.GetProperty("basePlanName").GetString().Should().Be("Start");
        t.TryGetProperty("trialEndsAt", out _).Should().BeTrue();
        var ent = JsonSerializer.Deserialize<Entitlements>(t.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        ent.UpgradePlans["WhiteLabel"].Should().Be("Business");
        ent.HiddenWhenLocked.Should().Contain("AuditLog");
        ent.TrialEndsAt.Should().NotBeNull();
        PlanCatalog.AfterTrial(ent).Name.Should().Be("Start", "strona „Ulepsz” porównuje z planem po okresie próbnym");
        PlanCatalog.HasUpgrade(ent).Should().BeTrue();

        var o = JsonDocument.Parse(await tenants.EntitlementsJsonAsync(oldId, start)).RootElement;
        o.GetProperty("name").GetString().Should().Be("Start");
        o.GetProperty("salesTools").GetBoolean().Should().BeTrue("obecne studio zachowuje karnety z czasów, gdy były w każdym planie");
        o.GetProperty("maxClients").GetInt32().Should().Be(15);
        o.GetProperty("aiAssistant").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Seeded_packages_follow_the_new_price_list()
    {
        using var ctx = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        ctx.Database.EnsureCreated();
        var plans = ctx.Plans.AsNoTracking().ToDictionary(p => p.Id);
        (plans["starter"].Name, plans["starter"].MonthlyPrice, plans["starter"].MaxClients).Should().Be(("Solo", 59m, 20));
        (plans["pro"].MonthlyPrice, plans["pro"].MaxSmsPerMonth, plans["pro"].MaxAiActionsPerMonth, plans["pro"].IsFeatured).Should().Be((119m, 150, 300, true));
        (plans["studio"].MonthlyPrice, plans["studio"].WhiteLabel, plans["studio"].MaxTrainers).Should().Be((249m, true, 5));
        plans["start"].MonthlyPrice.Should().Be(0);
        plans["starter"].AiAssistant.Should().BeFalse("AI w Solo tylko jako zajawka");
        plans.Values.Where(p => p.MonthlyPrice > 0).Should().OnlyContain(p => p.YearlyPrice == p.MonthlyPrice * 10, "rocznie 2 miesiące gratis");
    }

    [Fact]
    public async Task Try_for_14_days_once_picks_cheapest_plan_with_the_feature_and_keeps_billing()
    {
        var (tenants, db) = await BuildAsync();
        int id;
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var solo = new Tenant { Slug = "solo", CompanyName = "Solo", PlanId = "starter", TrialEndsAt = null, BillingStatus = "active" };
            ctx.Tenants.Add(solo);
            await ctx.SaveChangesAsync();
            id = solo.Id;
        }
        var starter = await (await db.CreateDbContextAsync()).Plans.AsNoTracking().FirstAsync(p => p.Id == "starter");
        JsonDocument.Parse(await tenants.EntitlementsJsonAsync(id, starter)).RootElement.GetProperty("upgradeTrialAvailable").GetBoolean().Should().BeTrue();

        var r = await tenants.StartUpgradeTrialAsync(id, "WhiteLabel");
        r.Ok.Should().BeTrue(r.Error);
        r.PlanName.Should().Be("Business", "white-label jest dopiero w Business");

        await using (var ctx = await db.CreateDbContextAsync())
        {
            var t = await ctx.Tenants.FirstAsync(x => x.Id == id);
            t.TrialPlanId.Should().Be("studio");
            t.TrialPlanEndsAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
            t.TrialEndsAt.Should().BeNull("okres próbny planu nie wstrzymuje rachunków za Solo");
        }
        var e = JsonDocument.Parse(await tenants.EntitlementsJsonAsync(id, starter)).RootElement;
        e.GetProperty("whiteLabel").GetBoolean().Should().BeTrue();
        e.GetProperty("upgradeTrialAvailable").GetBoolean().Should().BeFalse();
        (await tenants.StartUpgradeTrialAsync(id, "Automations")).Ok.Should().BeFalse("tylko raz na studio");
    }

    [Fact]
    public async Task Tester_status_goes_to_the_studio_app_with_the_due_survey()
    {
        var (tenants, db) = await BuildAsync();
        int id;
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var t = new Tenant { Slug = "tester", CompanyName = "Tester", PlanId = "start", TesterSince = DateTime.UtcNow.AddDays(-8), TesterUntil = DateTime.UtcNow.AddDays(82) };
            ctx.Tenants.Add(t);
            await ctx.SaveChangesAsync();
            id = t.Id;
        }
        var start = await (await db.CreateDbContextAsync()).Plans.AsNoTracking().FirstAsync(p => p.Id == "start");
        var tester = JsonDocument.Parse(await tenants.EntitlementsJsonAsync(id, start)).RootElement.GetProperty("tester");
        tester.GetProperty("week").GetInt32().Should().Be(1);
        tester.GetProperty("weeks").GetInt32().Should().Be(13);
        tester.GetProperty("feedbackDue").GetBoolean().Should().BeTrue();

        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.TesterFeedback.Add(new TesterFeedback { TenantId = id, Week = 1, Rating = 5 });
            await ctx.SaveChangesAsync();
        }
        JsonDocument.Parse(await tenants.EntitlementsJsonAsync(id, start)).RootElement.GetProperty("tester").GetProperty("feedbackDue").GetBoolean()
            .Should().BeFalse("opinia za ten tydzień już jest");
    }

    [Fact]
    public void Pricing_and_upgrade_page_come_from_the_catalog()
    {
        using var ctx = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        ctx.Database.EnsureCreated();
        var plans = ctx.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ToList();
        var pro = plans.First(p => p.Id == "pro");
        var labels = PTScheduler.Portal.Components.Home.HomeShared.PlanFeatures(pro, garmin: true, sms: true).Where(f => f.Included).Select(f => f.Label).ToList();
        labels.Should().Contain(["Do 75 klientów", "Asystent AI (300 akcji/mies.)", "Zegarki klientów (Garmin)", "Karnety i vouchery"]);
        PTScheduler.Portal.Components.Home.HomeShared.PlanFeatures(pro).Select(f => f.Label).Should().NotContain("Zegarki klientów (Garmin)", "Garmin wyłączony na platformie");
        var (lead, points) = PTScheduler.Portal.Components.Home.HomeShared.Highlights(pro, plans.First(p => p.Id == "starter"), 20, true, true);
        lead.Should().Contain("Solo");
        points.Should().NotContain("Plany treningowe i trening na żywo", "jest już w Solo");

        // Strona „Ulepsz plan” w studiu: cennik i zyski z tego samego katalogu.
        var feed = new PlanCatalog.Feed(
            plans.Where(p => p.IsActive).Select(p => new PlanCatalog.FeedPlan(p.Id, p.Name, p.Description, p.MonthlyPrice, p.YearlyPrice, p.IsFeatured, p.TrialDays,
                PlanFeatureCatalog.All.ToDictionary(f => f.Key, f => JsonSerializer.SerializeToElement(PlanFeatureCatalog.Get(p, f.Key))))).ToList(),
            PlanFeatureCatalog.All.Select(f => new PlanCatalog.FeedFeature(f.Key, f.Name, f.Benefit, f.Icon, f.Kind.ToString(), f.Pitch)).ToList());
        PlanCatalog.Apply(feed);
        try
        {
            var solo = new Entitlements { Id = "starter", Name = "Solo", MonthlyPrice = 59, MaxClients = 20, PaymentsEnabled = true, TrainingPlansEnabled = true,
                SalesTools = false, Automations = false, AiAssistant = false, WhiteLabel = false, Wearables = false, CoursesEnabled = false };
            PlanCatalog.HigherThan(solo).Select(p => p.Name).Should().Equal("Pro", "Business");
            var gains = PlanCatalog.GainsFor(PlanCatalog.Plans.First(p => p.Id == "pro"), solo).Select(b => b.Title).ToList();
            gains.Should().Contain(["Do 75 klientów", "Asystent AI", "Karnety i vouchery", "Automatyzacje i retencja"]);
            gains.Should().NotContain("Płatności online", "Solo już je ma");
            PlanCatalog.Plans.First(p => p.Id == "pro").Price.Should().Be(119);
        }
        finally { PlanCatalog.Reset(); }
    }

    [Fact]
    public async Task Studio_exception_grants_feature_until_it_expires()
    {
        var (tenants, db) = await BuildAsync();
        int id;
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var t = new Tenant { Slug = "ania", CompanyName = "Ania", PlanId = "starter" };
            ctx.Tenants.Add(t);
            await ctx.SaveChangesAsync();
            id = t.Id;
            var garmin = PlanFeatureCatalog.Find("Wearables")!;
            var clients = PlanFeatureCatalog.Find("MaxClients")!;
            ctx.TenantFeatureExceptions.AddRange(
                new TenantFeatureException { TenantId = id, Key = "Wearables", ValueJson = PlanFeatureCatalog.ValueJson(garmin, true), ExpiresAt = DateTime.UtcNow.AddDays(30), Note = "promocja" },
                new TenantFeatureException { TenantId = id, Key = "MaxClients", ValueJson = PlanFeatureCatalog.ValueJson(clients, 40) },
                new TenantFeatureException { TenantId = id, Key = "MaxClients", ValueJson = PlanFeatureCatalog.ValueJson(clients, 5) },
                new TenantFeatureException { TenantId = id, Key = "SalesTools", ValueJson = "true", ExpiresAt = DateTime.UtcNow.AddDays(-1) });
            await ctx.SaveChangesAsync();
        }
        var starter = await (await db.CreateDbContextAsync()).Plans.AsNoTracking().FirstAsync(p => p.Id == "starter");

        var e = JsonDocument.Parse(await tenants.EntitlementsJsonAsync(id, starter)).RootElement;
        e.GetProperty("wearables").GetBoolean().Should().BeFalse("Garmin wyłączony globalnie — wyjątek nie obchodzi włącznika platformy");
        e.GetProperty("maxClients").GetInt32().Should().BeGreaterThanOrEqualTo(20, "wyjątek nigdy nie zabiera");
        e.GetProperty("salesTools").GetBoolean().Should().BeFalse("wyjątek wygasł");

        await using (var ctx = await db.CreateDbContextAsync())
        {
            var t = await ctx.Tenants.FirstAsync(x => x.Id == id);
            var ex = await ctx.TenantFeatureExceptions.AsNoTracking().Where(x => x.TenantId == id).ToListAsync();
            var eff = PlanFeatureCatalog.Effective(t, starter, null, DateTime.UtcNow, ex);
            eff.Wearables.Should().BeTrue();
            eff.MaxClients.Should().Be(40);
            eff.SalesTools.Should().BeFalse();
            PlanFeatureCatalog.Effective(t, starter, null, DateTime.UtcNow.AddDays(31), ex).Wearables.Should().BeFalse("po terminie studio wraca do planu");
        }
        PlanFeatureCatalog.ValueJson(PlanFeatureCatalog.Find("BrandingTier")!, "premium").Should().Be("\"premium\"");
    }

    private static async Task<(TenantService Tenants, IDbContextFactory<PortalDbContext> Db)> BuildAsync()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        await using (var ctx = await db.CreateDbContextAsync()) await ctx.Database.EnsureCreatedAsync();
        var settings = sp.GetRequiredService<SiteSettingsService>();
        var tenants = new TenantService(db, null!, settings, null!, null!, null!, null!, sp.GetRequiredService<IConfiguration>(), NullLogger<TenantService>.Instance);
        return (tenants, db);
    }
}
