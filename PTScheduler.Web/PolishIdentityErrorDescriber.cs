using Microsoft.AspNetCore.Identity;

namespace PTScheduler.Web;

public class PolishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError()                          => E(nameof(DefaultError),            "Wystąpił nieznany błąd.");
    public override IdentityError ConcurrencyFailure()                   => E(nameof(ConcurrencyFailure),       "Błąd współbieżności. Spróbuj ponownie.");
    public override IdentityError PasswordMismatch()                     => E(nameof(PasswordMismatch),         "Nieprawidłowe hasło.");
    public override IdentityError InvalidToken()                         => E(nameof(InvalidToken),             "Nieprawidłowy token.");
    public override IdentityError RecoveryCodeRedemptionFailed()         => E(nameof(RecoveryCodeRedemptionFailed), "Kod odzyskiwania jest nieprawidłowy lub już użyty.");
    public override IdentityError LoginAlreadyAssociated()               => E(nameof(LoginAlreadyAssociated),   "Użytkownik z tym loginem już istnieje.");
    public override IdentityError InvalidUserName(string? userName)      => E(nameof(InvalidUserName),          $"Nazwa użytkownika '{userName}' jest nieprawidłowa.");
    public override IdentityError InvalidEmail(string? email)            => E(nameof(InvalidEmail),             $"Adres email '{email}' jest nieprawidłowy.");
    public override IdentityError DuplicateUserName(string userName)     => E(nameof(DuplicateUserName),        $"Nazwa użytkownika '{userName}' jest już zajęta.");
    public override IdentityError DuplicateEmail(string email)           => E(nameof(DuplicateEmail),           $"Email '{email}' jest już zarejestrowany.");
    public override IdentityError InvalidRoleName(string? role)          => E(nameof(InvalidRoleName),          $"Nazwa roli '{role}' jest nieprawidłowa.");
    public override IdentityError DuplicateRoleName(string role)         => E(nameof(DuplicateRoleName),        $"Rola '{role}' już istnieje.");
    public override IdentityError UserAlreadyHasPassword()               => E(nameof(UserAlreadyHasPassword),   "Użytkownik ma już ustawione hasło.");
    public override IdentityError UserLockoutNotEnabled()                => E(nameof(UserLockoutNotEnabled),    "Blokada konta nie jest włączona.");
    public override IdentityError UserAlreadyInRole(string role)         => E(nameof(UserAlreadyInRole),        $"Użytkownik jest już w roli '{role}'.");
    public override IdentityError UserNotInRole(string role)             => E(nameof(UserNotInRole),            $"Użytkownik nie jest w roli '{role}'.");
    public override IdentityError PasswordTooShort(int length)           => E(nameof(PasswordTooShort),         $"Hasło musi mieć co najmniej {length} znaków.");
    public override IdentityError PasswordRequiresNonAlphanumeric()      => E(nameof(PasswordRequiresNonAlphanumeric), "Hasło musi zawierać co najmniej jeden znak specjalny (np. !, @, #, $).");
    public override IdentityError PasswordRequiresDigit()                => E(nameof(PasswordRequiresDigit),    "Hasło musi zawierać co najmniej jedną cyfrę (0–9).");
    public override IdentityError PasswordRequiresLower()                => E(nameof(PasswordRequiresLower),    "Hasło musi zawierać co najmniej jedną małą literę (a–z).");
    public override IdentityError PasswordRequiresUpper()                => E(nameof(PasswordRequiresUpper),    "Hasło musi zawierać co najmniej jedną wielką literę (A–Z).");
    public override IdentityError PasswordRequiresUniqueChars(int n)     => E(nameof(PasswordRequiresUniqueChars), $"Hasło musi zawierać co najmniej {n} unikalnych znaków.");

    private static IdentityError E(string code, string desc) => new() { Code = code, Description = desc };
}
