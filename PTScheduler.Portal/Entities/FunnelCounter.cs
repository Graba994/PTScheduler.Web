namespace PTScheduler.Portal.Entities;

/// <summary>Dzienny licznik kroku lejka z kreatora (wejście, start, konto) — bez danych osobowych.</summary>
public class FunnelCounter
{
    public int Id { get; set; }
    public DateOnly Day { get; set; }
    public string Kind { get; set; } = "";
    public int Count { get; set; }
}

public static class FunnelKinds
{
    /// <summary>Kreator otwarty w przeglądarce (z JavaScriptem — boty się nie liczą).</summary>
    public const string View = "builder_view";
    /// <summary>Wpisana nazwa i „Dalej” z pierwszego kroku.</summary>
    public const string Start = "builder_start";
    /// <summary>Doszedł do ostatniego kroku (konto i plan).</summary>
    public const string Account = "builder_account";
}
