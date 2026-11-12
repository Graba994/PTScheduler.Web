using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Shop;

/// <summary>
/// „Kup ponownie”: która z aktywnych ofert to „ten sam pakiet”, który klient właśnie wykorzystał.
/// Pakiet nie pamięta oferty, z której powstał, więc dopasowujemy po rodzaju treningu, liczbie wejść i nazwie.
/// </summary>
public static class RebuyOffer
{
    public static PackageOfferDto? Match(IReadOnlyList<PackageOfferDto> offers, string packageName, int sessionTypeId, int totalSessions, bool forPair)
    {
        var sameType = offers.Where(o => o.SessionTypeId == sessionTypeId && o.IsForPair == forPair).ToList();
        bool SameName(PackageOfferDto o) => string.Equals(o.Name.Trim(), packageName.Trim(), StringComparison.OrdinalIgnoreCase);
        return sameType.FirstOrDefault(o => SameName(o) && o.SessionsCount == totalSessions)
            ?? sameType.FirstOrDefault(o => o.SessionsCount == totalSessions)
            ?? sameType.FirstOrDefault(SameName)
            ?? (sameType.Count == 1 ? sameType[0] : null);
    }
}
