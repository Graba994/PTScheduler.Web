using Microsoft.AspNetCore.Identity;
using Moq;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Tests.Helpers;

/// <summary>
/// Bare-bones <see cref="UserManager{ApplicationUser}"/> mock — no working store,
/// only methods we explicitly Setup will return non-default values. Useful for
/// services that just call <see cref="UserManager{T}.FindByIdAsync(string)"/> on
/// well-known IDs.
/// </summary>
internal static class MockUserManagerHelper
{
    public static Mock<UserManager<ApplicationUser>> Create()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
