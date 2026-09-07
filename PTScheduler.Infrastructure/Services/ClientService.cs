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
            .AsNoTracking()
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
        var c = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ApplicationUserId == userId);
        return c is null ? null : await GetClientAsync(c.Id);
    }

    public async Task<ClientDto?> GetClientAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Clients.FindAsync(id);
        if (c is null) return null;

        var user = await userManager.FindByIdAsync(c.ApplicationUserId);
        var stats = await db.Sessions
            .AsNoTracking()
            .Where(s => s.ClientId == id)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), LastDate = g.Max(s => (DateTime?)s.StartTime) })
            .FirstOrDefaultAsync();

        var packages = await db.SessionPackages
            .AsNoTracking()
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
            throw new InvalidOperationException("Podany adres email jest już zajęty.");

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
            .AsNoTracking()
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
            .AsNoTracking()
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

    public async Task<CsvImportResult> ImportClientsFromCsvAsync(Stream csvStream)
    {
        var result = new CsvImportResult();
        using var reader = new StreamReader(csvStream, System.Text.Encoding.UTF8);
        var header = await reader.ReadLineAsync();
        if (header is null) { result.Errors.Add("Plik jest pusty."); return result; }

        var cols = header.Split(';').Select(c => c.Trim().Trim('"')).ToArray();
        int Col(params string[] names) => Array.FindIndex(cols, c => names.Any(n => c.Equals(n, StringComparison.OrdinalIgnoreCase)));

        var iFirst = Col("Imię", "Imie", "FirstName");
        var iLast  = Col("Nazwisko", "LastName");
        var iEmail = Col("Email", "E-mail");
        var iPhone = Col("Telefon", "Phone");
        var iGoal  = Col("Cel treningowy", "TrainingGoal", "Cel");
        var iDob   = Col("Data urodzenia", "DateOfBirth", "Urodziny");

        if (iFirst < 0 || iLast < 0 || iEmail < 0)
        {
            result.Errors.Add("Brak wymaganych kolumn: Imię, Nazwisko, Email (rozdzielane średnikiem).");
            return result;
        }

        var row = 1;
        while (await reader.ReadLineAsync() is { } line)
        {
            row++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = ParseCsvLine(line, ';');
            string Val(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim().Trim('"') : string.Empty;

            var email = Val(iEmail);
            var first = Val(iFirst);
            var last  = Val(iLast);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(first))
            {
                result.Errors.Add($"Wiersz {row}: brak email lub imienia — pominięto.");
                result.Skipped++;
                continue;
            }

            if (await userManager.FindByEmailAsync(email) is not null)
            {
                result.Errors.Add($"Wiersz {row}: {email} — email już istnieje — pominięto.");
                result.Skipped++;
                continue;
            }

            DateOnly? dob = null;
            var dobStr = Val(iDob);
            if (!string.IsNullOrEmpty(dobStr) && DateOnly.TryParse(dobStr, out var parsed))
                dob = parsed;

            try
            {
                await CreateClientAsync(new CreateClientDto
                {
                    Email = email,
                    FirstName = first,
                    LastName = string.IsNullOrWhiteSpace(last) ? first : last,
                    Phone = string.IsNullOrWhiteSpace(Val(iPhone)) ? null : Val(iPhone),
                    TrainingGoal = string.IsNullOrWhiteSpace(Val(iGoal)) ? null : Val(iGoal),
                    DateOfBirth = dob
                });
                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Wiersz {row}: {email} — {ex.Message}");
                result.Skipped++;
            }
        }
        return result;
    }

    private static string[] ParseCsvLine(string line, char sep)
    {
        var fields = new List<string>();
        var inQuote = false;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (ch == sep && !inQuote) { fields.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }
}
