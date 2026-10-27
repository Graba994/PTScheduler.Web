using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISetupService
{
    Task<bool> IsSetupCompletedAsync();
    Task CompleteSetupAsync(string mode, string companyName, string adminEmail, string adminPassword);

    /// <summary>Dane z kreatora rejestracji w Portalu — konto właściciela, kolor, strona, oferta.</summary>
    Task<SetupBootstrapResult> BootstrapAsync(SetupBootstrapDto dto);

    /// <summary>Dane z rejestracji do wypełnienia /setup (null = brak).</summary>
    Task<SetupPrefillDto?> GetPrefillAsync();

    /// <summary>Jednorazowe wejście po publikacji: zwraca Id konta właściciela albo null (zły / zużyty / przeterminowany token).</summary>
    Task<string?> RedeemWelcomeTokenAsync(string? token);

    /// <summary>Profil trenera założony z kreatora (null = brak).</summary>
    Task<string?> GetTrainerProfileIdAsync();

    /// <summary>
    /// „Pierwsze ustawienia”: właściciel sam prowadzi treningi — jego konto dostaje rolę trenera (bez drugiego
    /// loginu). Tylko gdy w aplikacji nie ma jeszcze żadnego trenera. Zwraca Id profilu trenera albo null.
    /// </summary>
    Task<string?> MakeOwnerTrainerAsync(string ownerUserId);
}
