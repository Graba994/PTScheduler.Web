using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class WorkoutSessionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppClock clock) : IWorkoutSessionService
{
    public async Task SaveDraftAsync(int clientId, string draftJson)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.WorkoutSessions.FirstOrDefaultAsync(s => s.ClientId == clientId);
        if (session is null)
        {
            db.WorkoutSessions.Add(new WorkoutSession
            {
                ClientId = clientId,
                DraftJson = draftJson,
                UpdatedAt = clock.UtcNow
            });
        }
        else
        {
            session.DraftJson = draftJson;
            session.UpdatedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    public async Task<WorkoutSessionDraftDto?> GetOpenDraftAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.WorkoutSessions.AsNoTracking()
            .Where(s => s.ClientId == clientId)
            .Select(s => new WorkoutSessionDraftDto { DraftJson = s.DraftJson, UpdatedAt = s.UpdatedAt })
            .FirstOrDefaultAsync();
    }

    public async Task DiscardAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.WorkoutSessions.FirstOrDefaultAsync(s => s.ClientId == clientId);
        if (session is null) return;
        db.WorkoutSessions.Remove(session);
        await db.SaveChangesAsync();
    }
}
