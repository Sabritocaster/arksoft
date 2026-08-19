using EvMigration.Core.Models;

namespace EvMigration.Core.Ingestion;

public interface IStorionXClient
{
    Task<StorionXIngestResult> IngestAsync(
        StorionXIngestRequest request,
        CancellationToken cancellationToken = default);
}
