namespace PTScheduler.Application.Interfaces;

/// <summary>Jednorazowe, podpisane polecenia z przycisków powiadomień push (odbiorca, rodzaj, cel, argument, ważność).</summary>
public interface IPushActionTokens
{
    string Create(string userId, string kind, int targetId, string? arg = null);
    PushActionToken? Read(string token);
}

public sealed record PushActionToken(string UserId, string Kind, int TargetId, string? Arg);
