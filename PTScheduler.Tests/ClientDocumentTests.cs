using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Dokumenty do akceptacji: regulamin trzeba przyjąć, zgodę można odrzucić, nowa wersja pyta ponownie.</summary>
public class ClientDocumentTests
{
    private static async Task<(ClientDocumentService Svc, int RulesId, int ImageId)> SetupAsync()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Clients.AddRange(
                new Client { Id = 1, FirstName = "Anna", LastName = "Nowak" },
                new Client { Id = 2, FirstName = "Ola", LastName = "Kowalska" });
            await db.SaveChangesAsync();
        }
        var svc = new ClientDocumentService(f, TestClock.AtWallClock(new DateTime(2026, 9, 28, 12, 0, 0)));
        var rules = await svc.SaveAsync(new SaveClientDocumentDto { Title = "Regulamin studia", Content = "Treść regulaminu studia.", Kind = DocumentKinds.Required });
        var image = await svc.SaveAsync(new SaveClientDocumentDto { Title = "Zgoda na wizerunek", Content = "Zgadzam się na publikację zdjęć.", Kind = DocumentKinds.Consent });
        return (svc, rules, image);
    }

    [Fact]
    public async Task Required_Document_Cannot_Be_Declined_But_Consent_Can()
    {
        var (svc, rules, image) = await SetupAsync();
        (await svc.HasPendingAsync(1)).Should().BeTrue();

        var decline = () => svc.DecideAsync(1, rules, false, "10.0.0.1");
        await decline.Should().ThrowAsync<InvalidOperationException>();

        await svc.DecideAsync(1, rules, true, "10.0.0.1");
        await svc.DecideAsync(1, image, false, "10.0.0.1");
        (await svc.HasPendingAsync(1)).Should().BeFalse("brak zgody to też decyzja");

        var docs = await svc.GetAllAsync();
        docs.Single(d => d.Id == rules).AcceptedCount.Should().Be(1);
        docs.Single(d => d.Id == image).DeclinedCount.Should().Be(1);
        docs.Single(d => d.Id == image).ClientCount.Should().Be(2);

        // Klient zmienia zdanie — liczy się ostatnia decyzja.
        await svc.DecideAsync(1, image, true, null);
        (await svc.GetForClientAsync(1)).Single(d => d.DocumentId == image).Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task New_Version_Asks_Again_Only_When_Requested()
    {
        var (svc, rules, _) = await SetupAsync();
        await svc.DecideAsync(1, rules, true, null);

        // Poprawka literówki bez ponownego pytania — wersja bez zmian.
        await svc.SaveAsync(new SaveClientDocumentDto { Id = rules, Title = "Regulamin studia", Content = "Treść regulaminu studia (poprawiona).", Kind = DocumentKinds.Required, AskAgain = false });
        (await svc.GetForClientAsync(1)).Single(d => d.DocumentId == rules).Pending.Should().BeFalse();

        await svc.SaveAsync(new SaveClientDocumentDto { Id = rules, Title = "Regulamin studia", Content = "Nowy regulamin od października.", Kind = DocumentKinds.Required, AskAgain = true });
        var status = (await svc.GetForClientAsync(1)).Single(d => d.DocumentId == rules);
        status.Version.Should().Be(2);
        status.Pending.Should().BeTrue();

        var decisions = await svc.GetDecisionsAsync(rules);
        decisions.Should().HaveCount(2);
        decisions.Single(d => d.ClientId == 1).Accepted.Should().BeNull("akceptacja dotyczyła starej wersji");
    }

    [Fact]
    public async Task Archived_Document_Is_Not_Shown_To_Client()
    {
        var (svc, rules, image) = await SetupAsync();
        await svc.ArchiveAsync(image);
        (await svc.GetForClientAsync(2)).Select(d => d.DocumentId).Should().Equal(rules);
        var decide = () => svc.DecideAsync(2, image, true, null);
        await decide.Should().ThrowAsync<ArgumentException>();
    }
}
