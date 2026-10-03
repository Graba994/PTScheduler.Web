using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace PTScheduler.Web;

/// <summary>
/// Shared render modes. Interactive Server with prerender disabled so pages
/// render once (interactive) instead of prerender + reconnect, which replayed
/// CSS entrance animations twice on every navigation.
/// </summary>
public static class RenderModes
{
    public static readonly IComponentRenderMode Server = new InteractiveServerRenderMode(prerender: false);
}
