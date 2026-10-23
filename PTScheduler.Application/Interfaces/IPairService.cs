using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Pary podopiecznych: kto z kim trenuje. Para podpowiada partnera przy rezerwacji
/// treningu w parze i przy zakupie pakietu dla pary.
/// </summary>
public interface IPairService
{
    /// <param name="trainerUserId">null — wszystkie pary.</param>
    Task<List<PairDto>> GetPairsAsync(string? trainerUserId = null);
    Task<List<PartnerDto>> GetPartnersAsync(int clientId);
    Task AddPairAsync(int clientAId, int clientBId, string? trainerUserId = null);
    Task RemovePairAsync(int pairId);

    /// <summary>
    /// Zakup pakietu dla pary przez podopiecznego: szuka partnera po e-mailu lub telefonie.
    /// Gdy e-maila nie ma w aplikacji, zakłada konto (link do ustawienia hasła przychodzi po płatności).
    /// </summary>
    Task<PartnerLookupResult> FindOrCreatePartnerAsync(int buyerClientId, string contact, string? firstName);

    /// <summary>Po zakupie online: partner dostaje wiadomość, że pakiet jest też jego (z linkiem do konta).</summary>
    Task NotifyPackageSharedAsync(int packageId, string? appBaseUrl);
}
