using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class TrainerConfigService(IDbContextFactory<ApplicationDbContext> dbFactory) : ITrainerConfigService
{
    public async Task<TrainerConfigDto> GetAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.TrainerConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == trainerUserId);
        if (cfg is null)
            return new TrainerConfigDto();
        return new TrainerConfigDto
        {
            BreakAfterSessionMinutes = cfg.BreakAfterSessionMinutes,
            SlotGranularityMinutes = cfg.SlotGranularityMinutes,
            AllowClientsDiscoverPeers = cfg.AllowClientsDiscoverPeers,
            CancellationWindowHours = cfg.CancellationWindowHours,
            LateCancellationPolicy = cfg.LateCancellationPolicy,
            NoShowChargesSession = cfg.NoShowChargesSession,
        };
    }

    public async Task SaveAsync(string trainerUserId, TrainerConfigDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.TrainerConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == trainerUserId);
        if (cfg is null)
        {
            cfg = new TrainerConfig { TrainerUserId = trainerUserId };
            db.TrainerConfigs.Add(cfg);
        }
        cfg.BreakAfterSessionMinutes = dto.BreakAfterSessionMinutes;
        cfg.SlotGranularityMinutes = dto.SlotGranularityMinutes;
        cfg.AllowClientsDiscoverPeers = dto.AllowClientsDiscoverPeers;
        cfg.CancellationWindowHours = Math.Clamp(dto.CancellationWindowHours, 0, 168);
        cfg.LateCancellationPolicy = dto.LateCancellationPolicy;
        cfg.NoShowChargesSession = dto.NoShowChargesSession;
        await db.SaveChangesAsync();
    }

    public async Task<string> GetOrCreateCalendarFeedTokenAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await GetOrAddAsync(db, trainerUserId);
        if (string.IsNullOrEmpty(cfg.CalendarFeedToken))
        {
            cfg.CalendarFeedToken = NewToken();
            await db.SaveChangesAsync();
        }
        return cfg.CalendarFeedToken!;
    }

    public async Task<string> RegenerateCalendarFeedTokenAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await GetOrAddAsync(db, trainerUserId);
        cfg.CalendarFeedToken = NewToken();
        await db.SaveChangesAsync();
        return cfg.CalendarFeedToken;
    }

    public async Task<string?> FindTrainerByCalendarFeedTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 20) return null;
        await using var db = dbFactory.CreateDbContext();
        return await db.TrainerConfigs.AsNoTracking()
            .Where(c => c.CalendarFeedToken == token)
            .Select(c => c.TrainerUserId)
            .FirstOrDefaultAsync();
    }

    private static async Task<TrainerConfig> GetOrAddAsync(ApplicationDbContext db, string trainerUserId)
    {
        var cfg = await db.TrainerConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == trainerUserId);
        if (cfg is null)
        {
            cfg = new TrainerConfig { TrainerUserId = trainerUserId };
            db.TrainerConfigs.Add(cfg);
        }
        return cfg;
    }

    // 32 losowe bajty w base64url — nie do zgadnięcia, bezpieczne w URL.
    private static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
