using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ClientService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager) : IClientService
{
    public async Task<List<ClientDto>> GetClientsAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var clients = await db.Clients
            .Where(c => trainerUserId == null || c.TrainerUserId == trainerUserId)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .ToListAsync();

        var userIds = clients.Select(c => c.ApplicationUserId).ToList();
        var users = await userManager.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var sessionStats = await db.Sessions
            .Where(s => clients.Select(c => c.Id).Contains(s.ClientId))
            .GroupBy(s => s.ClientId)
            .Select(g => new
            {
                ClientId = g.Key,
                Total = g.Count(),
                LastDate = g.Max(s => (DateTime?)s.StartTime)
            })
            .ToDictionaryAsync(x => x.ClientId);

        return clients.Select(c =>
        {
            var user = users.GetValueOrDefault(c.ApplicationUserId);
            sessionStats.TryGetValue(c.Id, out var stats);
            return new ClientDto
            {
                Id = c.Id,
                ApplicationUserId = c.ApplicationUserId,
                Email = user?.Email ?? string.Empty,
                FirstName = c.FirstName,
                LastName = c.LastName,
                Phone = c.Phone,
                ProfilePictureUrl = c.ProfilePictureUrl,
                TrainingGoal = c.TrainingGoal,
                DateOfBirth = c.DateOfBirth,
                CreatedAt = c.CreatedAt,
                TotalSessions = stats?.Total ?? 0,
                LastSessionDate = stats?.LastDate,
                Status = c.Status,
                AllowSelfBooking = c.AllowSelfBooking,
                TrainerUserId = c.TrainerUserId
            };
        }).ToList();
    }

    public async Task<ClientDto?> GetClientByUserIdAsync(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == userId);
        return c is null ? null : await GetClientAsync(c.Id);
    }

    public async Task<ClientDto?> GetClientAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Clients.FindAsync(id);
        if (c is null) return null;

        var user = await userManager.FindByIdAsync(c.ApplicationUserId);
        var stats = await db.Sessions
            .Where(s => s.ClientId == id)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), LastDate = g.Max(s => (DateTime?)s.StartTime) })
            .FirstOrDefaultAsync();

        var packages = await db.SessionPackages
            .Include(p => p.SessionType)
            .Where(p => p.ClientId == id)
            .OrderByDescending(p => p.PurchasedAt)
            .ToListAsync();

        var activePackage = packages.FirstOrDefault(p => p.Status == PackageStatus.Active);

        static SessionPackageSummaryDto MapPkg(SessionPackage p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            SessionTypeId = p.SessionTypeId,
            SessionTypeName = p.SessionType?.Name ?? string.Empty,
            TotalSessions = p.TotalSessions,
            UsedSessions = p.UsedSessions,
            ExpiresAt = p.ExpiresAt,
            Status = p.Status,
            IsPaid = p.IsPaid
        };

        return new ClientDto
        {
            Id = c.Id,
            ApplicationUserId = c.ApplicationUserId,
            Email = user?.Email ?? string.Empty,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Phone = c.Phone,
            ProfilePictureUrl = c.ProfilePictureUrl,
            TrainingGoal = c.TrainingGoal,
            DateOfBirth = c.DateOfBirth,
            CreatedAt = c.CreatedAt,
            TotalSessions = stats?.Total ?? 0,
            LastSessionDate = stats?.LastDate,
            Status = c.Status,
            AllowSelfBooking = c.AllowSelfBooking,
            TrainerUserId = c.TrainerUserId,
            ActivePackage = activePackage is null ? null : MapPkg(activePackage),
            AllPackages = packages.Select(MapPkg).ToList()
        };
    }

    public async Task<ClientDto> CreateClientAsync(CreateClientDto dto)
    {
        if (await userManager.FindByEmailAsync(dto.Email) is not null)
            throw new InvalidOperationException($"Użytkownik z emailem '{dto.Email}' już istnieje.");

        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, Roles.Client);

        var client = new Client
        {
            ApplicationUserId = user.Id,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Phone = dto.Phone,
            TrainingGoal = dto.TrainingGoal,
            DateOfBirth = dto.DateOfBirth,
            CreatedAt = DateTime.UtcNow
        };

        await using var db = dbFactory.CreateDbContext();
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        return (await GetClientAsync(client.Id))!;
    }

    public async Task UpdateClientAsync(int id, UpdateClientDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(id)
            ?? throw new InvalidOperationException("Klient nie znaleziony.");

        client.FirstName = dto.FirstName;
        client.LastName = dto.LastName;
        client.Phone = dto.Phone;
        client.TrainingGoal = dto.TrainingGoal;
        client.DateOfBirth = dto.DateOfBirth;

        var user = await userManager.FindByIdAsync(client.ApplicationUserId);
        if (user is not null)
        {
            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            await userManager.UpdateAsync(user);
        }

        await db.SaveChangesAsync();
    }

    public async Task UpdateProfilePictureAsync(int id, string pictureUrl)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(id)
            ?? throw new InvalidOperationException("Klient nie znaleziony.");
        client.ProfilePictureUrl = pictureUrl;
        await db.SaveChangesAsync();
    }

    public async Task<List<TrainerNoteDto>> GetNotesAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var notes = await db.TrainerNotes
            .Where(n => n.ClientId == clientId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        var trainerIds = notes.Select(n => n.TrainerUserId).Distinct().ToList();
        var trainers = await userManager.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id);

        return notes.Select(n => new TrainerNoteDto
        {
            Id = n.Id,
            Content = n.Content,
            TrainerName = trainers.GetValueOrDefault(n.TrainerUserId, "Trener"),
            CreatedAt = n.CreatedAt
        }).ToList();
    }

    public async Task AddNoteAsync(int clientId, string trainerUserId, string content)
    {
        await using var db = dbFactory.CreateDbContext();
        db.TrainerNotes.Add(new TrainerNote
        {
            ClientId = clientId,
            TrainerUserId = trainerUserId,
            Content = content,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task DeleteNoteAsync(int noteId)
    {
        await using var db = dbFactory.CreateDbContext();
        var note = await db.TrainerNotes.FindAsync(noteId);
        if (note is not null)
        {
            db.TrainerNotes.Remove(note);
            await db.SaveChangesAsync();
        }
    }

    public async Task ApproveClientAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(clientId)
            ?? throw new InvalidOperationException("Klient nie istnieje.");
        client.Status = ClientStatus.Active;
        await db.SaveChangesAsync();
    }

    public async Task<List<ClientDto>> GetPendingClientsAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var clients = await db.Clients
            .Where(c => c.Status == ClientStatus.Pending
                     && (trainerUserId == null || c.TrainerUserId == trainerUserId))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var userIds = clients.Select(c => c.ApplicationUserId).ToList();
        var users = await userManager.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return clients.Select(c =>
        {
            var user = users.GetValueOrDefault(c.ApplicationUserId);
            return new ClientDto
            {
                Id = c.Id,
                ApplicationUserId = c.ApplicationUserId,
                Email = user?.Email ?? string.Empty,
                FirstName = c.FirstName,
                LastName = c.LastName,
                CreatedAt = c.CreatedAt,
                Status = ClientStatus.Pending,
                TrainerUserId = c.TrainerUserId
            };
        }).ToList();
    }

    public async Task SetAllowSelfBookingAsync(int clientId, bool allow)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(clientId)
            ?? throw new InvalidOperationException("Klient nie istnieje.");
        client.AllowSelfBooking = allow;
        await db.SaveChangesAsync();
    }
}
