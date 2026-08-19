using EvMigration.Core.Models;

namespace EvMigration.Core.Persistence;

public interface IMigrationCheckpointStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    bool IsCompleted(string sourceItemId, string targetArchiveId);

    Task MarkCompletedAsync(
        CheckpointItem item,
        CancellationToken cancellationToken = default);
}
