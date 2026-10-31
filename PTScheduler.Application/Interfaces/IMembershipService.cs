using PTScheduler.Application.Memberships;

namespace PTScheduler.Application.Interfaces;

public interface IMembershipService
{
    Task<List<MembershipPlanDto>> GetPlansAsync(bool onlyActive = false, bool onlyShop = false);
    Task<int> SavePlanAsync(MembershipPlanDto dto, string userId);

    Task<List<MembershipDto>> GetMembershipsAsync(string? trainerUserId = null);
    Task<List<MembershipDto>> GetClientMembershipsAsync(int clientId);

    /// <summary>Trener zapisuje klienta na karnet (pierwszy okres od <paramref name="start"/>).</summary>
    Task<(bool Ok, string? Error)> SubscribeAsync(int clientId, int planId, DateOnly start, decimal? priceOverride);

    Task MarkPeriodPaidAsync(int periodId, string reference);
    Task WaivePeriodAsync(int periodId);

    /// <summary>Rezygnacja: <paramref name="immediately"/> = od razu, inaczej z końcem okresu.</summary>
    Task CancelAsync(int membershipId, bool immediately);
    Task ResumeCancelledAsync(int membershipId);
    Task PauseAsync(int membershipId);
    Task ResumeAsync(int membershipId);

    /// <summary>Tworzy nowe okresy, oznacza zaległości i wysyła przypomnienia. Zwraca liczbę zmian.</summary>
    Task<int> ProcessBillingAsync(CancellationToken ct = default);
}
