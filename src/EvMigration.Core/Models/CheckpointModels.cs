namespace EvMigration.Core.Models;

public sealed record MigrationCheckpoint
{
    public int Version { get; init; } = 1;

    public required IReadOnlyList<CheckpointItem> CompletedItems { get; init; }
}

public sealed record CheckpointItem
{
    public required string SourceItemId { get; init; }

    public required string TargetArchiveId { get; init; }

    public required string ContentSha256 { get; init; }

    public required IngestOutcome Outcome { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
