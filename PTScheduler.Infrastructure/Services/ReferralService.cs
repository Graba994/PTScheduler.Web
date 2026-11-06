using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Marketing;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class MarketingSettingsService(IDbContextFactory<ApplicationDbContext> dbFactory) : IMarketingSettingsService
{
    public async Task<MarketingSettingsDto> GetAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.MarketingSettings.AsNoTracking().FirstOrDefaultAsync() ?? new MarketingSettings();
        return new MarketingSettingsDto
        {
            ReferralEnabled = s.ReferralEnabled,
            ReferrerRewardKind = s.ReferrerRewardKind,
            ReferrerRewardValue = s.ReferrerRewardValue,
            ReferrerRewardSessionTypeId = s.ReferrerRewardSessionTypeId,
            FriendDiscountPercent = s.FriendDiscountPercent,
            MaxRewardsPerClient = s.MaxRewardsPerClient,
            ReviewsEnabled = s.ReviewsEnabled,
            GoogleReviewUrl = s.GoogleReviewUrl,
            ReviewAskAfterSessions = s.ReviewAskAfterSessions,
            VouchersEnabled = s.VouchersEnabled,
            VoucherAmounts = s.VoucherAmounts,
            VoucherValidMonths = s.VoucherValidMonths
        };
    }

    public async Task SaveAsync(MarketingSettingsDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.MarketingSettings.FirstOrDefaultAsync();
        if (s is null) { s = new MarketingSettings(); db.MarketingSettings.Add(s); }

        s.ReferralEnabled = dto.ReferralEnabled;
        s.ReferrerRewardKind = dto.ReferrerRewardKind;
        s.ReferrerRewardValue = dto.ReferrerRewardKind switch
        {
            ReferralRewardKind.FreeSessions => Math.Clamp(Math.Round(dto.ReferrerRewardValue), 1, 20),
            ReferralRewardKind.DiscountPercent => Math.Clamp(Math.Round(dto.ReferrerRewardValue), 1, 100),
            _ => Math.Clamp(Math.Round(dto.ReferrerRewardValue, 2), 1, 10000)
        };
        s.ReferrerRewardSessionTypeId = dto.ReferrerRewardSessionTypeId;
        s.FriendDiscountPercent = Math.Clamp(dto.FriendDiscountPercent, 0, 100);
        s.MaxRewardsPerClient = Math.Max(0, dto.MaxRewardsPerClient);

        s.ReviewsEnabled = dto.ReviewsEnabled;
        var url = dto.GoogleReviewUrl?.Trim();
        s.GoogleReviewUrl = Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? url : null;
        s.ReviewAskAfterSessions = Math.Clamp(dto.ReviewAskAfterSessions, 1, 100);
        s.VouchersEnabled = dto.VouchersEnabled;
        var amounts = dto.VoucherAmountList;
        s.VoucherAmounts = amounts.Count > 0
            ? string.Join(",", amounts.Select(a => a.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))
            : "100,200,300,500";
        s.VoucherValidMonths = Math.Clamp(dto.VoucherValidMonths, 1, 36);
        await db.SaveChangesAsync();
    }
}

