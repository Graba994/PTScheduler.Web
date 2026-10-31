namespace PTScheduler.Web.Services;

/// <summary>Wybór na mapie mięśni na czas sesji (sylwetka, widok ogólny/szczegółowy) — wspólny dla katalogu i formularza.</summary>
public class MuscleMapPrefs
{
    public bool Female { get; set; }
    /// <summary>Null — domyślnie szczegółowo, gdy plan na to pozwala.</summary>
    public bool? Detailed { get; set; }
}
