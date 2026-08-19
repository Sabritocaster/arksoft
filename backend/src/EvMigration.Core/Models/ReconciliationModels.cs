namespace EvMigration.Core.Models;

public sealed record StorionXStateSnapshot
{
    public required int ItemCount { get; init; }

    public required int UniquePartCount { get; init; }

    public required long LogicalBytes { get; init; }

    public required long PhysicalBytes { get; init; }

    public required IReadOnlyList<StorionXStoredItemSnapshot> Items { get; init; }
}

public sealed record StorionXStoredItemSnapshot
{
    public required string SourceItemId { get; init; }

    public required string TargetArchiveId { get; init; }

    public required string MessageSha256 { get; init; }

    public required long LogicalBytes { get; init; }
}

public sealed record ReconciliationMismatch(
    string SourceItemId,
    IReadOnlyList<string> Fields);

public sealed record ReconciliationReport
{
    public required string RunId { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }

    public required bool IsReconciled { get; init; }

    public required int ExpectedItemCount { get; init; }

    public required int TargetItemCount { get; init; }

    public required int MatchedItemCount { get; init; }

    public required long SourceLogicalBytes { get; init; }

    public required long TargetLogicalBytes { get; init; }

    public required IReadOnlyList<string> MissingItemIds { get; init; }

    public required IReadOnlyList<string> UnexpectedItemIds { get; init; }

    public required IReadOnlyList<ReconciliationMismatch> Mismatches { get; init; }
}
