using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;

namespace PTScheduler.Portal.Components.Pages.Panel.Instagram;

/// <summary>Drobne formaty wspólne dla zakładek Instagrama.</summary>
public static class IgUi
{
    public static string When(DateTime? utc)
    {
        if (utc is null) return "bez terminu";
        var l = InstagramRules.ToWarsaw(utc.Value);
        var today = InstagramRules.ToWarsaw(DateTime.UtcNow).Date;
        var day = l.Date == today ? "dziś" : l.Date == today.AddDays(1) ? "jutro" : $"{InstagramRules.DayShort(InstagramRules.IsoDay(l))} {l:dd.MM}";
        return $"{day}, {l:HH:mm}";
    }

    public static string? Thumb(InstagramPost p) =>
        p.Assets.OrderBy(a => a.Order).FirstOrDefault(a => a.Kind == "image") is { } a ? $"/media/ig/{a.Token}.jpg" : null;

    public static string Source(string source) => source switch { "autopilot" => "Autopilot", "ai" => "AI", _ => "Ręcznie" };
}
