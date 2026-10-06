using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Dwukierunkowa synchronizacja z Google Calendar: wizyty trafiają do kalendarza
/// trenera, a zajęte terminy z kalendarza blokują grafik. Logowanie do Google
/// idzie przez Portal platformy, więc trener niczego nie konfiguruje.
/// </summary>
public interface IGoogleCalendarService
{
    Task<CalendarConnectionDto> GetStatusAsync(string userId);

    /// <summary>Adres zgody Google (przez Portal). Po zgodzie przeglądarka wraca na <paramref name="returnUrl"/>?google=…</summary>
    Task<(string? Url, string? Error)> StartConnectAsync(string userId, string returnUrl);

    /// <summary>Potwierdza połączenie po powrocie z Google i zapisuje je w aplikacji.</summary>
    Task<(bool Ok, string? Error)> CompleteConnectAsync(string userId);

    /// <summary>Instalacja samodzielna: synchronizacja przez konto z ustawień Google Meet.</summary>
    Task<(bool Ok, string? Error)> EnableOwnAccountAsync(string userId);

    Task DisconnectAsync(string userId);

    Task SaveOptionsAsync(string userId, bool pushSessions, bool blockBusy, bool showClientName, bool createMeetLinks);

    Task<CalendarSyncResult> SyncAsync(string userId, CancellationToken ct = default);

    /// <summary>Synchronizuje wszystkie połączenia, którym minął interwał (wołane przez usługę w tle).</summary>
    Task SyncDueAsync(TimeSpan interval, CancellationToken ct = default);

    /// <summary>Zajęte przedziały z Google w podanym oknie (zegar ścienny) — do podglądu w kalendarzu.</summary>
    Task<List<BusyBlockDto>> GetBusyBlocksAsync(string userId, DateTime from, DateTime to);
}
