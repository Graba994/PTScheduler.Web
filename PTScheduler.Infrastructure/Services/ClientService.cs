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
        var users = await db.Users.AsNoTracking()
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
                TrustedForDeferredPayment = c.TrustedForDeferredPayment,
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

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == c.ApplicationUserId);
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
                TrustedForDeferredPayment = c.TrustedForDeferredPayment,
            TrainerUserId = c.TrainerUserId,
            ActivePackage = activePackage is null ? null : MapPkg(activePackage),
            AllPackages = packages.Select(MapPkg).ToList()
        };
    }

    public async Task<EmailOwnerDto?> FindEmailOwnerAsync(string email)
    {
        var normalized = Domain.Rules.EmailRules.Normalize(email);
        if (normalized.Length == 0) return null;
        var user = await userManager.FindByEmailAsync(normalized);
        if (user is null) return null;
        var roles = await userManager.GetRolesAsync(user);
        var name = $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } n ? n : user.Email ?? normalized;
        if (!roles.Contains(Roles.Client) || roles.Any(r => r is Roles.Admin or Roles.Trainer or Roles.Subordinate or Roles.Root))
            return new("staff", name, null, null, null);
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ApplicationUserId == user.Id);
        string? trainerName = null;
        if (client?.TrainerUserId is { } tid)
        {
            var t = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == tid);
            trainerName = t is null ? null : $"{t.FirstName} {t.LastName}".Trim() is { Length: > 0 } tn ? tn : t.Email;
        }
        return new("client", client is null ? name : $"{client.FirstName} {client.LastName}".Trim(), client?.Id, client?.TrainerUserId, trainerName);
    }

    public async Task<ClientCreateResult> AddClientAsync(CreateClientDto dto)
    {
        dto.Email = Domain.Rules.EmailRules.Normalize(dto.Email);
        if (Domain.Rules.EmailRules.Problem(dto.Email) is { } problem) return new(null, problem);
        if (string.IsNullOrWhiteSpace(dto.FirstName) && string.IsNullOrWhiteSpace(dto.LastName)) return new(null, "Podaj imię albo nazwisko klienta.");

        var owner = await FindEmailOwnerAsync(dto.Email);
        if (owner is { Kind: "staff" })
            return new(null, $"Ten adres należy do konta zespołu studia ({owner.Name}). Klient potrzebuje innego adresu e-mail.");
        if (owner is { Kind: "client" })
        {
            await using var db = dbFactory.CreateDbContext();
            var user = await userManager.FindByEmailAsync(dto.Email);
            var client = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == user!.Id);
            if (client is null)
            {
                // Konto klienta bez profilu (np. po przerwanym imporcie) — uzupełniamy profil zamiast zakładać drugie konto.
                client = new Client
                {
                    ApplicationUserId = user!.Id, FirstName = dto.FirstName, LastName = dto.LastName, Phone = dto.Phone,
                    TrainingGoal = dto.TrainingGoal, DateOfBirth = dto.DateOfBirth, TrainerUserId = dto.TrainerUserId, CreatedAt = DateTime.UtcNow
                };
                db.Clients.Add(client);
                await db.SaveChangesAsync();
                return new(await GetClientAsync(client.Id), null, "To konto już istniało — uzupełniliśmy profil klienta i dodaliśmy go do listy.");
            }
            if (client.TrainerUserId == dto.TrainerUserId)
                return new(await GetClientAsync(client.Id), null, $"{owner.Name} już jest na liście klientów — otwieramy profil.");
            if (client.TrainerUserId is null)
            {
                client.TrainerUserId = dto.TrainerUserId;
                await db.SaveChangesAsync();
                return new(await GetClientAsync(client.Id), null, $"{owner.Name} jest już w bazie, ale bez trenera — dodaliśmy do listy.");
            }
            return new(null, $"Ten adres ma już klient innego trenera ({owner.TrainerName ?? "inny trener"}). Trenera zmieni administrator studia w profilu klienta.");
        }

        try { return new(await CreateClientAsync(dto), null); }
        catch (InvalidOperationException ex) { return new(null, ex.Message); }
    }

    public async Task<ClientDto> CreateClientAsync(CreateClientDto dto)
    {
        dto.Email = Domain.Rules.EmailRules.Normalize(dto.Email);
        if (Domain.Rules.EmailRules.Problem(dto.Email) is { } problem)
            throw new InvalidOperationException(problem);
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
            TrainerUserId = string.IsNullOrWhiteSpace(dto.TrainerUserId) ? null : dto.TrainerUserId,
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
        var trainers = await db.Users.AsNoTracking()
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
        var users = await db.Users.AsNoTracking()
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

    public async Task<CsvImportResult> ImportClientsFromCsvAsync(Stream csvStream, string? trainerUserId = null)
    {
        using var reader = new StreamReader(csvStream, System.Text.Encoding.UTF8);
        var table = PTScheduler.Application.Import.ClientImportParser.Parse(await reader.ReadToEndAsync());
        if (table.Rows.Count == 0) return new CsvImportResult { Errors = { "Plik jest pusty." } };
        var map = PTScheduler.Application.Import.ClientImportParser.GuessMapping(table);
        if (!PTScheduler.Application.Import.ClientImportParser.HasName(map) || !map.ContainsValue(PTScheduler.Application.Import.ImportField.Email))
            return new CsvImportResult { Errors = { "Brak wymaganych kolumn: Imię (albo Imię i nazwisko) oraz Email." } };
        return await ImportCandidatesAsync(PTScheduler.Application.Import.ClientImportParser.Build(table, map), trainerUserId);
    }

    public async Task<CsvImportResult> ImportCandidatesAsync(IReadOnlyList<PTScheduler.Application.Import.ImportCandidate> rows, string? trainerUserId = null)
    {
        var result = new CsvImportResult();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            if (r.Problem is not null || r.Email is null)
            {
                result.Errors.Add($"Wiersz {r.Row}: {r.Problem ?? "brak e-maila"} — pominięto.");
                result.Skipped++;
                continue;
            }
            if (!seen.Add(r.Email) || await userManager.FindByEmailAsync(r.Email) is not null)
            {
                result.Errors.Add($"Wiersz {r.Row}: {r.Email} — taki e-mail już jest — pominięto.");
                result.Skipped++;
                continue;
            }
            try
            {
                await CreateClientAsync(new CreateClientDto
                {
                    Email = r.Email,
                    FirstName = r.FirstName,
                    LastName = string.IsNullOrWhiteSpace(r.LastName) ? r.FirstName : r.LastName,
                    Phone = r.Phone,
                    TrainingGoal = r.Goal,
                    DateOfBirth = r.BirthDate,
                    TrainerUserId = trainerUserId
                });
                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Wiersz {r.Row}: {r.Email} — {ex.Message}");
                result.Skipped++;
            }
        }
        return result;
    }
}
