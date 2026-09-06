using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class PublicBookingService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    ITrainerAvailabilityService availabilityService,
    IEmailService emailService,
    IEmailTemplateService emailTemplateService,
    IAppClock clock,
    ILogger<PublicBookingService> logger) : IPublicBookingService
{
    private const string IntroSessionTypeName = "Sesja wstępna";
    private static readonly CultureInfo PlCulture = CultureInfo.GetCultureInfo("pl-PL");

    public async Task<PublicIntroOfferDto> GetActiveOfferAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var configs = await db.IntroSessionConfigs
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .ToListAsync();

        // Only accounts that are actually trainers may be surfaced publicly —
        // an administrator account must never appear as a trainer.
        ApplicationUser? trainer = null;
        IntroSessionConfig? cfg = null;
        foreach (var candidate in configs)
        {
            var user = await userManager.FindByIdAsync(candidate.TrainerUserId);
            if (user is null) continue;
            if (!await userManager.IsInRoleAsync(user, Roles.Trainer)) continue;
            trainer = user;
            cfg = candidate;
            break;
        }

        if (cfg is null || trainer is null)
            return new PublicIntroOfferDto
            {
                IsAvailable = false,
                UnavailableReason = "Brak skonfigurowanej oferty wstępnej. Trener musi włączyć ofertę w ustawieniach."
            };

        return new PublicIntroOfferDto
        {
            IsAvailable = true,
            TrainerUserId = cfg.TrainerUserId,
            TrainerName = ResolveTrainerName(trainer),
            DurationMinutes = cfg.DurationMinutes,
            IsFree = cfg.IsFree,
            Price = cfg.Price,
            PromoPrice = cfg.PromoPrice,
            PromoValidUntil = cfg.PromoValidUntil,
            HasActivePromo = PromoRules.IsActive(cfg.PromoPrice, cfg.PromoValidUntil, clock.Today),
            Description = cfg.Description
        };
    }

    public async Task<List<BookingDayDto>> GetUpcomingSlotsAsync(string trainerUserId, int durationMinutes, int daysAhead = 14)
    {
        var result = new List<BookingDayDto>();
        var today = clock.Today;

        for (var i = 1; i <= daysAhead; i++)
        {
            var date = today.AddDays(i);
            var slots = await availabilityService.GetAvailableSlotsAsync(trainerUserId, date, durationMinutes);
            var available = slots.Where(s => s.IsAvailable).ToList();
            if (available.Count == 0) continue;

            result.Add(new BookingDayDto
            {
                Date = date,
                DayLabel = Capitalize(date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM", PlCulture)),
                Slots = available.Select(s => new BookingSlotDto { Start = s.Start }).ToList()
            });
        }

        return result;
    }

    public async Task<BookingResultDto> CreateBookingAsync(CreatePublicBookingDto dto, string appBaseUrl)
    {
        if (!dto.AcceptedTerms)
            return Fail("Aby zarezerwować, musisz zaakceptować warunki.");
        if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
            return Fail("Imię i nazwisko są wymagane.");
        if (string.IsNullOrWhiteSpace(dto.Email) || !dto.Email.Contains('@'))
            return Fail("Podaj prawidłowy adres email.");
        if (string.IsNullOrWhiteSpace(dto.TrainerUserId))
            return Fail("Brak trenera dla rezerwacji.");
        // SlotStart to zegar ścienny, więc porównanie musi iść przez zegar
        // aplikacji. DateTime.Now dałoby czas maszyny — w kontenerze UTC,
        // co przesuwało próg o offset strefy.
        if (dto.SlotStart < clock.LocalNow.AddMinutes(15))
            return Fail("Wybrany termin minął lub jest zbyt blisko teraźniejszości.");

        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.IntroSessionConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TrainerUserId == dto.TrainerUserId && c.IsActive);
        if (cfg is null)
            return Fail("Oferta sesji wstępnej jest nieaktywna.");

        var trainerAccount = await userManager.FindByIdAsync(dto.TrainerUserId);
        if (trainerAccount is null || !await userManager.IsInRoleAsync(trainerAccount, Roles.Trainer))
            return Fail("Oferta sesji wstępnej jest nieaktywna.");

        if (!await availabilityService.IsSlotFreeAsync(dto.TrainerUserId, dto.SlotStart, cfg.DurationMinutes))
            return Fail("Wybrany termin jest już zajęty. Wybierz inny.");

        var emailNorm = dto.Email.Trim();
        if (await userManager.FindByEmailAsync(emailNorm) is not null)
            return Fail("Konto z tym adresem email już istnieje. Zaloguj się, aby zarezerwować sesję z poziomu konta.");

        // Lazy-create or fetch the "Sesja wstępna" SessionType matching this duration.
        // Different durations get different rows so trainer with 30/60/90 intros each gets its own entry.
        var sessionType = await db.SessionTypes
            .FirstOrDefaultAsync(t => t.Name == IntroSessionTypeName && t.DurationMinutes == cfg.DurationMinutes);
        if (sessionType is null)
        {
            sessionType = new SessionType
            {
                Name = IntroSessionTypeName,
                DurationMinutes = cfg.DurationMinutes,
                IsActive = true
            };
            db.SessionTypes.Add(sessionType);
            await db.SaveChangesAsync();
        }

        // Create user. EmailConfirmed=true — they came via a real email-verified booking flow,
        // and the trainer will verify identity in person at first session.
        var user = new ApplicationUser
        {
            UserName = emailNorm,
            Email = emailNorm,
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            EmailConfirmed = true
        };
        var tempPassword = GenerateTempPassword();
        var createResult = await userManager.CreateAsync(user, tempPassword);
        if (!createResult.Succeeded)
            return Fail("Nie udało się założyć konta: " + string.Join("; ", createResult.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, Roles.Client);

        var client = new Client
        {
            ApplicationUserId = user.Id,
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
            TrainingGoal = string.IsNullOrWhiteSpace(dto.TrainingGoal) ? null : dto.TrainingGoal.Trim(),
            Status = ClientStatus.Pending,
            AllowSelfBooking = false,
            TrainerUserId = dto.TrainerUserId,
            CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var session = new Session
        {
            ClientId = client.Id,
            SessionTypeId = sessionType.Id,
            TrainerUserId = dto.TrainerUserId,
            StartTime = dto.SlotStart,
            Status = SessionStatus.Scheduled,
            Notes = "[Z publicznej rezerwacji /book]"
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        // Send confirmation/notification emails. Failures here MUST NOT undo the booking —
        // the slot is already reserved. Just log so the trainer can resend manually.
        var trainer = await userManager.FindByIdAsync(dto.TrainerUserId);
        var trainerName = trainer is null ? "Trener" : ResolveTrainerName(trainer);
        var emailSent = false;

        try
        {
            var setPwLink = await BuildSetPasswordLinkAsync(user, appBaseUrl);
            var when = Capitalize(dto.SlotStart.ToString("dddd, d MMMM, HH:mm", PlCulture));
            var price = cfg.IsFree
                ? "<strong style=\"color:#16a34a\">bezpłatnie</strong>"
                : $"<strong>{cfg.Price.ToString("N2", PlCulture)} zł</strong>";
            var clientVars = new Dictionary<string, string>
            {
                ["ClientName"] = user.FirstName ?? "",
                ["TrainerName"] = trainerName,
                ["SessionDate"] = when,
                ["Duration"] = cfg.DurationMinutes.ToString(),
                ["Price"] = price,
                ["SetPasswordLink"] = setPwLink,
                ["AccentColor"] = "#0284C7"
            };
            var (clientSubject, clientHtml) = await emailTemplateService.RenderAsync("client-welcome", clientVars);
            await emailService.SendAsync(emailNorm, $"{user.FirstName} {user.LastName}".Trim(),
                clientSubject, clientHtml);

            if (!string.IsNullOrEmpty(trainer?.Email))
            {
                var phoneRow = string.IsNullOrEmpty(dto.Phone) ? "" :
                    $"<p style=\"margin:6px 0\"><strong>📞 Telefon:</strong> {dto.Phone}</p>";
                var goalRow = string.IsNullOrEmpty(dto.TrainingGoal) ? "" :
                    $"<p style=\"margin:10px 0 0;padding-top:8px;border-top:1px solid #e5e7eb\"><strong>🎯 Cel treningowy:</strong><br/>{dto.TrainingGoal}</p>";
                var trainerVars = new Dictionary<string, string>
                {
                    ["TrainerName"] = trainerName,
                    ["ClientName"] = $"{client.FirstName} {client.LastName}",
                    ["ClientEmail"] = dto.Email,
                    ["ClientPhone"] = dto.Phone ?? "",
                    ["PhoneRow"] = phoneRow,
                    ["SessionDate"] = when,
                    ["Duration"] = cfg.DurationMinutes.ToString(),
                    ["TrainingGoal"] = dto.TrainingGoal ?? "",
                    ["GoalRow"] = goalRow
                };
                var (trainerSubject, trainerHtml) = await emailTemplateService.RenderAsync("trainer-new-booking", trainerVars);
                await emailService.SendAsync(trainer.Email, trainerName, trainerSubject, trainerHtml);
            }

            emailSent = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Public booking emails failed (booking {BookingId} succeeded).", session.Id);
        }

        return new BookingResultDto
        {
            Success = true,
            ConfirmedSlot = dto.SlotStart,
            ClientEmail = emailNorm,
            TrainerName = trainerName,
            DurationMinutes = cfg.DurationMinutes,
            EmailSent = emailSent
        };
    }

    // ---- helpers ----

    private async Task<string> BuildSetPasswordLinkAsync(ApplicationUser user, string appBaseUrl)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encoded = Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return $"{appBaseUrl.TrimEnd('/')}/Account/ResetPassword?code={encoded}";
    }

    /// <summary>RFC 4648 §5 base64url (no padding) — matches Microsoft.AspNetCore.WebUtilities.WebEncoders.</summary>
    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ResolveTrainerName(ApplicationUser trainer)
    {
        var fullName = $"{trainer.FirstName} {trainer.LastName}".Trim();
        if (!string.IsNullOrEmpty(fullName)) return fullName;
        return trainer.UserName ?? trainer.Email ?? "Trener";
    }

    private static BookingResultDto Fail(string err) => new() { Success = false, ErrorMessage = err };

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], PlCulture) + s[1..];

    private static string GenerateTempPassword()
    {
        // Identity rules in Program.cs: ≥8 chars, requires digit. We generate 14 alpha chars + digit + special.
        const string alpha = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(14);
        var sb = new StringBuilder(16);
        foreach (var b in bytes) sb.Append(alpha[b % alpha.Length]);
        sb.Append('1');
        sb.Append('!');
        return sb.ToString();
    }

}
