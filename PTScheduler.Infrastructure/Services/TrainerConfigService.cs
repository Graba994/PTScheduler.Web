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
        cfg.CancellationWindowHours = dto.CancellationWindowHours;
        await db.SaveChangesAsync();
    }
}
