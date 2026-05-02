using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class BodyMeasurementService(ApplicationDbContext db) : IBodyMeasurementService
{
    public async Task<List<BodyMeasurementDto>> GetAsync(int clientId) =>
        await db.BodyMeasurements
            .Where(m => m.ClientId == clientId)
            .OrderByDescending(m => m.MeasurementDate)
            .Select(m => Map(m))
            .ToListAsync();

    public async Task<BodyMeasurementDto> AddAsync(CreateBodyMeasurementDto dto)
    {
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
            Notes = dto.Notes
        };
        db.BodyMeasurements.Add(entity);
        await db.SaveChangesAsync();
        return Map(entity);
    }

    public async Task DeleteAsync(int id)
    {
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
        Notes = m.Notes
    };
}
