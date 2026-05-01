using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web;

internal sealed class WebRootPathProvider(IWebHostEnvironment env) : IWebRootPathProvider
{
    public string WebRootPath => env.WebRootPath;
}
