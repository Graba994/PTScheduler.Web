using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class TrainerAvailabilityService(ApplicationDbContext db) : ITrainerAvailabilityService
{
    public async Task<List<TrainerAvailabilityDto>> GetAvailabilityRulesAsync(string trainerUserId)
    {
        var rules = await db.TrainerAvailabilities
            .Where(a => a.TrainerUserId == trainerUserId)
            .OrderBy(a => a.DayOfWeek)
            .ThenBy(a => a.SpecificDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync();

        return rules.Select(Map).ToList();
    }

    public async Task<TrainerAvailabilityDto> AddRuleAsync(CreateTrainerAvailabilityDto dto)
    {
        var rule = new TrainerAvailability
        {
            TrainerUserId = dto.TrainerUserId,
            DayOfWeek = dto.DayOfWeek,
            SpecificDate = dto.SpecificDate,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            Label = dto.Label,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.TrainerAvailabilities.Add(rule);
        await db.SaveChangesAsync();
        return Map(rule);
    }

    public async Task DeleteRuleAsync(int id)
    {
        var rule = await db.TrainerAvailabilities.FindAsync(id);
        if (rule is not null)
        {
            db.TrainerAvailabilities.Remove(rule);
            await db.SaveChangesAsync();
        }
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        var rule = await db.TrainerAvailabilities.FindAsync(id);
        if (rule is not null)
        {
            rule.IsActive = isActive;
            await db.SaveChangesAsync();
        }
    }

    public async Task<TrainerConfigDto> GetConfigAsync(string trainerUserId)
    {
        var cfg = await db.TrainerConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == trainerUserId);
        return cfg is null
            ? new TrainerConfigDto { BreakAfterSessionMinutes = 0, SlotGranularityMinutes = 30 }
            : new TrainerConfigDto
            {
                BreakAfterSessionMinutes = cfg.BreakAfterSessionMinutes,
                SlotGranularityMinutes = cfg.SlotGranularityMinutes,
                AllowClientsDiscoverPeers = cfg.AllowClientsDiscoverPeers
            };
    }

    public async Task SaveConfigAsync(string trainerUserId, TrainerConfigDto dto)
    {
        var cfg = await db.TrainerConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == trainerUserId);
        if (cfg is null)
        {
            cfg = new TrainerConfig { TrainerUserId = trainerUserId };
            db.TrainerConfigs.Add(cfg);
        }
        cfg.BreakAfterSessionMinutes = dto.BreakAfterSessionMinutes;
        cfg.SlotGranularityMinutes = Math.Max(15, dto.SlotGranularityMinutes);
        cfg.AllowClientsDiscoverPeers = dto.AllowClientsDiscoverPeers;
        await db.SaveChangesAsync();
    }

    public async Task<List<AvailableSlotDto>> GetAvailableSlotsAsync(
        string trainerUserId, DateOnly date, int sessionDurationMinutes)
    {
        var cfg = await GetConfigAsync(trainerUserId);
        var windows = await GetWindowsForDateAsync(trainerUserId, date);
        if (windows.Count == 0) return [];

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd   = date.ToDateTime(TimeOnly.MaxValue);

        // Load existing sessions for that day (including AwaitingPackage — slot is still taken)
        var sessions = await db.Sessions
            .Where(s => s.TrainerUserId == trainerUserId
                        && s.StartTime < dayEnd
                        && s.StartTime >= dayStart
                        && s.Status != SessionStatus.Cancelled)
            .Select(s => new { s.StartTime, EndMinutes = (int)s.SessionType.DurationMinutes })
            .ToListAsync();

        // Also load actual session types to get duration
        var sessionDetails = await db.Sessions
            .Include(s => s.SessionType)
            .Where(s => s.TrainerUserId == trainerUserId
                        && s.StartTime < dayEnd
                        && s.StartTime >= dayStart
                        && s.Status != SessionStatus.Cancelled)
            .ToListAsync();

        var bookedIntervals = sessionDetails
            .Select(s => (Start: s.StartTime, End: s.StartTime.AddMinutes(s.SessionType.DurationMinutes)))
            .ToList();

        var breakMin = cfg.BreakAfterSessionMinutes;
        var granMin  = cfg.SlotGranularityMinutes;
        var result   = new List<AvailableSlotDto>();

        foreach (var (windowStart, windowEnd) in windows)
        {
            var t = windowStart;
            while (t.AddMinutes(sessionDurationMinutes) <= windowEnd)
            {
                var slotEnd = t.AddMinutes(sessionDurationMinutes);
                var free = !bookedIntervals.Any(b =>
                    t < b.End.AddMinutes(breakMin) && slotEnd > b.Start);

                result.Add(new AvailableSlotDto { Start = t, End = slotEnd, IsAvailable = free });
                t = t.AddMinutes(granMin);
            }
        }

        return result;
    }

    public async Task<bool> IsSlotFreeAsync(string trainerUserId, DateTime start, int durationMinutes)
    {
        var cfg = await GetConfigAsync(trainerUserId);
        var slotEnd = start.AddMinutes(durationMinutes);

        return !await db.Sessions
            .Include(s => s.SessionType)
            .Where(s => s.TrainerUserId == trainerUserId
                        && s.Status != SessionStatus.Cancelled
                        && s.StartTime < slotEnd.AddMinutes(cfg.BreakAfterSessionMinutes)
                        && s.StartTime.AddMinutes(s.SessionType.DurationMinutes) > start)
            .AnyAsync();
    }

    // Returns (windowStart, windowEnd) in DateTime for the given date
    private async Task<List<(DateTime Start, DateTime End)>> GetWindowsForDateAsync(
        string trainerUserId, DateOnly date)
    {
        var dow = date.DayOfWeek;
        var today = date;

        var rules = await db.TrainerAvailabilities
            .Where(a => a.TrainerUserId == trainerUserId && a.IsActive)
            .ToListAsync();

        var windows = new List<(DateTime, DateTime)>();

        foreach (var rule in rules)
        {
            bool matches = rule.DayOfWeek.HasValue
                ? rule.DayOfWeek.Value == dow
                  && (!rule.ValidFrom.HasValue || today >= rule.ValidFrom.Value)
                  && (!rule.ValidUntil.HasValue || today <= rule.ValidUntil.Value)
                : rule.SpecificDate.HasValue && rule.SpecificDate.Value == today;

            if (matches)
                windows.Add((date.ToDateTime(rule.StartTime), date.ToDateTime(rule.EndTime)));
        }

        return windows;
    }

    private static TrainerAvailabilityDto Map(TrainerAvailability a) => new()
    {
        Id = a.Id,
        TrainerUserId = a.TrainerUserId,
        DayOfWeek = a.DayOfWeek,
        SpecificDate = a.SpecificDate,
        StartTime = a.StartTime,
        EndTime = a.EndTime,
        ValidFrom = a.ValidFrom,
        ValidUntil = a.ValidUntil,
        Label = a.Label,
        IsActive = a.IsActive
    };
}
