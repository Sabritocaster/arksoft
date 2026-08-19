namespace EvMigration.Core.Models;

public sealed record TargetArchiveMap
{
    public required IReadOnlyDictionary<string, string> UserArchives { get; init; }

    public required string ComplianceArchiveId { get; init; }

    public IReadOnlyDictionary<string, string> FileArchives { get; init; } =
        new Dictionary<string, string>();
}

public enum ArchiveMappingStatus
{
    Mapped,
    PendingMapping
}

public sealed record ArchiveDiscoveryResult
{
    public required string SourceArchiveId { get; init; }

    public required EvArchiveType ArchiveType { get; init; }

    public string? OwnerUpn { get; init; }

    public string? TargetArchiveId { get; init; }

    public required ArchiveMappingStatus Status { get; init; }

    public string? Reason { get; init; }

    public required bool LegalHold { get; init; }

    public required int ItemCount { get; init; }

    public required long TotalBytes { get; init; }
}

public sealed record ArchiveDiscoveryReport
{
    public required int ArchiveCount { get; init; }

    public required int ItemCount { get; init; }

    public required int MappedArchiveCount { get; init; }

    public required int PendingArchiveCount { get; init; }

    public required int EligibleItemCount { get; init; }

    public required int PendingItemCount { get; init; }

    public required long EligibleBytes { get; init; }

    public required int LegalHoldArchiveCount { get; init; }

    public required IReadOnlyList<ArchiveDiscoveryResult> Archives { get; init; }
}
