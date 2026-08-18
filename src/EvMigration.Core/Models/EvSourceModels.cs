namespace EvMigration.Core.Models;

public enum EvArchiveType
{
    Mailbox,
    Journal,
    Fsa
}

public sealed record EvArchive
{
    public required string ArchiveId { get; init; }

    public required EvArchiveType Type { get; init; }

    public string? OwnerUpn { get; init; }

    public required bool LegalHold { get; init; }
}

public sealed record EvItem
{
    public required string ItemId { get; init; }

    public required string ArchiveId { get; init; }

    public required string FolderPath { get; init; }

    public required string Subject { get; init; }

    public required DateTimeOffset SentDate { get; init; }

    public required string From { get; init; }

    public required IReadOnlyList<string> To { get; init; }

    public string? FilePath { get; init; }

    public DateTimeOffset? FileModifiedAt { get; init; }

    public required IReadOnlyList<string> ContentParts { get; init; }

    public required string RetentionCategory { get; init; }

    public required long SizeBytes { get; init; }
}

public sealed record EvSisPart
{
    public required string PartId { get; init; }

    public required string Sha256 { get; init; }

    public required long SizeBytes { get; init; }

    public required string DataRef { get; init; }
}

public sealed record EvDataSet
{
    public required IReadOnlyList<EvArchive> Archives { get; init; }

    public required IReadOnlyList<EvItem> Items { get; init; }

    public required IReadOnlyList<EvSisPart> SisParts { get; init; }
}
