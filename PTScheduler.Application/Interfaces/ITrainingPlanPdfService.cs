namespace PTScheduler.Application.Interfaces;

public interface ITrainingPlanPdfService
{
    /// <summary>
    /// Plan treningowy do druku: logo i kolory studia, dni z ćwiczeniami (serie, powtórzenia, ciężar,
    /// przerwa, tempo, uwagi) i puste kolumny do zapisywania wyników przez cztery tygodnie.
    /// Null, gdy planu nie ma.
    /// </summary>
    Task<(byte[] Bytes, string FileName)?> GenerateAsync(int planId);
}
