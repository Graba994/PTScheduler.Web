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
            TrainerMessages = prefs.TrainerMessages,
            ShowHints = prefs.ShowHints,
            PushReminders = prefs.PushReminders,
            PushSessions = prefs.PushSessions,
            PushPackages = prefs.PushPackages,
            PushMessages = prefs.PushMessages,
            PushTrainerMessages = prefs.PushTrainerMessages,
            PushClientActivity = prefs.PushClientActivity,
            SmsReminders = prefs.SmsReminders,
            SmsTrainerMessages = prefs.SmsTrainerMessages,
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
        prefs.TrainerMessages = dto.TrainerMessages;
        prefs.ShowHints = dto.ShowHints;
        prefs.PushReminders = dto.PushReminders;
        prefs.PushSessions = dto.PushSessions;
        prefs.PushPackages = dto.PushPackages;
        prefs.PushMessages = dto.PushMessages;
        prefs.PushTrainerMessages = dto.PushTrainerMessages;
        prefs.PushClientActivity = dto.PushClientActivity;
        prefs.SmsReminders = dto.SmsReminders;
        prefs.SmsTrainerMessages = dto.SmsTrainerMessages;
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
            NotificationTypes.TrainerMessages => prefs.TrainerMessages,
            NotificationTypes.SessionReminders => prefs.SessionReminders,
            NotificationTypes.PushReminders => prefs.PushReminders,
            NotificationTypes.PushSessions => prefs.PushSessions,
            NotificationTypes.PushPackages => prefs.PushPackages,
            NotificationTypes.PushMessages => prefs.PushMessages,
            NotificationTypes.PushTrainerMessages => prefs.PushTrainerMessages,
            NotificationTypes.PushClientActivity => prefs.PushClientActivity,
            NotificationTypes.SmsReminders => prefs.SmsReminders,
            NotificationTypes.SmsTrainerMessages => prefs.SmsTrainerMessages,
            _ => true
        };
    }
}
