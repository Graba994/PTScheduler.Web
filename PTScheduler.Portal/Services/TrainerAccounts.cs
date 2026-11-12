using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Konto trenera na stronie Hantly (/moje-konto): logowanie e-mailem i hasłem z kreatora albo linkiem
/// z e-maila. Osobne ciasteczko i schemat — trener nigdy nie dostaje dostępu do panelu administratora.
/// </summary>
public class TrainerAccounts(IDbContextFactory<PortalDbContext> dbFactory, IMemoryCache cache)
{
    public const string Scheme = "HantlyTrainer";
    public const string EmailClaim = "hantly:trainer_email";

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private const int MaxFailures = 5;

    public static string Normalize(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>Aplikacje trenera (bez usuniętych dawno temu), najnowsza pierwsza.</summary>
    public async Task<List<Tenant>> TenantsOfAsync(string email)
    {
        var e = Normalize(email);
        if (e.Length == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tenants.AsNoTracking().Include(t => t.Plan)
            .Where(t => t.OwnerEmail.ToLower() == e)
            .OrderBy(t => t.Status == TenantStatus.Destroyed).ThenByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Hasło z kreatora pasuje do którejś aplikacji trenera. Po 5 błędach z rzędu — 15 minut przerwy.</summary>
    public async Task<bool> CheckPasswordAsync(string email, string password)
    {
        var e = Normalize(email);
        if (e.Length == 0 || string.IsNullOrEmpty(password) || IsLocked(e)) return false;
        await using var db = await dbFactory.CreateDbContextAsync();
        var hashes = await db.Tenants.AsNoTracking()
            .Where(t => t.OwnerEmail.ToLower() == e && t.OwnerPasswordHash != null && t.Status != TenantStatus.Destroyed)
            .Select(t => t.OwnerPasswordHash!).ToListAsync();
        var hasher = new PasswordHasher<object>();
        var ok = hashes.Any(h => hasher.VerifyHashedPassword(new object(), h, password) != PasswordVerificationResult.Failed);
        if (ok) cache.Remove(FailKey(e));
        else cache.Set(FailKey(e), Failures(e) + 1, TimeSpan.FromMinutes(15));
        return ok;
    }

    public bool IsLocked(string email) => Failures(Normalize(email)) >= MaxFailures;

    /// <summary>Jednorazowy klucz logowania (po kreatorze albo w linku z e-maila), ważny 30 minut.</summary>
    public string CreateLoginToken(string email)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        cache.Set(TokenKey(token), Normalize(email), TokenLifetime);
        return token;
    }

    /// <summary>E-mail z klucza — klucz działa raz.</summary>
    public string? ConsumeLoginToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        if (!cache.TryGetValue(TokenKey(token), out string? email)) return null;
        cache.Remove(TokenKey(token));
        return email;
    }

    /// <summary>Link z e-maila najwyżej raz na minutę na adres — chroni skrzynkę trenera przed zasypaniem.</summary>
    public bool TryReserveLinkEmail(string email)
    {
        var key = "trainer-link:" + Normalize(email);
        if (cache.TryGetValue(key, out _)) return false;
        cache.Set(key, true, TimeSpan.FromMinutes(1));
        return true;
    }

    public static async Task SignInAsync(HttpContext ctx, string email)
    {
        var identity = new ClaimsIdentity([new Claim(EmailClaim, Normalize(email))], Scheme);
        await ctx.SignInAsync(Scheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
    }

    /// <summary>Zalogowany trener (e-mail) albo null.</summary>
    public static async Task<string?> CurrentEmailAsync(HttpContext ctx)
    {
        var result = await ctx.AuthenticateAsync(Scheme);
        return result.Succeeded ? result.Principal?.FindFirst(EmailClaim)?.Value : null;
    }

    private int Failures(string e) => cache.TryGetValue(FailKey(e), out int n) ? n : 0;
    private static string FailKey(string e) => "trainer-fail:" + e;
    private static string TokenKey(string t) => "trainer-token:" + t;
}
