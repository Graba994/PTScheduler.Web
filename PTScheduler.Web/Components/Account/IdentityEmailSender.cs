using Microsoft.AspNetCore.Identity;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Components.Account;

internal sealed class IdentityEmailSender(
    IEmailService emailService,
    IEmailTemplateService emailTemplateService,
    ILogger<IdentityEmailSender> logger)
    : IEmailSender<ApplicationUser>
{
    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        if (!await emailService.IsEnabledAsync()) { logger.LogInformation("Email disabled — skipping confirmation to {Email}", email); return; }
        var vars = new Dictionary<string, string>
        {
            ["UserName"] = user.UserName ?? email,
            ["ConfirmLink"] = confirmationLink,
            ["AccentColor"] = "#0284C7"
        };
        var (subject, html) = await emailTemplateService.RenderAsync("email-confirmation", vars);
        await emailService.SendAsync(email, user.UserName ?? email, subject, html);
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        if (!await emailService.IsEnabledAsync()) { logger.LogInformation("Email disabled — skipping password reset to {Email}", email); return; }
        var vars = new Dictionary<string, string>
        {
            ["UserName"] = user.UserName ?? email,
            ["ResetLink"] = resetLink,
            ["AccentColor"] = "#0284C7"
        };
        var (subject, html) = await emailTemplateService.RenderAsync("password-reset", vars);
        await emailService.SendAsync(email, user.UserName ?? email, subject, html);
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        if (!await emailService.IsEnabledAsync()) { logger.LogInformation("Email disabled — skipping reset code to {Email}", email); return; }
        var vars = new Dictionary<string, string>
        {
            ["UserName"] = user.UserName ?? email,
            ["ResetCode"] = resetCode
        };
        var (subject, html) = await emailTemplateService.RenderAsync("password-reset-code", vars);
        await emailService.SendAsync(email, user.UserName ?? email, subject, html);
    }
}
