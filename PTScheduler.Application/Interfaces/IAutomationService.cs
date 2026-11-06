using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>Automatyczne wiadomości do klientów: powitanie, „dawno Cię nie było”, koniec pakietu, urodziny.</summary>
public interface IAutomationService
{
    /// <summary>Wszystkie reguły (zapisane albo domyślne, wyłączone).</summary>
    Task<List<AutomationRuleDto>> GetRulesAsync();
    Task SaveRuleAsync(AutomationRuleDto dto);
    Task<List<AutomationLogDto>> GetLogAsync(int take = 100);
    Task<List<AutomationStatsDto>> GetStatsAsync(int days = 90);

    /// <summary>Jeden przebieg: oznacza powroty i wysyła należne wiadomości. Zwraca liczbę wysłanych.</summary>
    Task<int> RunAsync(AutomationChannels channels);
}
