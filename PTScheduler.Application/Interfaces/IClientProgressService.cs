using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IClientProgressService
{
    /// <summary>Postępy i wnioski dla jednego klienta (siła, ciało, frekwencja, odstępstwa od planu).</summary>
    Task<ClientProgressDto> GetAsync(int clientId);
}
