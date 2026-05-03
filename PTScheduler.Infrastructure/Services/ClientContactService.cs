using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ClientContactService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager) : IClientContactService
{
    public async Task<List<ClientContactDto>> GetContactsForClientAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(clientId);
        if (client is null) return [];

        var contacts = new List<ClientContactDto>();

        // Direct pairs
        var pairs = await db.ClientContacts
            .Include(cc => cc.Client1)
            .Include(cc => cc.Client2)
            .Where(cc => cc.Client1Id == clientId || cc.Client2Id == clientId)
            .ToListAsync();

        foreach (var pair in pairs)
        {
            var other = pair.Client1Id == clientId ? pair.Client2 : pair.Client1;
            contacts.Add(await BuildContactDtoAsync(pair.Id, other));
        }

        // If trainer allows discovery — add all other trainer clients
        if (client.TrainerUserId is not null)
        {
            var cfg = await db.TrainerConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == client.TrainerUserId);
            if (cfg?.AllowClientsDiscoverPeers == true)
            {
                var allPeers = await db.Clients
                    .Where(c => c.TrainerUserId == client.TrainerUserId
                                && c.Id != clientId
                                && c.Status == ClientStatus.Active)
                    .ToListAsync();

                var existingIds = contacts.Select(c => c.ContactClientId).ToHashSet();
                foreach (var peer in allPeers.Where(p => !existingIds.Contains(p.Id)))
                    contacts.Add(await BuildContactDtoAsync(null, peer));
            }
        }

        return contacts.DistinctBy(c => c.ContactClientId).ToList();
    }

    public async Task<List<ClientContactDto>> GetPairsAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var pairs = await db.ClientContacts
            .Include(cc => cc.Client1)
            .Include(cc => cc.Client2)
            .Where(cc => cc.TrainerUserId == trainerUserId)
            .ToListAsync();

        var result = new List<ClientContactDto>();
        foreach (var pair in pairs)
        {
            result.Add(await BuildContactDtoAsync(pair.Id, pair.Client1));
            result.Add(await BuildContactDtoAsync(pair.Id, pair.Client2));
        }
        return result;
    }

    public async Task AddPairAsync(string trainerUserId, int client1Id, int client2Id)
    {
        await using var db = dbFactory.CreateDbContext();
        var a = Math.Min(client1Id, client2Id);
        var b = Math.Max(client1Id, client2Id);

        var exists = await db.ClientContacts.AnyAsync(cc => cc.Client1Id == a && cc.Client2Id == b);
        if (exists) return;

        db.ClientContacts.Add(new ClientContact
        {
            TrainerUserId = trainerUserId,
            Client1Id = a,
            Client2Id = b,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task RemovePairAsync(int contactId)
    {
        await using var db = dbFactory.CreateDbContext();
        var pair = await db.ClientContacts.FindAsync(contactId);
        if (pair is not null)
        {
            db.ClientContacts.Remove(pair);
            await db.SaveChangesAsync();
        }
    }

    public async Task<List<SessionInvitationDto>> GetPendingInvitationsAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var invitations = await db.SessionInvitations
            .Include(i => i.Session).ThenInclude(s => s.SessionType)
            .Where(i => i.InvitedClientId == clientId && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        var result = new List<SessionInvitationDto>();
        foreach (var inv in invitations)
        {
            var trainer = await userManager.FindByIdAsync(inv.Session.TrainerUserId);
            var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmptyX() ?? trainer?.Email ?? inv.Session.TrainerUserId;
            result.Add(new SessionInvitationDto
            {
                Id = inv.Id,
                SessionId = inv.SessionId,
                SessionStartTime = inv.Session.StartTime,
                SessionTypeName = inv.Session.SessionType.Name,
                TrainerName = trainerName,
                InvitedClientId = inv.InvitedClientId,
                Status = inv.Status,
                CreatedAt = inv.CreatedAt,
                RespondedAt = inv.RespondedAt
            });
        }
        return result;
    }

    public async Task CreateInvitationAsync(int sessionId, int invitedClientId, string createdByUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var exists = await db.SessionInvitations
            .AnyAsync(i => i.SessionId == sessionId && i.InvitedClientId == invitedClientId);
        if (exists) return;

        db.SessionInvitations.Add(new SessionInvitation
        {
            SessionId = sessionId,
            InvitedClientId = invitedClientId,
            CreatedByUserId = createdByUserId,
            Status = InvitationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task RespondToInvitationAsync(int invitationId, bool accept, string? note = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var inv = await db.SessionInvitations.FindAsync(invitationId)
            ?? throw new InvalidOperationException("Zaproszenie nie istnieje.");
        inv.Status = accept ? InvitationStatus.Accepted : InvitationStatus.Declined;
        inv.RespondedAt = DateTime.UtcNow;
        inv.ResponseNote = note;
        await db.SaveChangesAsync();
    }

    public async Task AcceptOnBehalfAsync(int invitationId, string trainerUserId)
        => await RespondToInvitationAsync(invitationId, true, $"Zaakceptowane przez trenera");

    private async Task<ClientContactDto> BuildContactDtoAsync(int? contactId, Client client)
    {
        var user = await userManager.FindByIdAsync(client.ApplicationUserId);
        return new ClientContactDto
        {
            Id = contactId ?? 0,
            ContactClientId = client.Id,
            ContactClientName = $"{client.FirstName} {client.LastName}".Trim().NullIfEmptyX() ?? user?.Email ?? "Klient",
            ContactClientEmail = user?.Email
        };
    }
}

file static class StringExtX
{
    public static string? NullIfEmptyX(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
