using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Wnioski z danych o wizytach, pakietach i treningach: którzy klienci wymagają
/// reakcji trenera (dawno nie byli, kończy się pakiet, nie ćwiczą według planu…).
/// </summary>
public interface IClientAttentionService
{
    /// <param name="trainerUserId">Klienci tego trenera; null = wszyscy.</param>
    /// <param name="includeTraining">Czy uwzględniać moduł treningowy (plan dostępny w pakiecie).</param>
    Task<List<ClientAttentionDto>> GetAsync(string? trainerUserId, bool includeTraining);
}
