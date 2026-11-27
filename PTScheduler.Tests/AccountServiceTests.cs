using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using SkiaSharp;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Moje konto: profil zsynchronizowany z kartą klienta, zdjęcie profilowe, link kalendarza, kanały powiadomień.</summary>
public sealed class AccountServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ptacct-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<(ServiceProvider Sp, AccountService Svc, IDbContextFactory<ApplicationDbContext> Db, string UserId)> CreateAsync()
    {
        var services = new ServiceCollection();
        var dbName = $"acct_{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<ApplicationUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        var sp = services.BuildServiceProvider();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        await sp.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new IdentityRole(Roles.Client));
        var user = new ApplicationUser { UserName = "ola@example.com", Email = "ola@example.com", FirstName = "Ola" };
        (await users.CreateAsync(user, "Haslo1234!")).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, Roles.Client);

        var factory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (var db = factory.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 5, ApplicationUserId = user.Id, FirstName = "Ola", LastName = "" });
            await db.SaveChangesAsync();
        }

        var web = new Mock<IWebRootPathProvider>();
        web.SetupGet(w => w.WebRootPath).Returns(_root);
        var svc = new AccountService(factory, users, web.Object, TestClock.AtWallClock(new DateTime(2026, 9, 29, 10, 0, 0)),
            NullLogger<AccountService>.Instance);
        return (sp, svc, factory, user.Id);
    }

    [Fact]
    public async Task Profile_Is_Saved_On_Account_And_Client_Card()
    {
        var (sp, svc, db, userId) = await CreateAsync();
        await using var _ = sp;

        await svc.SaveProfileAsync(userId, new SaveAccountProfileDto
        {
            FirstName = " Ola ", LastName = "Nowak", Phone = "600  100 200",
            DateOfBirth = new DateOnly(1995, 3, 1), TrainingGoal = "Przebiec 10 km"
        });

        var overview = await svc.GetOverviewAsync(userId);
        overview!.FullName.Should().Be("Ola Nowak");
        overview.Phone.Should().Be("600 100 200");
        overview.Role.Should().Be(Roles.Client);
        overview.ClientId.Should().Be(5);
        overview.TrainingGoal.Should().Be("Przebiec 10 km");
        await using var ctx = db.CreateDbContext();
        var client = await ctx.Clients.SingleAsync(c => c.Id == 5);
        client.LastName.Should().Be("Nowak");
        client.Phone.Should().Be("600 100 200");
        client.DateOfBirth.Should().Be(new DateOnly(1995, 3, 1));
    }

    [Theory]
    [InlineData("", "Nowak", null, "Wpisz imię.")]
    [InlineData("Ola", "Nowak", "abc", "Numer telefonu")]
    public async Task Invalid_Profile_Is_Rejected(string first, string last, string? phone, string message)
    {
        var (sp, svc, _, userId) = await CreateAsync();
        await using var _ = sp;
        var act = () => svc.SaveProfileAsync(userId, new SaveAccountProfileDto { FirstName = first, LastName = last, Phone = phone });
        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(message);
    }

    [Fact]
    public async Task Avatar_Is_Square_And_Shown_On_Client_Card_Until_Removed()
    {
        var (sp, svc, db, userId) = await CreateAsync();
        await using var _ = sp;

        using var bmp = new SKBitmap(1200, 800);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.CornflowerBlue);
        using var jpeg = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Jpeg, 90);

        var url = await svc.SetAvatarAsync(userId, jpeg.ToArray());
        url.Should().StartWith($"/avatars/{userId}.webp?v=");
        var path = svc.AvatarFilePath(userId);
        path.Should().NotBeNull();
        using (var saved = SKBitmap.Decode(path))
        {
            saved.Width.Should().Be(AccountService.AvatarEdge);
            saved.Height.Should().Be(AccountService.AvatarEdge);
        }
        (await svc.GetAvatarUrlAsync(userId)).Should().Be(url);
        await using (var ctx = db.CreateDbContext())
            (await ctx.Clients.SingleAsync(c => c.Id == 5)).ProfilePictureUrl.Should().Be(url);

        var notImage = () => svc.SetAvatarAsync(userId, [1, 2, 3, 4]);
        await notImage.Should().ThrowAsync<ArgumentException>();

        await svc.RemoveAvatarAsync(userId);
        svc.AvatarFilePath(userId).Should().BeNull();
        (await svc.GetAvatarUrlAsync(userId)).Should().BeNull();
        svc.AvatarFilePath("../etc/passwd").Should().BeNull("ścieżka spoza katalogu zdjęć");
    }

    [Fact]
    public async Task Calendar_Token_Is_Stable_Until_Reset()
    {
        var (sp, svc, _, userId) = await CreateAsync();
        await using var _ = sp;

        var token = await svc.GetOrCreateCalendarTokenAsync(userId);
        (await svc.GetOrCreateCalendarTokenAsync(userId)).Should().Be(token);
        (await svc.FindUserByCalendarTokenAsync(token)).Should().Be(userId);

        var fresh = await svc.ResetCalendarTokenAsync(userId);
        fresh.Should().NotBe(token);
        (await svc.FindUserByCalendarTokenAsync(token)).Should().BeNull("stary link przestaje działać");
        (await svc.FindUserByCalendarTokenAsync(fresh)).Should().Be(userId);
    }

    [Fact]
    public async Task Push_Is_Skipped_When_Recipient_Turned_Off_Category()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.WebPushSettings.Add(new WebPushSettings { PublicKey = "pub", PrivateKey = "priv", Subject = "mailto:a@b.pl" });
            db.NotificationPreferences.Add(new NotificationPreferences { UserId = "u1", PushMessages = false });
            await db.SaveChangesAsync();
        }
        var push = new WebPushService(f, NullLogger<WebPushService>.Instance);
        var report = await push.SendWithReportAsync("u1", new PushMessageDto { Category = NotificationTypes.PushMessages, Title = "x" });
        report.LastError.Should().Contain("wyłączył");

        var prefs = new NotificationPreferencesService(f);
        (await prefs.IsEnabledAsync("u1", NotificationTypes.PushMessages)).Should().BeFalse();
        (await prefs.IsEnabledAsync("u1", NotificationTypes.SmsReminders)).Should().BeTrue("nowe kanały domyślnie włączone");
        (await prefs.IsEnabledAsync("nowy", NotificationTypes.SmsTrainerMessages)).Should().BeTrue();
    }
}
