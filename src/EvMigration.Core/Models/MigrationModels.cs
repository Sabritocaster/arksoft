namespace EvMigration.Core.Models;

public enum IngestOutcome
{
    Created,
    Existing,
    Failed
}

public sealed record StorionXIngestResult(
    IngestOutcome Outcome,
    int Attempts,
    int? HttpStatusCode = null,
    string? Error = null);

public sealed record MigrationFailure(
    string ItemId,
    string Category,
    string Error,
    int? HttpStatusCode = null);

public sealed record MigrationReport
{
    public required string RunId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }

    public required int WorkerCount { get; init; }

    public required bool DryRun { get; init; }

    public required int ScannedItemCount { get; init; }

    public required int FilteredOutItemCount { get; init; }

    public required int EligibleItemCount { get; init; }

    public required int PendingMappingItemCount { get; init; }

    public required int CheckpointSkippedItemCount { get; init; }

    public required int AttemptedItemCount { get; init; }

    public required int UploadedItemCount { get; init; }

    public required int ExistingItemCount { get; init; }

    public required int FailedItemCount { get; init; }

    public required int RetryCount { get; init; }

    public required long MigratedBytes { get; init; }

    public required long PlannedBytes { get; init; }

    public required int PhysicalSisReads { get; init; }

    public required int CachedSisParts { get; init; }

    public required IReadOnlyDictionary<string, int> ErrorBreakdown { get; init; }

    public required IReadOnlyList<MigrationFailure> Failures { get; init; }
}
