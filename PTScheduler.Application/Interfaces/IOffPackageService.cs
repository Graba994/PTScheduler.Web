using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Rules;

namespace PTScheduler.Application.Interfaces;

/// <summary>Co klient może zrobić, gdy nie ma w pakiecie wybranego rodzaju treningu.</summary>
public sealed record OffPackageOptionsDto(
    bool RequiresPackage,
    bool CanPayOnline,
    decimal? SinglePrice,
    AtTrainerMode AtTrainer,
    int UnpaidVisits,
    int UnpaidLimit,
    IReadOnlyList<PaymentOptionDto> Providers);

/// <summary>Ile rzeczy czeka na trenera: prośby o termin i treningi do rozliczenia u niego.</summary>
public sealed record OffPackageCountsDto(int Requests, int UnpaidAtTrainer);

/// <summary>
/// Rezerwacja treningu, którego klient nie ma w pakiecie: płatność online przy rezerwacji
/// albo „zapłacę u trenera” (od razu albo po akceptacji trenera).
/// </summary>
public interface IOffPackageService
{
    Task<OffPackageOptionsDto> GetOptionsAsync(int clientId, int sessionTypeId);

    /// <summary>„Zapłacę u trenera”: od razu albo jako prośba do akceptacji — zależnie od ustawień i klienta.</summary>
    Task<SessionDto> BookAtTrainerAsync(string clientUserId, CreateSessionDto dto, int? partnerClientId = null);

    /// <summary>Płatność online: termin trzymany 15 minut, zwraca adres bramki płatności.</summary>
    Task<PaymentInitResult> BookOnlineAsync(string clientUserId, CreateSessionDto dto, string providerKey,
        string appBaseUrl, string buyerEmail, string customerIp);

    Task ApproveAsync(int sessionId, string actorUserId);
    Task RejectAsync(int sessionId, string actorUserId, string? reason);
    /// <summary>Trener odhacza płatność: „gotówka” albo „przelew”.</summary>
    Task MarkPaidAsync(int sessionId, string actorUserId, string via);

    /// <summary>Anuluje rezerwacje online, których nikt nie opłacił w 15 minut. Zwraca liczbę.</summary>
    Task<int> ExpireHoldsAsync();

    Task SetTrustedAsync(int clientId, bool trusted);
    Task<OffPackageCountsDto> GetCountsAsync(string? trainerUserId = null);
}
