namespace PTScheduler.Application.Interfaces;

/// <summary>Import publicznego katalogu ćwiczeń z wger.de (API bez klucza).</summary>
public interface IWgerCatalogImporter
{
    /// <param name="progress">Postęp: (przetworzone, wszystkie) — wszystkie znane po pierwszej stronie.</param>
    Task<WgerImportResult> ImportAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default);
}

public sealed record WgerImportResult(int Added, int Updated, int Skipped, int Total);
