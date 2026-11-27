using PTScheduler.Application.Marketing;

namespace PTScheduler.Application.Interfaces;

public interface IMarketingSettingsService
{
    Task<MarketingSettingsDto> GetAsync();
    Task SaveAsync(MarketingSettingsDto dto);
}

public interface IReferralService
{
    /// <summary>Kod polecający klienta — tworzony przy pierwszym wywołaniu.</summary>
    Task<string> GetOrCreateCodeAsync(int clientId);

    /// <summary>Czy kod należy do istniejącego klienta (do obsługi linku /r/{kod}).</summary>
    Task<bool> IsValidCodeAsync(string code);

    /// <summary>
    /// Zapisuje, że nowy klient przyszedł z polecenia. Nic nie robi, gdy program jest wyłączony,
    /// kod jest nieznany, klient poleca sam siebie albo był już polecony.
    /// </summary>
    Task<bool> RecordAsync(int newClientId, string? code);

    Task<MyReferralsDto> GetMineAsync(int clientId);
    Task<List<ReferralDto>> GetAllAsync(string? trainerUserId = null);

    /// <summary>Przyznaje nagrody za polecenia, w których znajomy odbył już pierwszą wizytę.</summary>
    Task<int> ProcessPendingAsync();

    Task<bool> CancelAsync(int referralId);
}
