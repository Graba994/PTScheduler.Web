using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class PairService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    IEmailTemplateService emailTemplateService,
    INotificationPreferencesService notificationPrefs,
    IAppClock clock,
    ILogger<PairService> logger) : IPairService
{
    public async Task<List<PairDto>> GetPairsAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var pairs = await db.ClientContacts.AsNoTracking()
            .Include(cc => cc.Client1)
            .Include(cc => cc.Client2)
            .Where(cc => trainerUserId == null || cc.TrainerUserId == trainerUserId
                         || cc.Client1.TrainerUserId == trainerUserId || cc.Client2.TrainerUserId == trainerUserId)
            .ToListAsync();
        if (pairs.Count == 0) return [];

        var ids = pairs.SelectMany(p => new[] { p.Client1Id, p.Client2Id }).Distinct().ToList();
        var packages = await db.SessionPackages.AsNoTracking()
            .Where(p => p.PartnerClientId != null && p.Status == PackageStatus.Active && ids.Contains(p.ClientId))
            .Select(p => new { p.ClientId, Partner = p.PartnerClientId!.Value, Left = p.TotalSessions - p.UsedSessions })
            .ToListAsync();
        var now = clock.LocalNow;
        var upcoming = await db.Sessions.AsNoTracking()
            .Where(s => s.PairGroupId != null && s.StartTime >= now && ids.Contains(s.ClientId)
                        && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.AwaitingPackage))
            .Select(s => new { s.PairGroupId, s.ClientId, s.StartTime })
            .ToListAsync();
        var groupMembers = upcoming.GroupBy(s => s.PairGroupId!.Value)
            .Select(g => new { Clients = g.Select(x => x.ClientId).ToHashSet(), g.First().StartTime })
            .ToList();

        return pairs.Select(p => new PairDto
        {
            Id = p.Id,
            ClientAId = p.Client1Id,
            ClientAName = Name(p.Client1),
            ClientBId = p.Client2Id,
            ClientBName = Name(p.Client2),
            SharedRemaining = packages
                .Where(k => (k.ClientId == p.Client1Id && k.Partner == p.Client2Id) || (k.ClientId == p.Client2Id && k.Partner == p.Client1Id))
                .Sum(k => Math.Max(0, k.Left)),
            NextTraining = groupMembers
                .Where(g => g.Clients.Contains(p.Client1Id) && g.Clients.Contains(p.Client2Id))
                .Select(g => (DateTime?)g.StartTime).Min()
        }).OrderBy(p => p.ClientAName).ToList();
    }

    public async Task<List<PartnerDto>> GetPartnersAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var pairs = await db.ClientContacts.AsNoTracking()
            .Include(cc => cc.Client1)
            .Include(cc => cc.Client2)
            .Where(cc => cc.Client1Id == clientId || cc.Client2Id == clientId)
            .ToListAsync();
        return pairs
            .Select(p => p.Client1Id == clientId ? p.Client2 : p.Client1)
            .Where(c => c.Status == ClientStatus.Active)
            .Select(c => new PartnerDto { ClientId = c.Id, Name = Name(c), FirstName = c.FirstName.Trim() is { Length: > 0 } f ? f : Name(c) })
            .OrderBy(p => p.Name)
            .ToList();
    }

    public async Task AddPairAsync(int clientAId, int clientBId, string? trainerUserId = null)
    {
        if (clientAId == clientBId) throw new InvalidOperationException("Wybierz dwie różne osoby.");
        await using var db = dbFactory.CreateDbContext();
        await EnsurePairAsync(db, clientAId, clientBId, trainerUserId);
        await db.SaveChangesAsync();
    }

    public async Task RemovePairAsync(int pairId)
    {
        await using var db = dbFactory.CreateDbContext();
        var pair = await db.ClientContacts.FindAsync(pairId);
        if (pair is null) return;
        db.ClientContacts.Remove(pair);
        await db.SaveChangesAsync();
    }

    public async Task<PartnerLookupResult> FindOrCreatePartnerAsync(int buyerClientId, string contact, string? firstName)
    {
        contact = (contact ?? "").Trim();
        if (contact.Length == 0) return new(false, null, null, "Wpisz e-mail albo telefon osoby, z którą trenujesz.");

        await using var db = dbFactory.CreateDbContext();
        var buyer = await db.Clients.FindAsync(buyerClientId);
        if (buyer is null) return new(false, null, null, "Nie znaleziono Twojego profilu.");

        Client? partner;
        var isNew = false;
        if (contact.Contains('@'))
        {
            var user = await userManager.FindByEmailAsync(contact);
            if (user is not null)
            {
                partner = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == user.Id);
                if (partner is null)
                    return new(false, null, null, "Tego adresu nie można dodać do pary. Poproś trenera o pomoc.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(firstName))
                    return new(false, null, null, "Tej osoby nie ma jeszcze w aplikacji — wpisz jej imię, a założymy jej konto.");
                var name = firstName.Trim();
                var newUser = new ApplicationUser { UserName = contact, Email = contact, FirstName = name, EmailConfirmed = true };
                var created = await userManager.CreateAsync(newUser);
                if (!created.Succeeded)
                    return new(false, null, null, "Nie udało się założyć konta: " + string.Join(", ", created.Errors.Select(e => e.Description)));
                await userManager.AddToRoleAsync(newUser, Roles.Client);
                partner = new Client
                {
                    ApplicationUserId = newUser.Id,
                    FirstName = name,
                    TrainerUserId = buyer.TrainerUserId,
                    Status = ClientStatus.Active,
                    CreatedAt = DateTime.UtcNow
                };
                db.Clients.Add(partner);
                await db.SaveChangesAsync();
                isNew = true;
            }
        }
        else
        {
            var digits = Digits(contact);
            if (digits.Length < 9) return new(false, null, null, "Wpisz pełny numer telefonu albo e-mail.");
            var tail = digits[^9..];
            var withPhone = await db.Clients.Where(c => c.Phone != null && c.Id != buyer.Id).ToListAsync();
            partner = withPhone.FirstOrDefault(c => Digits(c.Phone!).EndsWith(tail));
            if (partner is null)
                return new(false, null, null, "Nie znaleźliśmy nikogo z tym numerem. Wpisz e-mail — wyślemy link do założenia konta.");
        }

        if (partner.Id == buyer.Id) return new(false, null, null, "Wpisz dane drugiej osoby, nie swoje.");

        await EnsurePairAsync(db, buyer.Id, partner.Id, buyer.TrainerUserId);
        await db.SaveChangesAsync();
        return new(true, partner.Id, Name(partner), null, isNew);
    }

    public async Task NotifyPackageSharedAsync(int packageId, string? appBaseUrl)
    {
        try
        {
            if (!await emailService.IsEnabledAsync()) return;
            await using var db = dbFactory.CreateDbContext();
            var pkg = await db.SessionPackages.AsNoTracking()
                .Include(p => p.Client)
                .Include(p => p.PartnerClient)
                .FirstOrDefaultAsync(p => p.Id == packageId);
            if (pkg?.PartnerClient is null) return;
            var partner = pkg.PartnerClient;
            var user = await userManager.FindByIdAsync(partner.ApplicationUserId);
            if (user?.Email is null) return;
            if (!await notificationPrefs.IsEnabledAsync(user.Id, NotificationTypes.PackageAssigned)) return;

            var baseUrl = (appBaseUrl ?? Environment.GetEnvironmentVariable("TENANT_DOMAIN") ?? "").Trim().TrimEnd('/');
            if (baseUrl.Length > 0 && !baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) baseUrl = "https://" + baseUrl;

            // Konto bez hasła (założone przy zakupie) — link do ustawienia hasła jednym kliknięciem.
            string? link = null, label = null;
            if (!await userManager.HasPasswordAsync(user) && baseUrl.Length > 0)
            {
                link = await ClientInvitationService.BuildLinkAsync(userManager, user, baseUrl + "/");
                label = "Załóż konto jednym kliknięciem";
            }
            else if (baseUrl.Length > 0)
            {
                link = baseUrl + "/my/packages";
                label = "Zobacz pakiet";
            }

            var trainer = pkg.Client.TrainerUserId is { } tid ? await userManager.FindByIdAsync(tid) : null;
            var vars = new Dictionary<string, string>
            {
                ["ClientName"] = partner.FirstName.Trim() is { Length: > 0 } f ? f : Name(partner),
                ["BuyerName"] = pkg.Client.FirstName.Trim() is { Length: > 0 } b ? b : Name(pkg.Client),
                ["PackageName"] = pkg.Name,
                ["TotalSessions"] = pkg.TotalSessions.ToString(),
                ["TrainerName"] = trainer is null ? "" : $"{trainer.FirstName} {trainer.LastName}".Trim(),
                ["ExpiresAt"] = pkg.ExpiresAt is { } e ? clock.ToWallClock(e).ToString("dd.MM.yyyy") : "",
                ["ExpiresRow"] = pkg.ExpiresAt is { } e2
                    ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Ważny do</td><td style=\"padding:8px 0;font-size:14px;font-weight:600\">{clock.ToWallClock(e2):dd.MM.yyyy}</td></tr>"
                    : "",
                ["ActionLink"] = link ?? "",
                ["ActionButton"] = link is null ? "" :
                    $"<div style=\"text-align:center;margin:24px 0\"><a href=\"{link}\" style=\"background:#7C3AED;color:white;text-decoration:none;padding:12px 32px;border-radius:6px;font-weight:600;font-size:15px;display:inline-block\">{label}</a></div>"
            };
            var (subject, html) = await emailTemplateService.RenderAsync("pair-package-shared", vars);
            await emailService.SendAsync(user.Email, Name(partner), subject, html);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd powiadomienia partnera o pakiecie (PackageId={Id})", packageId); }
    }

    internal static async Task EnsurePairAsync(ApplicationDbContext db, int clientAId, int clientBId, string? trainerUserId)
    {
        var a = Math.Min(clientAId, clientBId);
        var b = Math.Max(clientAId, clientBId);
        if (await db.ClientContacts.AnyAsync(cc => cc.Client1Id == a && cc.Client2Id == b)) return;
        if (db.ClientContacts.Local.Any(cc => cc.Client1Id == a && cc.Client2Id == b)) return;
        trainerUserId ??= await db.Clients.Where(c => c.Id == a).Select(c => c.TrainerUserId).FirstOrDefaultAsync();
        db.ClientContacts.Add(new ClientContact
        {
            TrainerUserId = trainerUserId ?? "",
            Client1Id = a,
            Client2Id = b,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static string Name(Client c) =>
        $"{c.FirstName} {c.LastName}".Trim() is { Length: > 0 } n ? n : "Podopieczny";

    private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());
}
