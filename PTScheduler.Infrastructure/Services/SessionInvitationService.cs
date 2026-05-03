using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SessionInvitationService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager) : ISessionInvitationService
{
    public async Task<SessionInvitationDto> SendAsync(int sessionId, int invitedClientId, string sentByUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new InvalidOperationException("Sesja nie istnieje.");

        var alreadyExists = await db.SessionInvitations
            .AnyAsync(i => i.SessionId == sessionId && i.InvitedClientId == invitedClientId
                           && i.Status == InvitationStatus.Pending);
        if (alreadyExists)
            throw new InvalidOperationException("Zaproszenie dla tego klienta na tę sesję już istnieje.");

        var invitation = new SessionInvitation
        {
            SessionId       = sessionId,
            InvitedClientId = invitedClientId,
            Status          = InvitationStatus.Pending,
            CreatedByUserId = sentByUserId,
            CreatedAt       = DateTime.Now
        };

        db.SessionInvitations.Add(invitation);
        await db.SaveChangesAsync();

        return await BuildDtoAsync(db, invitation, session);
    }

    public async Task<List<SessionInvitationDto>> GetForSessionAsync(int sessionId)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return [];

        var invitations = await db.SessionInvitations
            .Include(i => i.InvitedClient)
            .Where(i => i.SessionId == sessionId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        var result = new List<SessionInvitationDto>();
        foreach (var inv in invitations)
            result.Add(await BuildDtoAsync(db, inv, session));
        return result;
    }

    public async Task<List<SessionInvitationDto>> GetPendingForClientAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var invitations = await db.SessionInvitations
            .Include(i => i.Session).ThenInclude(s => s.SessionType)
            .Include(i => i.InvitedClient)
            .Where(i => i.InvitedClientId == clientId && i.Status == InvitationStatus.Pending)
            .OrderBy(i => i.Session.StartTime)
            .ToListAsync();

        var result = new List<SessionInvitationDto>();
        foreach (var inv in invitations)
            result.Add(await BuildDtoAsync(db, inv, inv.Session));
        return result;
    }

    public async Task RespondAsync(int invitationId, bool accept, string? note = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var invitation = await db.SessionInvitations.FindAsync(invitationId)
            ?? throw new InvalidOperationException("Zaproszenie nie istnieje.");
        if (invitation.Status != InvitationStatus.Pending)
            throw new InvalidOperationException("To zaproszenie zostało już rozpatrzone.");

        invitation.Status      = accept ? InvitationStatus.Accepted : InvitationStatus.Declined;
        invitation.RespondedAt = DateTime.Now;
        invitation.ResponseNote = note;
        await db.SaveChangesAsync();
    }

    public async Task RevokeAsync(int invitationId)
    {
        await using var db = dbFactory.CreateDbContext();
        var invitation = await db.SessionInvitations.FindAsync(invitationId)
            ?? throw new InvalidOperationException("Zaproszenie nie istnieje.");
        db.SessionInvitations.Remove(invitation);
        await db.SaveChangesAsync();
    }

    private async Task<SessionInvitationDto> BuildDtoAsync(ApplicationDbContext db, SessionInvitation inv, Session session)
    {
        var trainer = await userManager.FindByIdAsync(session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim()
                          is { Length: > 0 } n ? n : trainer?.Email ?? session.TrainerUserId;

        var client = inv.InvitedClient ?? await db.Clients.FindAsync(inv.InvitedClientId);
        var clientName = client is null ? "" : $"{client.FirstName} {client.LastName}".Trim();
        if (string.IsNullOrEmpty(clientName)) clientName = client?.ApplicationUserId ?? "";

        return new SessionInvitationDto
        {
            Id                = inv.Id,
            SessionId         = inv.SessionId,
            SessionStartTime  = session.StartTime,
            SessionTypeName   = session.SessionType?.Name ?? "",
            DurationMinutes   = session.SessionType?.DurationMinutes ?? 0,
            TrainerName       = trainerName,
            InvitedClientId   = inv.InvitedClientId,
            InvitedClientName = clientName,
            Status            = inv.Status,
            CreatedAt         = inv.CreatedAt,
            RespondedAt       = inv.RespondedAt,
            ResponseNote      = inv.ResponseNote
        };
    }
}
