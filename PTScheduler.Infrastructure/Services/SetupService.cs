using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SetupService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    ISiteContentService siteContent,
    ILogger<SetupService> logger) : ISetupService
{
    // Kolory aplikacji z /admin/branding — nieznany klucz z Portalu zostaje przy domyślnym.
    private static readonly HashSet<string> AppThemes =
        ["ocean", "forest", "slate", "teal", "sunset", "crimson", "lavender", "amber", "indigo", "rose"];

    /// <summary>Zapisane w AppBranding.OnboardingJson — dane z rejestracji i token wejścia.</summary>
    private sealed class OnboardingState
    {
        public string? CompanyName { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public string? SetupMode { get; set; }
        public string? OwnerUserId { get; set; }
        public string? WelcomeTokenHash { get; set; }
        public DateTime? WelcomeTokenExpiresUtc { get; set; }
    }

    private static OnboardingState ReadState(AppBranding? b)
    {
        if (string.IsNullOrWhiteSpace(b?.OnboardingJson)) return new();
        try { return JsonSerializer.Deserialize<OnboardingState>(b.OnboardingJson) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public async Task<bool> IsSetupCompletedAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        return b?.SetupCompleted ?? false;
    }

    public async Task CompleteSetupAsync(string mode, string companyName, string adminEmail, string adminPassword)
    {
        // Właściciel dostaje WŁASNE konto administratora studia. Konto techniczne root@admin.local
        // (rola Root) zostaje osobno — dla operatora platformy (Portal, testy, funkcje techniczne).
        var owner = await userManager.FindByEmailAsync(adminEmail);
        if (owner is not null && await userManager.IsInRoleAsync(owner, Roles.Root))
            throw new InvalidOperationException("Ten adres należy do konta technicznego — podaj własny adres e-mail.");

        if (owner is null)
        {
            owner = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FirstName = "Administrator",
                LastName = companyName
            };
            // Imię i telefon z rejestracji w Portalu — o ile to ten sam adres.
            var prefill = await GetPrefillAsync();
            if (prefill is not null && string.Equals(prefill.Email, adminEmail, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(prefill.FirstName)) owner.FirstName = prefill.FirstName.Trim();
                if (!string.IsNullOrWhiteSpace(prefill.LastName)) owner.LastName = prefill.LastName.Trim();
                if (!string.IsNullOrWhiteSpace(prefill.Phone)) owner.PhoneNumber = prefill.Phone.Trim();
            }
            var created = await userManager.CreateAsync(owner, adminPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        }
        else
        {
            await userManager.RemovePasswordAsync(owner);
            var changed = await userManager.AddPasswordAsync(owner, adminPassword);
            if (!changed.Succeeded)
                throw new InvalidOperationException(string.Join(" ", changed.Errors.Select(e => e.Description)));
        }

        if (!await userManager.IsInRoleAsync(owner, Roles.Admin))
            await userManager.AddToRoleAsync(owner, Roles.Admin);
        // Hasło ustawia sam trener w kreatorze — nie wymuszamy kolejnej zmiany.
        owner.MustChangePassword = false;
        await userManager.UpdateAsync(owner);

        // Kreator jest skończony dopiero, gdy konto właściciela istnieje.
        await using var db = dbFactory.CreateDbContext();
        var branding = await db.AppBrandings.FirstOrDefaultAsync();
        if (branding is null)
        {
            branding = new AppBranding();
            db.AppBrandings.Add(branding);
        }

        branding.CompanyName = companyName;
        branding.SetupCompleted = true;
        branding.SetupMode = mode;
        branding.SetupCompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<SetupPrefillDto?> GetPrefillAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var state = ReadState(await db.AppBrandings.AsNoTracking().FirstOrDefaultAsync());
        if (string.IsNullOrWhiteSpace(state.Email) && string.IsNullOrWhiteSpace(state.CompanyName)) return null;
        return new SetupPrefillDto(state.CompanyName, state.Email, state.FirstName, state.LastName, state.Phone, state.SetupMode);
    }

    public async Task<SetupBootstrapResult> BootstrapAsync(SetupBootstrapDto dto)
    {
        var email = dto.OwnerEmail?.Trim() ?? "";
        var company = string.IsNullOrWhiteSpace(dto.CompanyName) ? null : dto.CompanyName.Trim();

        await using var db = dbFactory.CreateDbContext();
        var branding = await db.AppBrandings.FirstOrDefaultAsync();
        if (branding is null)
        {
            branding = new AppBranding();
            db.AppBrandings.Add(branding);
        }
        var state = ReadState(branding);

        // Powtórka po przerwanym połączeniu: konto już jest — odświeżamy tylko token wejścia.
        if (branding.SetupCompleted)
        {
            var existing = string.IsNullOrEmpty(email) ? null : await userManager.FindByEmailAsync(email);
            if (existing is not null && dto.PasswordHash is not null && await userManager.IsInRoleAsync(existing, Roles.Admin))
            {
                SetWelcomeToken(state, existing.Id, dto);
                branding.OnboardingJson = JsonSerializer.Serialize(state);
                await db.SaveChangesAsync();
            }
            return new(true, "Aplikacja była już skonfigurowana.");
        }

        if (company is not null) branding.CompanyName = company;
        if (dto.AppTheme is { } theme && AppThemes.Contains(theme)) branding.ThemeName = theme;
        state.CompanyName = company;
        state.Email = email;
        state.FirstName = dto.OwnerFirstName?.Trim();
        state.LastName = dto.OwnerLastName?.Trim();
        state.Phone = dto.OwnerPhone?.Trim();
        state.SetupMode = dto.SetupMode;

        // Bez skrótu hasła — tylko wypełniamy /setup; hasło trener ustawi sam.
        if (string.IsNullOrWhiteSpace(dto.PasswordHash) || !email.Contains('@'))
        {
            branding.OnboardingJson = JsonSerializer.Serialize(state);
            await db.SaveChangesAsync();
            return new(false, "Dane zapisane — kreator /setup jest wypełniony.");
        }

        var owner = await userManager.FindByEmailAsync(email);
        if (owner is not null && await userManager.IsInRoleAsync(owner, Roles.Root))
            throw new InvalidOperationException("Ten adres należy do konta technicznego.");
        if (owner is null)
        {
            owner = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = string.IsNullOrWhiteSpace(state.FirstName) ? "Administrator" : state.FirstName,
                LastName = string.IsNullOrWhiteSpace(state.LastName) ? (company ?? "") : state.LastName,
                PhoneNumber = string.IsNullOrWhiteSpace(state.Phone) ? null : state.Phone,
                // Skrót z Portalu (ASP.NET Identity) — hasło trener wpisał w kreatorze rejestracji.
                PasswordHash = dto.PasswordHash
            };
            var created = await userManager.CreateAsync(owner);
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        }
        else
        {
            owner.PasswordHash = dto.PasswordHash;
            await userManager.UpdateSecurityStampAsync(owner);
        }
        if (!await userManager.IsInRoleAsync(owner, Roles.Admin))
            await userManager.AddToRoleAsync(owner, Roles.Admin);
        owner.MustChangePassword = false;
        await userManager.UpdateAsync(owner);

        await ApplyOfferAsync(db, dto, owner.Id);

        branding.SetupCompleted = true;
        branding.SetupMode = dto.SetupMode ?? "self";
        branding.SetupCompletedAt = DateTime.UtcNow;
        SetWelcomeToken(state, owner.Id, dto);
        branding.OnboardingJson = JsonSerializer.Serialize(state);
        await db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(dto.SiteTemplate))
        {
            try
            {
                var site = SiteWidgets.BuildTemplate(dto.SiteTemplate);
                if (company is not null) site.SeoDescription = $"{company} — treningi, wolne terminy i zapisy online.";
                await siteContent.SaveAsync(site);
                await siteContent.DiscardDraftAsync();
            }
            catch (Exception ex) { logger.LogWarning(ex, "Nie udało się ustawić strony głównej z szablonu {Template}.", dto.SiteTemplate); }
        }

        return new(true, "Konto właściciela utworzone, aplikacja skonfigurowana.");
    }

    /// <summary>Rodzaje treningów i pakiet z kreatora zamiast domyślnych „45/60/90 minut”.</summary>
    private static async Task ApplyOfferAsync(ApplicationDbContext db, SetupBootstrapDto dto, string ownerId)
    {
        var offers = dto.Offers.Where(o => !string.IsNullOrWhiteSpace(o.Name)).Take(6).ToList();
        if (offers.Count == 0) return;

        // Świeża instancja: domyślne rodzaje nikt jeszcze nie użył — usuwamy; w innym razie tylko ukrywamy.
        var defaults = await db.SessionTypes.ToListAsync();
        if (!await db.Sessions.AnyAsync() && !await db.PackageOffers.AnyAsync() && !await db.SessionPackages.AnyAsync())
            db.SessionTypes.RemoveRange(defaults);
        else
            defaults.ForEach(t => t.IsActive = false);

        var types = offers.Select(o => new SessionType
        {
            Name = o.Name.Trim(),
            DurationMinutes = Math.Clamp(o.DurationMinutes, 15, 240),
            SinglePrice = o.Price is > 0 ? o.Price : null,
            IsPair = o.IsPair && !o.IsGroup,
            IsGroup = o.IsGroup,
            MaxParticipants = o.IsGroup ? Math.Clamp(o.MaxParticipants ?? 8, 2, 50) : null,
            IsActive = true
        }).ToList();
        db.SessionTypes.AddRange(types);
        await db.SaveChangesAsync();

        if (dto.Package is { SessionsCount: > 0, Price: > 0 } p)
        {
            var type = types[Math.Clamp(p.OfferIndex, 0, types.Count - 1)];
            db.PackageOffers.Add(new PackageOffer
            {
                Name = string.IsNullOrWhiteSpace(p.Name) ? $"Pakiet {p.SessionsCount} treningów" : p.Name.Trim(),
                SessionTypeId = type.Id,
                SessionsCount = p.SessionsCount,
                Price = p.Price,
                IsForPair = type.IsPair,
                IsActive = true,
                IsFeatured = true,
                CreatedByUserId = ownerId
            });
            await db.SaveChangesAsync();
        }
    }

    private static void SetWelcomeToken(OnboardingState state, string ownerId, SetupBootstrapDto dto)
    {
        state.OwnerUserId = ownerId;
        if (string.IsNullOrWhiteSpace(dto.WelcomeTokenHash)) return;
        state.WelcomeTokenHash = dto.WelcomeTokenHash.Trim().ToLowerInvariant();
        state.WelcomeTokenExpiresUtc = dto.WelcomeTokenExpiresUtc ?? DateTime.UtcNow.AddMinutes(30);
    }

    public async Task<string?> RedeemWelcomeTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        await using var db = dbFactory.CreateDbContext();
        var branding = await db.AppBrandings.FirstOrDefaultAsync();
        var state = ReadState(branding);
        if (branding is null || state.WelcomeTokenHash is null || state.OwnerUserId is null) return null;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();
        var match = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(state.WelcomeTokenHash));
        if (!match || state.WelcomeTokenExpiresUtc is not { } exp || exp < DateTime.UtcNow) return null;

        // Jednorazowy: po użyciu znika.
        state.WelcomeTokenHash = null;
        state.WelcomeTokenExpiresUtc = null;
        branding.OnboardingJson = JsonSerializer.Serialize(state);
        await db.SaveChangesAsync();
        return state.OwnerUserId;
    }
}
