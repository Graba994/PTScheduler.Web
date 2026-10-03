using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using Microsoft.Extensions.Logging;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class BodyMeasurementService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppClock clock,
    IWebPushService push,
    ILogger<BodyMeasurementService> logger) : IBodyMeasurementService
{
    public async Task<List<BodyMeasurementDto>> GetAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.BodyMeasurements
            .AsNoTracking()
            .Where(m => m.ClientId == clientId)
            .OrderByDescending(m => m.MeasurementDate)
            .Select(m => Map(m))
            .ToListAsync();
    }

    public async Task<BodyMeasurementDto> AddAsync(CreateBodyMeasurementDto dto)
    {
        var errors = BodyMeasurementRules.Validate(dto.MeasurementDate, clock.Today, dto.WeightKg, dto.BodyFatPercent,
            ("Klatka", dto.ChestCm), ("Talia", dto.WaistCm), ("Biodra", dto.HipsCm), ("Udo", dto.ThighCm), ("Ramię", dto.ArmCm));
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));

        await using var db = dbFactory.CreateDbContext();
        var entity = new BodyMeasurement
        {
            ClientId = dto.ClientId,
            MeasurementDate = dto.MeasurementDate,
            WeightKg = dto.WeightKg,
            BodyFatPercent = dto.BodyFatPercent,
            ChestCm = dto.ChestCm,
            WaistCm = dto.WaistCm,
            HipsCm = dto.HipsCm,
            ThighCm = dto.ThighCm,
            ArmCm = dto.ArmCm,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            AddedByClient = dto.AddedByClient
        };
        db.BodyMeasurements.Add(entity);
        await db.SaveChangesAsync();
        if (dto.AddedByClient) await NotifyTrainerAsync(db, entity);
        return Map(entity);
    }

    public async Task<bool> DeleteOwnAsync(int id, int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.BodyMeasurements.FirstOrDefaultAsync(m => m.Id == id && m.ClientId == clientId && m.AddedByClient);
        if (entity is null) return false;
        db.BodyMeasurements.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    // Trener widzi od razu, że klient sam się zważył/zmierzył.
    private async Task NotifyTrainerAsync(ApplicationDbContext db, BodyMeasurement m)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == m.ClientId);
        if (string.IsNullOrEmpty(client?.TrainerUserId)) return;
        var name = $"{client.FirstName} {client.LastName}".Trim();
        var what = m.WeightKg is { } w ? $"waga {w:0.#} kg" : "nowe pomiary ciała";
        try
        {
            await push.SendAsync(client.TrainerUserId, new PushMessageDto
            {
                Category = PTScheduler.Domain.Constants.NotificationTypes.PushClientActivity,
                Title = $"📏 {name}: nowy pomiar",
                Body = $"Klient wpisał {what} ({m.MeasurementDate:dd.MM}).",
                Url = $"/clients/{client.Id}?tab=measurements"
            });
        }
        catch (Exception ex) { logger.LogWarning(ex, "Push o pomiarze {Id} nie wyszedł.", m.Id); }
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.BodyMeasurements.FindAsync(id);
        if (entity is null) return;
        db.BodyMeasurements.Remove(entity);
        await db.SaveChangesAsync();
    }

    private static BodyMeasurementDto Map(BodyMeasurement m) => new()
    {
        Id = m.Id,
        ClientId = m.ClientId,
        MeasurementDate = m.MeasurementDate,
        WeightKg = m.WeightKg,
        BodyFatPercent = m.BodyFatPercent,
        ChestCm = m.ChestCm,
        WaistCm = m.WaistCm,
        HipsCm = m.HipsCm,
        ThighCm = m.ThighCm,
        ArmCm = m.ArmCm,
        Notes = m.Notes,
        AddedByClient = m.AddedByClient
    };
}
