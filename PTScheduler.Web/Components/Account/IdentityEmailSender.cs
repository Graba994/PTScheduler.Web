using Microsoft.AspNetCore.Identity;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Components.Account;

internal sealed class IdentityEmailSender(IEmailService emailService, ILogger<IdentityEmailSender> logger)
    : IEmailSender<ApplicationUser>
{
    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        if (!await emailService.IsEnabledAsync()) { logger.LogInformation("Email disabled — skipping confirmation to {Email}", email); return; }
        await emailService.SendAsync(email, user.UserName ?? email, "Potwierdź swój adres email",
            EmailTemplate("Potwierdź swój adres email",
                $"Cześć <strong>{user.UserName ?? email}</strong>!",
                "Kliknij poniższy przycisk, aby potwierdzić swój adres email i aktywować konto.",
                confirmationLink, "Potwierdź email"));
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        if (!await emailService.IsEnabledAsync()) { logger.LogInformation("Email disabled — skipping password reset to {Email}", email); return; }
        await emailService.SendAsync(email, user.UserName ?? email, "Reset hasła",
            EmailTemplate("Reset hasła",
                $"Cześć <strong>{user.UserName ?? email}</strong>!",
                "Otrzymaliśmy prośbę o reset hasła. Kliknij poniższy przycisk — link jest ważny przez 24 godziny.",
                resetLink, "Zresetuj hasło",
                "Jeśli nie prosiłeś o reset hasła, zignoruj tę wiadomość."));
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        if (!await emailService.IsEnabledAsync()) { logger.LogInformation("Email disabled — skipping reset code to {Email}", email); return; }
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
              <div style="background:#0284C7;border-radius:8px 8px 0 0;padding:24px;text-align:center">
                <h2 style="color:white;margin:0;font-size:20px">Kod resetu hasła</h2>
              </div>
              <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
                <p style="color:#374151;font-size:15px">Cześć <strong>{user.UserName ?? email}</strong>!</p>
                <p style="color:#374151;font-size:15px">Twój kod resetowania hasła:</p>
                <div style="background:#f3f4f6;border-radius:8px;padding:16px;text-align:center;font-size:28px;font-weight:bold;letter-spacing:6px;color:#111827;margin:20px 0">
                  {resetCode}
                </div>
                <p style="color:#6b7280;font-size:13px">Kod jest ważny przez 24 godziny.</p>
                <p style="color:#9ca3af;font-size:12px;margin-top:24px;padding-top:16px;border-top:1px solid #f3f4f6;text-align:center">
                  Wiadomość automatyczna — nie odpowiadaj na ten email.
                </p>
              </div>
            </div>
            """;
        await emailService.SendAsync(email, user.UserName ?? email, "Kod resetu hasła", html);
    }

    private static string EmailTemplate(string title, string greeting, string body, string link, string btnText, string? note = null) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:#0284C7;border-radius:8px 8px 0 0;padding:24px;text-align:center">
            <h2 style="color:white;margin:0;font-size:20px">{title}</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            <p style="color:#374151;font-size:15px">{greeting}</p>
            <p style="color:#374151;font-size:15px">{body}</p>
            <div style="text-align:center;margin:28px 0">
              <a href="{link}" style="background:#0284C7;color:white;text-decoration:none;padding:12px 32px;border-radius:6px;font-weight:600;font-size:15px;display:inline-block">{btnText}</a>
            </div>
            {(note is not null ? $"<p style=\"color:#6b7280;font-size:13px\">{note}</p>" : "")}
            <p style="color:#9ca3af;font-size:12px;margin-top:24px;padding-top:16px;border-top:1px solid #f3f4f6;text-align:center">
              Wiadomość automatyczna — nie odpowiadaj na ten email.
            </p>
          </div>
        </div>
        """;
}
