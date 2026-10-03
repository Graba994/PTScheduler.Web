using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>Moje konto: profil, zdjęcie profilowe, stan bezpieczeństwa i link do kalendarza.</summary>
public interface IAccountService
{
    Task<AccountOverviewDto?> GetOverviewAsync(string userId);

    /// <summary>Zapisuje dane osobowe (u klienta także w jego karcie klienta).</summary>
    /// <exception cref="ArgumentException">Niepoprawne dane — komunikat dla użytkownika.</exception>
    Task SaveProfileAsync(string userId, SaveAccountProfileDto dto);

    /// <summary>Zapisuje zdjęcie profilowe (kwadrat 512 px, WebP) i zwraca jego adres.</summary>
    /// <exception cref="ArgumentException">Plik nie jest zdjęciem albo jest za duży.</exception>
    Task<string> SetAvatarAsync(string userId, byte[] image);
    Task RemoveAvatarAsync(string userId);

    /// <summary>Adres zdjęcia profilowego albo null (lekkie zapytanie — do menu i awatarów).</summary>
    Task<string?> GetAvatarUrlAsync(string userId);

    /// <summary>Ścieżka pliku zdjęcia na dysku albo null.</summary>
    string? AvatarFilePath(string userId);

    /// <summary>Token linku „Moje wizyty w kalendarzu” (tworzy przy pierwszym użyciu).</summary>
    Task<string> GetOrCreateCalendarTokenAsync(string userId);
    Task<string> ResetCalendarTokenAsync(string userId);
    Task<string?> FindUserByCalendarTokenAsync(string token);
}
