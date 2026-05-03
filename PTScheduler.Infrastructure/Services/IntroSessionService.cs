using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class IntroSessionService(IDbContextFactory<ApplicationDbContext> dbFactory) : IIntroSessionService
{
    public async Task<IntroSessionConfigDto?> GetConfigAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.IntroSessionConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == trainerUserId);
        return cfg is null ? null : MapToDto(cfg);
    }

    public async Task SaveConfigAsync(string trainerUserId, SaveIntroConfigDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.IntroSessionConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == trainerUserId);
        if (cfg is null)
        {
            cfg = new IntroSessionConfig { TrainerUserId = trainerUserId };
            db.IntroSessionConfigs.Add(cfg);
        }

        cfg.DurationMinutes = dto.DurationMinutes;
        cfg.IsFree = dto.IsFree;
        cfg.Price = dto.IsFree ? 0 : dto.Price;
        cfg.PromoPrice = dto.PromoPrice;
        cfg.PromoValidUntil = dto.PromoValidUntil;
        cfg.Description = dto.Description;
        cfg.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
    }

    public async Task<bool> HasBookedIntroAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Sessions.AnyAsync(s =>
            s.ClientId == clientId &&
            s.Status != SessionStatus.Cancelled);
    }

    private static IntroSessionConfigDto MapToDto(IntroSessionConfig c) => new()
    {
        Id = c.Id,
        TrainerUserId = c.TrainerUserId,
        DurationMinutes = c.DurationMinutes,
        IsFree = c.IsFree,
        Price = c.Price,
        PromoPrice = c.PromoPrice,
        PromoValidUntil = c.PromoValidUntil,
        Description = c.Description,
        IsActive = c.IsActive
    };
}
