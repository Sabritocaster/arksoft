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
    string Error);

public sealed record MigrationReport
{
    public required int WorkerCount { get; init; }

    public required int ScannedItemCount { get; init; }

    public required int PendingMappingItemCount { get; init; }

    public required int AttemptedItemCount { get; init; }

    public required int UploadedItemCount { get; init; }

    public required int ExistingItemCount { get; init; }

    public required int FailedItemCount { get; init; }

    public required int RetryCount { get; init; }

    public required long MigratedBytes { get; init; }

    public required int PhysicalSisReads { get; init; }

    public required int CachedSisParts { get; init; }

    public required IReadOnlyList<MigrationFailure> Failures { get; init; }
}
