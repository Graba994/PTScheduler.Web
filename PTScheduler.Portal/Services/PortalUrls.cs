namespace PTScheduler.Portal.Services;

/// <summary>
/// Publiczny adres Portalu do linków w e-mailach, SMS-ach i odpowiedziach dla aplikacji trenerów.
/// docker-compose przekazuje pusty <c>Portal__PublicUrl</c>, gdy zmienna nie jest ustawiona — wtedy hantly.pl,
/// a nie adres względny (taki link otwierał się w aplikacji trenera i kończył na „Tej strony nie ma”).
/// </summary>
public static class PortalUrls
{
    public const string Default = "https://hantly.pl";

    public static string Public(IConfiguration config) =>
        (config.GetValue<string>("Portal:PublicUrl") is { Length: > 0 } pub ? pub : Default).TrimEnd('/');
}
