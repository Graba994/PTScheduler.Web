using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class EmailTemplateService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IBrandingService brandingService) : IEmailTemplateService
{
    public async Task<List<EmailTemplateDto>> GetAllAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var customized = await db.EmailTemplates.ToListAsync();
        var customMap = customized.ToDictionary(t => t.Key);

        return Defaults.Select(d =>
        {
            var dto = CloneDefault(d.Value);
            if (customMap.TryGetValue(d.Key, out var custom))
                ApplyCustom(dto, custom);
            return dto;
        }).ToList();
    }

    public async Task<EmailTemplateDto> GetAsync(string key)
    {
        if (!Defaults.TryGetValue(key, out var def))
            throw new ArgumentException($"Unknown template key: {key}");

        var dto = CloneDefault(def);
        await using var db = dbFactory.CreateDbContext();
        var custom = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Key == key);
        if (custom is not null) ApplyCustom(dto, custom);
        return dto;
    }

    public async Task SaveAsync(string key, EmailTemplateDto dto)
    {
        if (!Defaults.ContainsKey(key))
            throw new ArgumentException($"Unknown template key: {key}");

        await using var db = dbFactory.CreateDbContext();
        var entity = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Key == key);
        if (entity is null)
        {
            entity = new EmailTemplate { Key = key };
            db.EmailTemplates.Add(entity);
        }
        entity.Subject = dto.Subject;
        entity.HeaderTitle = dto.HeaderTitle;
        entity.HtmlBody = dto.HtmlBody;
        entity.AccentColor = dto.AccentColor;
        entity.FooterText = dto.FooterText;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task ResetAsync(string key)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Key == key);
        if (entity is not null)
        {
            db.EmailTemplates.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    public async Task<(string Subject, string Html)> RenderAsync(string key, Dictionary<string, string> variables)
    {
        var template = await GetAsync(key);
        var branding = await brandingService.GetAsync();
        var companyName = branding.CompanyName ?? "PTScheduler";
        var accentColor = template.AccentColor;

        variables["CompanyName"] = companyName;
        variables["AccentColor"] = accentColor;

        string? logoHtml = null;
        if (!string.IsNullOrEmpty(branding.LogoPath))
        {
            variables["Logo"] = branding.LogoPath;
            logoHtml = "<img src=\"{{Logo}}\" alt=\"" + companyName + "\" style=\"max-height:48px;max-width:180px;display:block;margin:0 auto 12px\" />";
        }

        var subject = ReplacePlaceholders(template.Subject, variables);
        var headerTitle = ReplacePlaceholders(template.HeaderTitle, variables);
        var body = ReplacePlaceholders(template.HtmlBody, variables);
        var footer = ReplacePlaceholders(template.FooterText, variables);
        var logoRendered = logoHtml is not null ? ReplacePlaceholders(logoHtml, variables) : null;

        var html = WrapInLayout(headerTitle, body, footer, accentColor, logoRendered);
        return (subject, html);
    }

    private static string ReplacePlaceholders(string text, Dictionary<string, string> vars)
    {
        foreach (var (k, v) in vars)
            text = text.Replace($"{{{{{k}}}}}", v);
        return text;
    }

    private static string WrapInLayout(string headerTitle, string body, string footer, string accent, string? logoHtml = null) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:{accent};border-radius:8px 8px 0 0;padding:24px;text-align:center">
            {(logoHtml is not null ? logoHtml : "")}
            <h2 style="color:white;margin:0;font-size:20px">{headerTitle}</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            {body}
            <p style="color:#9ca3af;font-size:12px;margin-top:24px;padding-top:16px;border-top:1px solid #f3f4f6;text-align:center">
              {footer}
            </p>
          </div>
        </div>
        """;

    private static EmailTemplateDto CloneDefault(DefaultTemplate d) => new()
    {
        Key = d.Key,
        DisplayName = d.DisplayName,
        Icon = d.Icon,
        Subject = d.Subject,
        HeaderTitle = d.HeaderTitle,
        HtmlBody = d.HtmlBody,
        AccentColor = d.AccentColor,
        FooterText = d.FooterText,
        AvailableVariables = d.Variables,
        IsCustomized = false
    };

    private static void ApplyCustom(EmailTemplateDto dto, EmailTemplate entity)
    {
        dto.Subject = entity.Subject;
        dto.HeaderTitle = entity.HeaderTitle;
        dto.HtmlBody = entity.HtmlBody;
        dto.AccentColor = entity.AccentColor;
        dto.FooterText = entity.FooterText;
        dto.IsCustomized = true;
    }

    // ────────────────────────────────────────────────────────────
    //  DEFAULT TEMPLATES
    // ────────────────────────────────────────────────────────────

    private sealed record DefaultTemplate(
        string Key, string DisplayName, string Icon,
        string Subject, string HeaderTitle, string HtmlBody,
        string AccentColor, string FooterText, List<string> Variables);

    private static readonly Dictionary<string, DefaultTemplate> Defaults = new()
    {
        ["session-booked"] = new(
            "session-booked", "Potwierdzenie rezerwacji", "bi-calendar-check",
            "Potwierdzenie rezerwacji wizyty",
            "Potwierdzenie rezerwacji ✅",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Twoja wizyta została zarezerwowana. Szczegóły:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionType}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Data i godzina</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionDate}} o {{SessionTime}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Czas trwania</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{Duration}} minut</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Trener</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{TrainerName}}</td></tr>
            </table>
            """,
            "#0284C7",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["ClientName", "TrainerName", "SessionType", "SessionDate", "SessionTime", "Duration"]),

        ["session-cancelled"] = new(
            "session-cancelled", "Anulowanie wizyty", "bi-calendar-x",
            "Anulowanie wizyty",
            "Wizyta anulowana ❌",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Twoja wizyta została anulowana. Szczegóły:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionType}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Data i godzina</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionDate}} o {{SessionTime}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Trener</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{TrainerName}}</td></tr>
              {{ReasonRow}}
            </table>
            """,
            "#DC2626",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["ClientName", "TrainerName", "SessionType", "SessionDate", "SessionTime", "Reason", "ReasonRow"]),

        ["session-cancelled-by-client"] = new(
            "session-cancelled-by-client", "Klient anulował wizytę", "bi-person-x",
            "Klient anulował wizytę — {{SessionDate}} {{SessionTime}}",
            "Klient anulował wizytę 📋",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{TrainerName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Klient <strong>{{ClientName}}</strong> anulował wizytę:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionType}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Data i godzina</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionDate}} o {{SessionTime}}</td></tr>
              {{ReasonRow}}
            </table>
            """,
            "#9333EA",
            "Wiadomość automatyczna z PTScheduler.",
            ["ClientName", "TrainerName", "SessionType", "SessionDate", "SessionTime", "Reason", "ReasonRow"]),

        ["session-rescheduled"] = new(
            "session-rescheduled", "Zmiana terminu", "bi-arrow-repeat",
            "Zmiana terminu wizyty",
            "Zmiana terminu wizyty 🔄",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Twój trener <strong>{{TrainerName}}</strong> zmienił termin wizyty:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionType}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Stary termin</td><td style="padding:8px 0;font-size:14px;text-decoration:line-through;color:#9ca3af">{{OldDate}} {{OldTime}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Nowy termin</td><td style="padding:8px 0;font-size:14px;font-weight:600;color:#16A34A">{{NewDate}} o {{NewTime}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Trener</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{TrainerName}}</td></tr>
            </table>
            """,
            "#0284C7",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["ClientName", "TrainerName", "SessionType", "OldDate", "OldTime", "NewDate", "NewTime"]),

        ["session-reminder"] = new(
            "session-reminder", "Przypomnienie o treningu", "bi-bell",
            "Przypomnienie — jutro masz trening",
            "Przypomnienie — jutro trening 💪",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Przypominamy o jutrzejszym treningu. Do zobaczenia!</p>
            <div style="background:#f3f4f6;border-radius:8px;padding:16px;margin:20px 0">
              <p style="margin:6px 0"><strong>📅 Data:</strong> {{SessionDate}}</p>
              <p style="margin:6px 0"><strong>🕒 Godzina:</strong> {{SessionTime}}</p>
              <p style="margin:6px 0"><strong>⏱️ Czas trwania:</strong> {{Duration}} min</p>
              <p style="margin:6px 0"><strong>💪 Trener:</strong> {{TrainerName}}</p>
              <p style="margin:6px 0"><strong>🏋️ Typ:</strong> {{SessionType}}</p>
            </div>
            <p style="color:#6b7280;font-size:13px">Jeśli nie możesz dotrzeć — <strong>daj znać trenerowi</strong> lub anuluj sesję w aplikacji.</p>
            """,
            "#0284C7",
            "Wiadomość automatyczna. Możesz wyłączyć przypomnienia w ustawieniach konta.",
            ["ClientName", "TrainerName", "SessionType", "SessionDate", "SessionTime", "Duration"]),

        ["package-assigned"] = new(
            "package-assigned", "Nowy pakiet", "bi-box-seam",
            "Nowy pakiet — {{PackageName}}",
            "Nowy pakiet sesji 🎁",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Otrzymałeś nowy pakiet treningowy:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Nazwa pakietu</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{PackageName}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Typ sesji</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{SessionType}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Liczba sesji</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{TotalSessions}}</td></tr>
              {{ExpiresRow}}
            </table>
            """,
            "#16A34A",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["ClientName", "PackageName", "SessionType", "TotalSessions", "ExpiresAt", "ExpiresRow"]),

        ["client-welcome"] = new(
            "client-welcome", "Powitanie klienta", "bi-person-plus",
            "Twoja sesja wstępna jest umówiona",
            "Twoja sesja wstępna jest umówiona ✅",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Dziękujemy za rezerwację. Czekamy na Ciebie:</p>
            <div style="background:#f3f4f6;border-radius:8px;padding:16px;margin:20px 0">
              <p style="margin:6px 0"><strong>📅 Termin:</strong> {{SessionDate}}</p>
              <p style="margin:6px 0"><strong>⏱️ Czas trwania:</strong> {{Duration}} min</p>
              <p style="margin:6px 0"><strong>💪 Trener:</strong> {{TrainerName}}</p>
              <p style="margin:6px 0"><strong>💵 Koszt:</strong> {{Price}}</p>
            </div>
            <p style="color:#374151;font-size:15px"><strong>Aby zalogować się i zobaczyć grafik</strong>, ustaw hasło klikając poniższy przycisk:</p>
            <div style="text-align:center;margin:24px 0">
              <a href="{{SetPasswordLink}}" style="background:{{AccentColor}};color:white;text-decoration:none;padding:12px 32px;border-radius:6px;font-weight:600;font-size:15px;display:inline-block">Ustaw hasło</a>
            </div>
            <p style="color:#6b7280;font-size:13px">Po ustawieniu hasła zalogujesz się swoim adresem email.</p>
            """,
            "#0284C7",
            "Wiadomość automatyczna — w razie pytań odpisz na ten email.",
            ["ClientName", "TrainerName", "SessionDate", "Duration", "Price", "SetPasswordLink", "AccentColor"]),

        ["trainer-new-booking"] = new(
            "trainer-new-booking", "Nowy zapis klienta", "bi-person-fill-add",
            "Nowy zapis: {{ClientName}}",
            "Nowy klient ze strony rezerwacji 🎉",
            """
            <p style="color:#374151;font-size:15px">Cześć {{TrainerName}}, masz nowy zapis:</p>
            <div style="background:#f3f4f6;border-radius:8px;padding:16px;margin:20px 0">
              <p style="margin:6px 0"><strong>👤 Imię i nazwisko:</strong> {{ClientName}}</p>
              <p style="margin:6px 0"><strong>📧 Email:</strong> {{ClientEmail}}</p>
              {{PhoneRow}}
              <p style="margin:6px 0"><strong>📅 Termin:</strong> {{SessionDate}} ({{Duration}} min)</p>
              {{GoalRow}}
            </div>
            <p style="color:#6b7280;font-size:14px">Klient ma status <strong>Oczekuje</strong>. Zatwierdź go w panelu <em>Klienci</em>, żeby uzyskał pełny dostęp do aplikacji.</p>
            """,
            "#16A34A",
            "Wiadomość automatyczna z PTScheduler.",
            ["TrainerName", "ClientName", "ClientEmail", "ClientPhone", "PhoneRow", "SessionDate", "Duration", "TrainingGoal", "GoalRow"]),

        ["password-reset"] = new(
            "password-reset", "Reset hasła", "bi-key",
            "Reset hasła",
            "Reset hasła 🔑",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{UserName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Otrzymaliśmy prośbę o reset hasła. Kliknij poniższy przycisk — link jest ważny przez 24 godziny.</p>
            <div style="text-align:center;margin:28px 0">
              <a href="{{ResetLink}}" style="background:{{AccentColor}};color:white;text-decoration:none;padding:12px 32px;border-radius:6px;font-weight:600;font-size:15px;display:inline-block">Zresetuj hasło</a>
            </div>
            <p style="color:#6b7280;font-size:13px">Jeśli nie prosiłeś o reset hasła, zignoruj tę wiadomość.</p>
            """,
            "#0284C7",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["UserName", "ResetLink", "AccentColor"]),

        ["password-reset-code"] = new(
            "password-reset-code", "Kod resetu hasła", "bi-123",
            "Kod resetu hasła",
            "Kod resetu hasła 🔐",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{UserName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Twój kod resetowania hasła:</p>
            <div style="background:#f3f4f6;border-radius:8px;padding:16px;text-align:center;font-size:28px;font-weight:bold;letter-spacing:6px;color:#111827;margin:20px 0">
              {{ResetCode}}
            </div>
            <p style="color:#6b7280;font-size:13px">Kod jest ważny przez 24 godziny.</p>
            """,
            "#0284C7",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["UserName", "ResetCode"]),

        ["email-confirmation"] = new(
            "email-confirmation", "Potwierdzenie email", "bi-envelope-check",
            "Potwierdź swój adres email",
            "Potwierdź swój adres email ✉️",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{UserName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Kliknij poniższy przycisk, aby potwierdzić swój adres email i aktywować konto.</p>
            <div style="text-align:center;margin:28px 0">
              <a href="{{ConfirmLink}}" style="background:{{AccentColor}};color:white;text-decoration:none;padding:12px 32px;border-radius:6px;font-weight:600;font-size:15px;display:inline-block">Potwierdź email</a>
            </div>
            """,
            "#0284C7",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["UserName", "ConfirmLink", "AccentColor"]),

        ["payment-reminder"] = new(
            "payment-reminder", "Przypomnienie o płatności", "bi-credit-card",
            "Przypomnienie o płatności za pakiet",
            "Przypomnienie o płatności 💳",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Przypominamy o oczekującej płatności za pakiet treningowy:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Pakiet</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{PackageName}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Kwota</td><td style="padding:8px 0;font-size:14px;font-weight:600;color:#DC2626">{{Amount}} zł</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Trener</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{TrainerName}}</td></tr>
            </table>
            <p style="color:#6b7280;font-size:13px">Prosimy o uregulowanie płatności. W razie pytań skontaktuj się z trenerem.</p>
            """,
            "#D97706",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["ClientName", "PackageName", "Amount", "TrainerName"]),

        ["package-expiring"] = new(
            "package-expiring", "Wygasający pakiet", "bi-hourglass-split",
            "Twój pakiet wkrótce wygasa",
            "Pakiet wygasa ⏰",
            """
            <p style="color:#374151;font-size:15px">Cześć <strong>{{ClientName}}</strong>!</p>
            <p style="color:#374151;font-size:15px">Twój pakiet treningowy wkrótce wygasa:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Pakiet</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{PackageName}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Wygasa</td><td style="padding:8px 0;font-size:14px;font-weight:600;color:#DC2626">{{ExpiresAt}}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Pozostało sesji</td><td style="padding:8px 0;font-size:14px;font-weight:600">{{RemainingCredits}}</td></tr>
            </table>
            <p style="color:#6b7280;font-size:13px">Skontaktuj się z trenerem <strong>{{TrainerName}}</strong>, aby przedłużyć pakiet lub zakupić nowy.</p>
            """,
            "#D97706",
            "Wiadomość automatyczna — nie odpowiadaj na ten email.",
            ["ClientName", "PackageName", "ExpiresAt", "RemainingCredits", "TrainerName"]),
    };
}
