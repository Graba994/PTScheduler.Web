using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>Dokumenty do akceptacji przez klientów (regulamin, zgody) i historia decyzji.</summary>
public interface IClientDocumentService
{
    Task<List<ClientDocumentDto>> GetAllAsync(bool includeInactive = false);
    Task<int> SaveAsync(SaveClientDocumentDto dto);
    Task ArchiveAsync(int id);
    /// <summary>Wszystkie aktywne dokumenty ze stanem dla klienta.</summary>
    Task<List<ClientDocumentStatusDto>> GetForClientAsync(int clientId);
    /// <summary>Czy klient ma coś do zaakceptowania (wymagane dokumenty i zgody bez decyzji w bieżącej wersji).</summary>
    Task<bool> HasPendingAsync(int clientId);
    /// <summary>Zapisuje decyzję; wymaganego dokumentu nie da się odrzucić.</summary>
    Task DecideAsync(int clientId, int documentId, bool accepted, string? ipAddress);
    /// <summary>Kto zaakceptował bieżącą wersję, kto odmówił, kto jeszcze nie zdecydował.</summary>
    Task<List<DocumentDecisionDto>> GetDecisionsAsync(int documentId);
}
