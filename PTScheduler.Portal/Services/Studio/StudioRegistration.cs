namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Rejestracja usług Studia reklam w jednym miejscu — używa jej Program.cs i test StudioRegistrationTests,
/// który buduje wszystkie usługi razem (łapie m.in. zależności cykliczne, których nie widać w testach pojedynczych klas).
/// </summary>
public static class StudioRegistration
{
    public static IServiceCollection AddStudioReklam(this IServiceCollection services, bool backgroundServices = true)
    {
        // Teksty reklam i posty (etap 2)
        services.AddScoped<StudioService>();

        // Filmy: głos i dźwięk, renderer (kontener hantly-renderer), zrzuty, scenarzysta, kolejka zadań
        services.AddScoped<StudioMedia>();
        services.AddHttpClient(RendererClient.HttpClientName);
        services.AddScoped<RendererClient>();
        services.AddScoped<StudioNotifier>();
        services.AddScoped<StudioJobs>();
        services.AddScoped<StudioScreens>();
        services.AddScoped<AdWriter>();
        services.AddScoped<StudioVideos>();

        services.AddScoped<IStudioJobHandler, SoundsJobHandler>();
        services.AddScoped<IStudioJobHandler, ScreensJobHandler>();
        services.AddScoped<IStudioJobHandler, WriteVideoJobHandler>();
        services.AddScoped<IStudioJobHandler, VoiceJobHandler>();
        services.AddScoped<IStudioJobHandler, RenderJobHandler>();
        services.AddScoped<IStudioJobHandler, QaJobHandler>();

        if (backgroundServices)
        {
            services.AddHostedService<StudioBackgroundService>();
            services.AddHostedService<StudioJobsBackgroundService>();
        }
        return services;
    }
}
