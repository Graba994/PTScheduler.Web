using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Dokumenty do akceptacji: klient decyduje w aplikacji, a każda decyzja zostaje w historii
/// (wersja dokumentu, data, adres IP). Liczy się ostatnia decyzja dla bieżącej wersji.
/// </summary>
public class ClientDocumentService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock) : IClientDocumentService
{
    public async Task<List<ClientDocumentDto>> GetAllAsync(bool includeInactive = false)
    {
        await using var db = dbFactory.CreateDbContext();
        var docs = await db.ClientDocuments.AsNoTracking()
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Id)
            .ToListAsync();
        var activeClients = await db.Clients.AsNoTracking().Where(c => c.Status == ClientStatus.Active).Select(c => c.Id).ToListAsync();
        var docIds = docs.Select(d => d.Id).ToList();
        var decisions = await LatestDecisionsAsync(db, docIds);

        return docs.Select(d =>
        {
            var current = decisions.Where(x => x.DocumentId == d.Id && x.Version == d.Version && activeClients.Contains(x.ClientId)).ToList();
            return new ClientDocumentDto
            {
                Id = d.Id, Title = d.Title, Content = d.Content, Kind = d.Kind, Version = d.Version, IsActive = d.IsActive,
                UpdatedAt = d.UpdatedAt,
                AcceptedCount = current.Count(x => x.Accepted),
                DeclinedCount = current.Count(x => !x.Accepted),
                ClientCount = activeClients.Count
            };
        }).ToList();
    }

    public async Task<int> SaveAsync(SaveClientDocumentDto dto)
    {
        var title = dto.Title?.Trim() ?? "";
        var content = (dto.Content ?? "").Replace("\r\n", "\n").Trim();
        if (title.Length is < 3 or > 150) throw new ArgumentException("Tytuł powinien mieć od 3 do 150 znaków.");
        if (content.Length < 10) throw new ArgumentException("Wpisz treść dokumentu.");
        if (content.Length > 20000) throw new ArgumentException("Treść jest za długa (maks. 20 000 znaków).");
        var kind = dto.Kind == DocumentKinds.Consent ? DocumentKinds.Consent : DocumentKinds.Required;

        await using var db = dbFactory.CreateDbContext();
        ClientDocument doc;
        if (dto.Id is int id)
        {
            doc = await db.ClientDocuments.FirstOrDefaultAsync(d => d.Id == id) ?? throw new ArgumentException("Nie ma takiego dokumentu.");
            var changed = doc.Content != content || doc.Title != title || doc.Kind != kind;
            if (changed && dto.AskAgain) doc.Version++;
        }
        else
        {
            doc = new ClientDocument { SortOrder = await db.ClientDocuments.CountAsync() };
            db.ClientDocuments.Add(doc);
        }
        doc.Title = title;
        doc.Content = content;
        doc.Kind = kind;
        doc.IsActive = true;
        doc.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync();
        return doc.Id;
    }

    public async Task ArchiveAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var doc = await db.ClientDocuments.FirstOrDefaultAsync(d => d.Id == id);
        if (doc is null) return;
        doc.IsActive = false; // historia decyzji zostaje
        doc.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<ClientDocumentStatusDto>> GetForClientAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var docs = await db.ClientDocuments.AsNoTracking().Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Id).ToListAsync();
        var latest = (await db.DocumentAcceptances.AsNoTracking().Where(a => a.ClientId == clientId).ToListAsync())
            .GroupBy(a => a.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id).First());
        return docs.Select(d =>
        {
            latest.TryGetValue(d.Id, out var a);
            return new ClientDocumentStatusDto(d.Id, d.Title, d.Kind, d.Version, d.Content, a?.Accepted, a?.Version, a?.DecidedAt);
        }).ToList();
    }

    public async Task<bool> HasPendingAsync(int clientId) =>
        (await GetForClientAsync(clientId)).Any(d => d.Pending);

    public async Task DecideAsync(int clientId, int documentId, bool accepted, string? ipAddress)
    {
        await using var db = dbFactory.CreateDbContext();
        var doc = await db.ClientDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.IsActive)
                  ?? throw new ArgumentException("Nie ma takiego dokumentu.");
        if (!accepted && doc.Kind == DocumentKinds.Required)
            throw new InvalidOperationException("Ten dokument trzeba zaakceptować, żeby korzystać z aplikacji.");
        db.DocumentAcceptances.Add(new DocumentAcceptance
        {
            DocumentId = doc.Id, Version = doc.Version, ClientId = clientId, Accepted = accepted,
            DecidedAt = clock.UtcNow, IpAddress = ipAddress is { Length: > 45 } ? ipAddress[..45] : ipAddress
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<DocumentDecisionDto>> GetDecisionsAsync(int documentId)
    {
        await using var db = dbFactory.CreateDbContext();
        var doc = await db.ClientDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId);
        if (doc is null) return [];
        var clients = await db.Clients.AsNoTracking().Where(c => c.Status == ClientStatus.Active)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Select(c => new { c.Id, Name = (c.FirstName + " " + c.LastName).Trim() }).ToListAsync();
        var decisions = (await LatestDecisionsAsync(db, [documentId])).ToDictionary(d => d.ClientId);
        return clients.Select(c =>
        {
            decisions.TryGetValue(c.Id, out var d);
            var current = d is not null && d.Version == doc.Version;
            return new DocumentDecisionDto(c.Id, c.Name, current ? d!.Accepted : null, d?.Version, d?.DecidedAt);
        }).ToList();
    }

    private static async Task<List<DocumentAcceptance>> LatestDecisionsAsync(ApplicationDbContext db, List<int> documentIds) =>
        (await db.DocumentAcceptances.AsNoTracking().Where(a => documentIds.Contains(a.DocumentId)).ToListAsync())
            .GroupBy(a => new { a.DocumentId, a.ClientId })
            .Select(g => g.OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.Id).First())
            .ToList();
}
