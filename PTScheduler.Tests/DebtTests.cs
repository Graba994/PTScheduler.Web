using FluentAssertions;
using PTScheduler.Application.Messaging;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Tests.Helpers;
using PTScheduler.Infrastructure.Services;
using Xunit;

namespace PTScheduler.Tests;

public class DebtTests
{
    [Fact]
    public async Task Lists_Past_Unpaid_Sessions_And_Unpaid_Packages_Per_Client()
    {
        var (f, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60, SinglePrice = 150 });
        db.Clients.AddRange(
            new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Ola", LastName = "N", TrainerUserId = "t1" },
            new Client { Id = 2, ApplicationUserId = "c2", FirstName = "Jan", LastName = "K", TrainerUserId = "t2" });
        var now = new DateTime(2026, 10, 16, 20, 0, 0);
        db.Sessions.AddRange(
            new Session { Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", StartTime = now.AddDays(-3), Status = SessionStatus.Completed, OffPackagePayment = "trainer" },
            new Session { Id = 2, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", StartTime = now.AddDays(-2), Status = SessionStatus.Completed, OffPackagePayment = "trainer", PaidAt = now },
            new Session { Id = 3, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", StartTime = now.AddDays(2), Status = SessionStatus.Scheduled, OffPackagePayment = "trainer" },
            new Session { Id = 4, ClientId = 2, SessionTypeId = 1, TrainerUserId = "t2", StartTime = now.AddDays(-1), Status = SessionStatus.Completed, OffPackagePayment = "trainer" });
        db.SessionPackages.Add(new SessionPackage { Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Start 5", TotalSessions = 5, PricePerSession = 100, IsPaid = false });
        await db.SaveChangesAsync();

        await using var ctx = f.CreateDbContext();
        var list = await DebtService.LoadAsync(ctx, now, "t1", null);

        list.Should().ContainSingle();
        var ola = list[0];
        ola.Name.Should().Be("Ola N");
        ola.Items.Select(i => (i.Kind, i.Id)).Should().BeEquivalentTo([("session", 1), ("package", 1)]);
        ola.Total.Should().Be(650);
        (await DebtService.LoadAsync(ctx, now, null, null)).Should().HaveCount(2);
    }

    [Fact]
    public void Payment_Template_Mentions_What_And_How_Much()
    {
        var facts = new Dictionary<string, string> { ["za_co"] = "za trening 13.10", ["kwota"] = "150" };
        var informal = MessageTemplates.Build(DraftPurpose.Payment, new("ty", "czasem", null, null), facts);
        informal.Should().Contain("za trening 13.10 (150 zł)").And.Contain(MessageTemplates.ClientTag);
        var formal = MessageTemplates.Build(DraftPurpose.Payment, new("pan", "brak", null, "Anna"), facts);
        formal.Should().StartWith("Dzień dobry!").And.Contain("Uprzejmie przypominam").And.EndWith("Anna");
    }
}

public class QuickRepliesTests
{
    [Theory]
    [InlineData("Niestety jestem chory, nie dam rady jutro", "Zdrowiej")]
    [InlineData("Czy możemy przełożyć na czwartek?", "nowy termin")]
    [InlineData("Spóźnię się 10 minut, korek", "czekam")]
    [InlineData("Dzięki za trening!", "Do zobaczenia")]
    [InlineData("Ok", "👍")]
    public void Picks_Replies_By_Intent(string message, string expected)
    {
        PTScheduler.Application.Messaging.QuickReplies.For(message).Should().Contain(r => r.Contains(expected));
    }
}

public class PushActionTokenTests
{
    [Fact]
    public void Roundtrips_And_Rejects_Tampered_Tokens()
    {
        var tokens = new PTScheduler.Infrastructure.Services.PushActionTokens(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        var t = tokens.Create("trainer-1", "reply", 42, "Jasne!");
        var read = tokens.Read(t);
        read.Should().NotBeNull();
        read!.UserId.Should().Be("trainer-1");
        read.Kind.Should().Be("reply");
        read.TargetId.Should().Be(42);
        read.Arg.Should().Be("Jasne!");
        tokens.Read(t[..^2] + "xx").Should().BeNull();
        tokens.Read("").Should().BeNull();
    }
}
