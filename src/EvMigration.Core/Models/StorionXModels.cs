namespace EvMigration.Core.Models;

public sealed record StorionXIngestRequest
{
    public required string TargetArchiveId { get; init; }

    public required string SourceItemId { get; init; }

    public required string MessageSha256 { get; init; }

    public required IReadOnlyList<StorionXContentPart> Parts { get; init; }

    public required StorionXMetadata Metadata { get; init; }

    public required StorionXRetention Retention { get; init; }
}

public sealed record StorionXContentPart
{
    public required string Sha256 { get; init; }

    public required long SizeBytes { get; init; }

    public required string DataBase64 { get; init; }
}

public sealed record StorionXMetadata
{
    public required string SourceArchiveId { get; init; }

    public required string SourceItemId { get; init; }

    public required string FolderPath { get; init; }

    public required string Subject { get; init; }

    public required DateTimeOffset SentDate { get; init; }

    public required string From { get; init; }

    public required IReadOnlyList<string> To { get; init; }
}

public sealed record StorionXRetention
{
    public required string Category { get; init; }

    public required bool LegalHold { get; init; }
}
