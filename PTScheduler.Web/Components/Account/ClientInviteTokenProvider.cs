using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace PTScheduler.Web.Components.Account;

/// <summary>
/// Tokeny zaproszeń klientów — osobny dostawca, bo zaproszenie ma żyć dłużej (7 dni)
/// niż zwykły reset hasła (1 dzień), a oba nie powinny dzielić ustawień.
/// </summary>
public sealed class ClientInviteTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<ClientInviteTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;

public sealed class ClientInviteTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public ClientInviteTokenProviderOptions()
    {
        Name = "ClientInviteDataProtectorTokenProvider";
        TokenLifespan = TimeSpan.FromDays(PTScheduler.Application.Interfaces.ClientInvite.ValidDays);
    }
}
