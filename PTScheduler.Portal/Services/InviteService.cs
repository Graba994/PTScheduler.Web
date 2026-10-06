using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using QRCoder;

namespace PTScheduler.Portal.Services;

/// <summary>Kody zaproszeń: tworzenie, link do kreatora, kod QR do zeskanowania telefonem i wysyłka e-mailem.</summary>
public class InviteService(IDbContextFactory<PortalDbContext> dbFactory, EmailService email, IConfiguration config)
{
    /// <summary>Okres próbny planu + ten dodatek ≈ 3 miesiące za darmo (oferta na spotkania z trenerami).</summary>
    public const int ThreeMonthsExtraDays = 76;

    public string PortalBase(string fallbackBase) =>
        config.GetValue<string>("Portal:PublicUrl")?.TrimEnd('/') is { Length: > 0 } pub ? pub : fallbackBase.TrimEnd('/');

    public string LinkFor(string code, string fallbackBase) => $"{PortalBase(fallbackBase)}/register?kod={Uri.EscapeDataString(code)}";

    public static string NewCode(string prefix = "PT") =>
        $"{prefix}-{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3))}";

    public async Task<(InviteCode? Code, string? Error)> CreateAsync(string? code, string? note, int maxUses, int extraDays, DateTime? expiresUtc)
    {
        var value = string.IsNullOrWhiteSpace(code) ? NewCode()
            : System.Text.RegularExpressions.Regex.Replace(code.Trim().ToUpperInvariant(), "[^A-Z0-9-]", "");
        if (value.Length < 4) return (null, "Kod musi mieć co najmniej 4 znaki (litery, cyfry, myślnik).");
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.InviteCodes.AnyAsync(c => c.Code == value)) return (null, $"Kod {value} już istnieje.");
        var invite = new InviteCode
        {
            Code = value,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(120, note.Trim().Length)],
            MaxUses = Math.Max(0, maxUses),
            ExtraTrialDays = Math.Clamp(extraDays, 0, 180),
            ExpiresAt = expiresUtc
        };
        db.InviteCodes.Add(invite);
        await db.SaveChangesAsync();
        return (invite, null);
    }

    /// <summary>Kod QR jako SVG (skaluje się do każdego rozmiaru, ostry na ekranie i w druku).</summary>
    public static string QrSvg(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(8, "#111111", "#ffffff", true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    public async Task<(bool Ok, string? Error)> SendAsync(InviteCode invite, string toEmail, string? name, string link)
    {
        var to = toEmail.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(to, out _)) return (false, "To nie wygląda na adres e-mail.");
        return await email.SendAsync(to, "Zaproszenie: Twoja aplikacja dla trenera", email.InviteEmailBody(name, invite.Code, link, invite.ExtraTrialDays));
    }
}
