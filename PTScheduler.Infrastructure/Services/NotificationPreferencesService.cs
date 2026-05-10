using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class NotificationPreferencesService(IDbContextFactory<ApplicationDbContext> dbFactory) : INotificationPreferencesService
{
    public async Task<NotificationPreferencesDto> GetAsync(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var prefs = await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (prefs is null) return new NotificationPreferencesDto();
        return new NotificationPreferencesDto
        {
            SessionBooked = prefs.SessionBooked,
            SessionCancelledByTrainer = prefs.SessionCancelledByTrainer,
            SessionRescheduled = prefs.SessionRescheduled,
            PackageAssigned = prefs.PackageAssigned,
            SessionReminders = prefs.SessionReminders,
            ClientCancelledSession = prefs.ClientCancelledSession,
            NewClientPending = prefs.NewClientPending,
            ExpiringPackages = prefs.ExpiringPackages,
        };
    }

    public async Task SaveAsync(string userId, NotificationPreferencesDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var prefs = await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (prefs is null)
        {
            prefs = new NotificationPreferences { UserId = userId };
            db.NotificationPreferences.Add(prefs);
        }
        prefs.SessionBooked = dto.SessionBooked;
        prefs.SessionCancelledByTrainer = dto.SessionCancelledByTrainer;
        prefs.SessionRescheduled = dto.SessionRescheduled;
        prefs.PackageAssigned = dto.PackageAssigned;
        prefs.SessionReminders = dto.SessionReminders;
        prefs.ClientCancelledSession = dto.ClientCancelledSession;
        prefs.NewClientPending = dto.NewClientPending;
        prefs.ExpiringPackages = dto.ExpiringPackages;
        await db.SaveChangesAsync();
    }

    public async Task<bool> IsEnabledAsync(string userId, string notificationType)
    {
        var prefs = await GetAsync(userId);
        return notificationType switch
        {
            NotificationTypes.SessionBooked => prefs.SessionBooked,
            NotificationTypes.SessionCancelledByTrainer => prefs.SessionCancelledByTrainer,
            NotificationTypes.SessionRescheduled => prefs.SessionRescheduled,
            NotificationTypes.PackageAssigned => prefs.PackageAssigned,
            NotificationTypes.ClientCancelledSession => prefs.ClientCancelledSession,
            NotificationTypes.NewClientPending => prefs.NewClientPending,
            NotificationTypes.ExpiringPackages => prefs.ExpiringPackages,
            NotificationTypes.SessionReminders => prefs.SessionReminders,
            _ => true
        };
    }
}