public class ReferralService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IMarketingSettingsService settings,
    IWebPushService push,
    IAppClock clock,
    ILogger<ReferralService> logger) : IReferralService
{
    // Bez znaków mylących się na ekranie (0/O, 1/I/L).
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private static string RandomCode(int length) =>
        string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });

    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public async Task<string> GetOrCreateCodeAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FirstAsync(c => c.Id == clientId);
        if (!string.IsNullOrEmpty(client.ReferralCode)) return client.ReferralCode;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = RandomCode(8);
            if (await db.Clients.AnyAsync(c => c.ReferralCode == code)) continue;
            client.ReferralCode = code;
            try
            {
                await db.SaveChangesAsync();
                return code;
            }
            catch (DbUpdateException)
            {
                // Równoległe wygenerowanie tego samego kodu — losujemy jeszcze raz.
                client.ReferralCode = null;
            }
        }
        throw new InvalidOperationException("Nie udało się wygenerować kodu polecającego.");
    }

    public async Task<bool> IsValidCodeAsync(string code)
    {
        var norm = NormalizeCode(code);
        if (norm.Length is < 6 or > 16) return false;
        await using var db = dbFactory.CreateDbContext();
        return await db.Clients.AnyAsync(c => c.ReferralCode == norm);
    }

    public async Task<bool> RecordAsync(int newClientId, string? code)
    {
        var norm = NormalizeCode(code);
        if (norm.Length == 0) return false;
        var cfg = await settings.GetAsync();
        if (!cfg.ReferralEnabled) return false;

        await using var db = dbFactory.CreateDbContext();
        var referrer = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ReferralCode == norm);
        if (referrer is null || referrer.Id == newClientId) return false;
        if (await db.Referrals.AnyAsync(r => r.ReferredClientId == newClientId)) return false;

        var referral = new Referral
        {
            ReferrerClientId = referrer.Id,
            ReferredClientId = newClientId,
            CreatedAt = clock.UtcNow
        };
        if (cfg.FriendDiscountPercent > 0)
        {
            var coupon = NewCoupon("WITAJ", "percent", cfg.FriendDiscountPercent, validDays: 60,
                $"Powitalny rabat z polecenia (klient #{newClientId})");
            db.Coupons.Add(coupon);
            referral.FriendCouponCode = coupon.Code;
        }
        db.Referrals.Add(referral);
        await db.SaveChangesAsync();
        logger.LogInformation("Referral recorded: client {Referrer} → {Referred}.", referrer.Id, newClientId);
        return true;
    }

    public async Task<MyReferralsDto> GetMineAsync(int clientId)
    {
        var code = await GetOrCreateCodeAsync(clientId);
        var cfg = await settings.GetAsync();
        await using var db = dbFactory.CreateDbContext();
        var mine = await Query(db).Where(r => r.ReferrerClientId == clientId).ToListAsync();

        var welcome = await db.Referrals.AsNoTracking()
            .Where(r => r.ReferredClientId == clientId && r.FriendCouponCode != null)
            .Select(r => r.FriendCouponCode)
            .FirstOrDefaultAsync();
        if (welcome is not null)
        {
            var coupon = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == welcome);
            var usable = coupon is { IsActive: true } && coupon.UsedCount < Math.Max(1, coupon.MaxUses)
                && (coupon.ValidUntil is null || coupon.ValidUntil > clock.UtcNow);
            if (!usable) welcome = null;
        }

        return new MyReferralsDto
        {
            Code = code,
            Referrals = mine,
            WelcomeCouponCode = welcome,
            WelcomeDiscountPercent = cfg.FriendDiscountPercent
        };
    }

    public async Task<List<ReferralDto>> GetAllAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var q = db.Referrals.AsNoTracking();
        if (trainerUserId is not null)
            q = q.Where(r => r.ReferrerClient.TrainerUserId == trainerUserId || r.ReferredClient.TrainerUserId == trainerUserId);
        return await Query(db, q).ToListAsync();
    }

    private static IQueryable<ReferralDto> Query(ApplicationDbContext db, IQueryable<Referral>? source = null) =>
        (source ?? db.Referrals.AsNoTracking())
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReferralDto
            {
                Id = r.Id,
                ReferrerClientId = r.ReferrerClientId,
                ReferrerName = r.ReferrerClient.FirstName + " " + r.ReferrerClient.LastName,
                ReferredClientId = r.ReferredClientId,
                ReferredName = r.ReferredClient.FirstName + " " + r.ReferredClient.LastName,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
                RewardedAt = r.RewardedAt,
                RewardDescription = r.RewardDescription,
                RewardCouponCode = r.RewardCouponCode
            });

    public async Task<int> ProcessPendingAsync()
    {
        var cfg = await settings.GetAsync();
        if (!cfg.ReferralEnabled) return 0;

        await using var db = dbFactory.CreateDbContext();
        var ready = await db.Referrals
            .Include(r => r.ReferrerClient)
            .Where(r => r.Status == ReferralStatus.Pending
                && db.Sessions.Any(s => s.ClientId == r.ReferredClientId && s.Status == SessionStatus.Completed))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
        if (ready.Count == 0) return 0;

        int? sessionTypeId = null;
        if (cfg.ReferrerRewardKind == ReferralRewardKind.FreeSessions)
        {
            sessionTypeId = cfg.ReferrerRewardSessionTypeId is int st && await db.SessionTypes.AnyAsync(t => t.Id == st)
                ? st
                : await db.SessionTypes.Where(t => t.IsActive && !t.IsGroup).OrderBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync();
            if (sessionTypeId is null)
            {
                logger.LogWarning("Referral rewards skipped: no session type for free-session reward.");
                return 0;
            }
        }

        var referrerIds = ready.Select(r => r.ReferrerClientId).Distinct().ToList();
        var rewardedSoFar = await db.Referrals
            .Where(x => referrerIds.Contains(x.ReferrerClientId) && x.Status == ReferralStatus.Rewarded)
            .GroupBy(x => x.ReferrerClientId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var granted = 0;
        var now = clock.UtcNow;
        var notify = new List<(string UserId, string Reward)>();
        foreach (var r in ready)
        {
            if (cfg.MaxRewardsPerClient > 0 && rewardedSoFar.GetValueOrDefault(r.ReferrerClientId) >= cfg.MaxRewardsPerClient)
            {
                r.Status = ReferralStatus.Cancelled;
                r.RewardDescription = "Limit nagród za polecenia wykorzystany";
                continue;
            }

            if (cfg.ReferrerRewardKind == ReferralRewardKind.FreeSessions)
            {
                var count = (int)cfg.ReferrerRewardValue;
                var package = new SessionPackage
                {
                    ClientId = r.ReferrerClientId,
                    CreatedByUserId = r.ReferrerClient.TrainerUserId ?? "system",
                    Name = "Nagroda za polecenie",
                    Notes = $"Polecenie #{r.Id}",
                    SessionTypeId = sessionTypeId!.Value,
                    TotalSessions = count,
                    PricePerSession = 0,
                    IsPaid = true,
                    PaidAt = now,
                    PaymentReference = $"referral:{r.Id}",
                    PurchasedAt = now
                };
                db.SessionPackages.Add(package);
                await db.SaveChangesAsync();
                r.RewardPackageId = package.Id;
                r.RewardDescription = cfg.RewardLabel;
            }
            else
            {
                var type = cfg.ReferrerRewardKind == ReferralRewardKind.DiscountPercent ? "percent" : "amount";
                var coupon = NewCoupon("POLEC", type, cfg.ReferrerRewardValue, validDays: 180, $"Nagroda za polecenie #{r.Id}");
                db.Coupons.Add(coupon);
                r.RewardCouponCode = coupon.Code;
                r.RewardDescription = $"{cfg.RewardLabel}: {coupon.Code}";
            }
            r.Status = ReferralStatus.Rewarded;
            r.RewardedAt = now;
            rewardedSoFar[r.ReferrerClientId] = rewardedSoFar.GetValueOrDefault(r.ReferrerClientId) + 1;
            granted++;
            if (!string.IsNullOrEmpty(r.ReferrerClient.ApplicationUserId))
                notify.Add((r.ReferrerClient.ApplicationUserId, cfg.RewardLabel));
        }
        await db.SaveChangesAsync();

        foreach (var (userId, reward) in notify)
        {
            try
            {
                await push.SendAsync(userId, new PushMessageDto
                {
                    Title = "🎁 Dziękujemy za polecenie!",
                    Body = $"Twój znajomy odbył pierwszy trening — masz nagrodę: {reward}.",
                    Url = "/my/referrals"
                });
            }
            catch (Exception ex) { logger.LogWarning(ex, "Referral reward push failed."); }
        }
        if (granted > 0) logger.LogInformation("Granted {Count} referral reward(s).", granted);
        return granted;
    }

    public async Task<bool> CancelAsync(int referralId)
    {
        await using var db = dbFactory.CreateDbContext();
        var r = await db.Referrals.FirstOrDefaultAsync(x => x.Id == referralId && x.Status == ReferralStatus.Pending);
        if (r is null) return false;
        r.Status = ReferralStatus.Cancelled;
        r.RewardDescription = "Anulowane przez trenera";
        await db.SaveChangesAsync();
        return true;
    }

    private Coupon NewCoupon(string prefix, string type, decimal value, int validDays, string description) => new()
    {
        Code = $"{prefix}-{RandomCode(6)}",
        Description = description,
        DiscountType = type,
        DiscountValue = value,
        ValidFrom = clock.UtcNow,
        ValidUntil = clock.UtcNow.AddDays(validDays),
        MaxUses = 1,
        MaxUsesPerUser = 1,
        Scope = "all",
        IsActive = true,
        CreatedAt = clock.UtcNow
    };
}
