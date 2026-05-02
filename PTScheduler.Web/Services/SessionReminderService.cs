using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

public class SessionReminderService(IServiceScopeFactory scopeFactory, ILogger<SessionReminderService> logger) : BackgroundService
{
    private readonly HashSet<int> _sentIds = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), ct);

        while (!ct.IsCancellationRequested)
        {
            try { await SendRemindersAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Session reminder loop failed"); }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }

    private async Task SendRemindersAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        if (!await emailService.IsEnabledAsync()) return;

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var windowStart = DateTime.Now.AddHours(23);
        var windowEnd = DateTime.Now.AddHours(25);

        var sessions = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= windowStart && s.StartTime < windowEnd
                     && s.Status == SessionStatus.Scheduled)
            .ToListAsync();

        foreach (var session in sessions)
        {
            if (_sentIds.Contains(session.Id)) continue;

            try
            {
                var clientUser = await userManager.FindByIdAsync(session.Client.ApplicationUserId);
                if (clientUser?.Email is null) { _sentIds.Add(session.Id); continue; }

                var trainer = await userManager.FindByIdAsync(session.TrainerUserId);
                var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(trainerName)) trainerName = trainer?.Email ?? "Trener";

                var clientName = $"{session.Client.FirstName} {session.Client.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(clientName)) clientName = clientUser.Email!;

                var html = BuildReminderHtml(clientName, trainerName, session.SessionType.Name, session.StartTime, session.SessionType.DurationMinutes);
                await emailService.SendAsync(clientUser.Email, clientName, "Przypomnienie o jutrzejszej wizycie", html);

                _sentIds.Add(session.Id);
                logger.LogInformation("Sent 24h reminder for session {Id} to {Email}", session.Id, clientUser.Email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send reminder for session {Id}", session.Id);
            }
        }
    }

    private static string BuildReminderHtml(string clientName, string trainerName, string sessionType, DateTime startTime, int durationMin) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:#0284C7;border-radius:8px 8px 0 0;padding:24px;text-align:center">
            <h2 style="color:white;margin:0;font-size:20px">Przypomnienie o jutrzejszej wizycie</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            <p style="color:#374151;font-size:15px">Cześć <strong>{clientName}</strong>!</p>
            <p style="color:#374151;font-size:15px">Przypominamy o zaplanowanej wizycie jutro. Szczegóły:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Typ wizyty</td><td style="padding:8px 0;font-size:14px;font-weight:600">{sessionType}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Data i godzina</td><td style="padding:8px 0;font-size:14px;font-weight:600">{startTime:dddd\, dd MMMM yyyy} o {startTime:HH:mm}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Czas trwania</td><td style="padding:8px 0;font-size:14px;font-weight:600">{durationMin} minut</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Trener</td><td style="padding:8px 0;font-size:14px;font-weight:600">{trainerName}</td></tr>
            </table>
            <p style="color:#9ca3af;font-size:12px;margin-top:24px;padding-top:16px;border-top:1px solid #f3f4f6;text-align:center">
              Wiadomość automatyczna — nie odpowiadaj na ten email.
            </p>
          </div>
        </div>
        """;
}
