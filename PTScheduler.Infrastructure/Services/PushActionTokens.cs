using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

/// <summary>Tokeny przycisków push: zaszyfrowane i podpisane (Data Protection), ważne 3 dni.</summary>
public sealed class PushActionTokens(IDataProtectionProvider provider) : IPushActionTokens
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(3);
    private readonly IDataProtector _protector = provider.CreateProtector("PushActions.v1");

    private sealed record Payload(string U, string K, int T, string? A, long E);

    public string Create(string userId, string kind, int targetId, string? arg = null) =>
        _protector.Protect(JsonSerializer.Serialize(new Payload(userId, kind, targetId, arg, DateTime.UtcNow.Add(Lifetime).Ticks)));

    public PushActionToken? Read(string token)
    {
        try
        {
            var p = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            if (p is null || p.E < DateTime.UtcNow.Ticks) return null;
            return new PushActionToken(p.U, p.K, p.T, p.A);
        }
        catch { return null; }
    }
}
