using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services.Photos;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// „Moje konto”: dane osobowe (u klienta zsynchronizowane z kartą klienta), zdjęcie profilowe
/// w prywatnym katalogu, stan zabezpieczeń konta i token linku kalendarza klienta.
/// </summary>
public partial class AccountService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IWebRootPathProvider webRoot,
    IAppClock clock,
    ILogger<AccountService> logger) : IAccountService
{
    public const long MaxAvatarBytes = 15 * 1024 * 1024;
    public const int AvatarEdge = 512, AvatarQuality = 82;

    private static readonly string[] RolePriority = [Roles.Admin, Roles.Trainer, Roles.Subordinate, Roles.Client];

    public static string AvatarUrl(string userId, DateTime? updatedAt) =>
        updatedAt is { } t ? $"/avatars/{Uri.EscapeDataString(userId)}.webp?v={t.Ticks.ToString(CultureInfo.InvariantCulture)}" : "";

    private string AvatarDir => Path.Combine(PrivateStorage.Root(webRoot), "avatars");

    public string? AvatarFilePath(string userId)
    {
        // Id użytkownika to GUID — mimo to nie wpuszczamy znaków ścieżki.
        if (string.IsNullOrWhiteSpace(userId) || userId.IndexOfAny(['/', '\\', '.']) >= 0) return null;
        var path = Path.Combine(AvatarDir, userId + ".webp");
        return File.Exists(path) ? path : null;
    }

    public async Task<string?> GetAvatarUrlAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        await using var db = dbFactory.CreateDbContext();
        var at = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.AvatarUpdatedAt).FirstOrDefaultAsync();
        return at is null ? null : AvatarUrl(userId, at);
    }

    public async Task<AccountOverviewDto?> GetOverviewAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return null;
        var roles = await userManager.GetRolesAsync(user);
        var role = RolePriority.FirstOrDefault(roles.Contains) ?? roles.FirstOrDefault() ?? "";

        await using var db = dbFactory.CreateDbContext();
        var lastLogin = await db.LoginLogs.AsNoTracking()
            .Where(l => l.UserId == userId && l.Success)
            .OrderByDescending(l => l.LoginTime)
            .Select(l => (DateTime?)l.LoginTime)
            .FirstOrDefaultAsync();
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ApplicationUserId == userId);

        AccountTrainerDto? trainer = null;
        if (client?.TrainerUserId is { } trainerId && await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == trainerId) is { } t)
        {
            var name = $"{t.FirstName} {t.LastName}".Trim();
            trainer = new AccountTrainerDto(name.Length > 0 ? name : t.Email ?? "Trener", t.Email, t.PhoneNumber,
                t.AvatarUpdatedAt is null ? null : AvatarUrl(t.Id, t.AvatarUpdatedAt));
        }

        return new AccountOverviewDto
        {
            UserId = user.Id,
            Email = user.Email ?? "",
            EmailConfirmed = user.EmailConfirmed,
            FirstName = user.FirstName ?? client?.FirstName ?? "",
            LastName = user.LastName ?? client?.LastName ?? "",
            Phone = user.PhoneNumber ?? client?.Phone,
            AvatarUrl = user.AvatarUpdatedAt is null ? null : AvatarUrl(user.Id, user.AvatarUpdatedAt),
            Role = role,
            HasPassword = await userManager.HasPasswordAsync(user),
            PasskeyCount = await PasskeyCountAsync(user),
            TwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user),
            LastLoginUtc = lastLogin?.ToUniversalTime(),
            ClientId = client?.Id,
            DateOfBirth = client?.DateOfBirth,
            TrainingGoal = client?.TrainingGoal,
            ClientSinceUtc = client?.CreatedAt,
            Trainer = trainer
        };
    }

    /// <summary>Liczba kluczy dostępu; 0, gdy magazyn ich nie obsługuje (np. baza bez tabeli kluczy) — strona konta działa dalej.</summary>
    private async Task<int> PasskeyCountAsync(ApplicationUser user)
    {
        try { return (await userManager.GetPasskeysAsync(user)).Count; }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            logger.LogWarning(ex, "Nie udało się odczytać kluczy dostępu użytkownika {UserId}.", user.Id);
            return 0;
        }
    }

    public async Task SaveProfileAsync(string userId, SaveAccountProfileDto dto)
    {
        var first = (dto.FirstName ?? "").Trim();
        var last = (dto.LastName ?? "").Trim();
        var phone = NormalizePhone(dto.Phone);
        var goal = string.IsNullOrWhiteSpace(dto.TrainingGoal) ? null : dto.TrainingGoal.Trim();

        if (first.Length == 0) throw new ArgumentException("Wpisz imię.");
        if (first.Length > 50 || last.Length > 50) throw new ArgumentException("Imię i nazwisko mogą mieć najwyżej 50 znaków.");
        if (phone is not null && !PhoneRegex().IsMatch(phone))
            throw new ArgumentException("Numer telefonu wygląda na niepoprawny — wpisz np. 600 100 200 albo +48 600 100 200.");
        if (dto.DateOfBirth is { } dob && (dob > clock.Today || dob.Year < 1900))
            throw new ArgumentException("Sprawdź datę urodzenia.");
        if (goal is { Length: > 500 }) throw new ArgumentException("Cel treningowy może mieć najwyżej 500 znaków.");

        var user = await userManager.FindByIdAsync(userId) ?? throw new ArgumentException("Nie znaleziono konta.");
        user.FirstName = first;
        user.LastName = last;
        user.PhoneNumber = phone;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description)));

        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == userId);
        if (client is not null)
        {
            client.FirstName = first;
            client.LastName = last;
            client.Phone = phone;
            client.DateOfBirth = dto.DateOfBirth;
            client.TrainingGoal = goal;
            await db.SaveChangesAsync();
        }
    }

    public async Task<string> SetAvatarAsync(string userId, byte[] image)
    {
        if (image.Length == 0) throw new ArgumentException("Wybierz zdjęcie.");
        if (image.Length > MaxAvatarBytes) throw new ArgumentException("Zdjęcie jest za duże (maks. 15 MB).");
        if (AvatarFilePathFor(userId) is not { } path) throw new ArgumentException("Nie znaleziono konta.");

        PhotoCompressor.Result square;
        try { square = PhotoCompressor.CompressSquare(image, AvatarEdge, AvatarQuality); }
        catch (InvalidDataException) { throw new ArgumentException("To nie wygląda na zdjęcie — wybierz plik JPG, PNG, WebP albo HEIC."); }

        var user = await userManager.FindByIdAsync(userId) ?? throw new ArgumentException("Nie znaleziono konta.");
        Directory.CreateDirectory(AvatarDir);
        await File.WriteAllBytesAsync(path, square.Data);
        user.AvatarUpdatedAt = clock.UtcNow;
        await userManager.UpdateAsync(user);
        var url = AvatarUrl(userId, user.AvatarUpdatedAt);
        await SyncClientPictureAsync(userId, url);
        return url;
    }

    public async Task RemoveAvatarAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return;
        if (AvatarFilePath(userId) is { } path)
        {
            try { File.Delete(path); }
            catch (Exception ex) { logger.LogWarning(ex, "Nie udało się usunąć zdjęcia profilowego {UserId}.", userId); }
        }
        user.AvatarUpdatedAt = null;
        await userManager.UpdateAsync(user);
        await SyncClientPictureAsync(userId, null);
    }

    public async Task<string> GetOrCreateCalendarTokenAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new ArgumentException("Nie znaleziono konta.");
        if (!string.IsNullOrEmpty(user.CalendarFeedToken)) return user.CalendarFeedToken;
        user.CalendarFeedToken = NewToken();
        await userManager.UpdateAsync(user);
        return user.CalendarFeedToken;
    }

    public async Task<string> ResetCalendarTokenAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new ArgumentException("Nie znaleziono konta.");
        user.CalendarFeedToken = NewToken();
        await userManager.UpdateAsync(user);
        return user.CalendarFeedToken;
    }

    public async Task<string?> FindUserByCalendarTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;
        await using var db = dbFactory.CreateDbContext();
        return await db.Users.AsNoTracking().Where(u => u.CalendarFeedToken == token).Select(u => u.Id).FirstOrDefaultAsync();
    }

    private string? AvatarFilePathFor(string userId) =>
        string.IsNullOrWhiteSpace(userId) || userId.IndexOfAny(['/', '\\', '.']) >= 0 ? null : Path.Combine(AvatarDir, userId + ".webp");

    /// <summary>Karta klienta pokazuje to samo zdjęcie (lista klientów, profil, grafik).</summary>
    private async Task SyncClientPictureAsync(string userId, string? url)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == userId);
        if (client is null) return;
        client.ProfilePictureUrl = url;
        await db.SaveChangesAsync();
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        return Regex.Replace(phone.Trim(), @"\s+", " ");
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex(@"^\+?[0-9 ()\-]{9,20}$")]
    private static partial Regex PhoneRegex();
}
