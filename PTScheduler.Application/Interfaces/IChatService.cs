using PTScheduler.Application.Chat;

namespace PTScheduler.Application.Interfaces;

public interface IChatService
{
    /// <summary>Czy użytkownik może czytać i pisać w rozmowie z tym klientem.</summary>
    Task<bool> CanAccessAsync(int clientId, string userId, bool isAdmin);

    /// <summary>Rozmowy trenera (admin: wszystkie), najnowsze na górze.</summary>
    Task<List<ChatConversationDto>> GetConversationsAsync(string? trainerUserId);

    Task<List<ChatMessageDto>> GetMessagesAsync(int clientId, int take = 100);

    /// <summary>Wysyła wiadomość i powiadamia drugą stronę (push + odświeżenie otwartych okien).</summary>
    Task<(bool Ok, string? Error)> SendAsync(int clientId, string senderUserId, bool fromStaff, string body);

    /// <summary>Oznacza jako przeczytane wiadomości drugiej strony.</summary>
    Task MarkReadAsync(int clientId, bool readerIsStaff);

    /// <summary>Liczba nieprzeczytanych: dla trenera ze wszystkich rozmów, dla klienta z jego rozmowy.</summary>
    Task<int> GetUnreadCountAsync(string userId, bool isStaff, bool isAdmin);
}

/// <summary>
/// Sygnał „w rozmowie z klientem X pojawiła się zmiana” dla otwartych okien czatu
/// (jedna instancja aplikacji na tenanta, więc wystarcza zdarzenie w pamięci).
/// </summary>
public interface IChatNotifier
{
    event Action<int>? ConversationChanged;
    void Notify(int clientId);
}
