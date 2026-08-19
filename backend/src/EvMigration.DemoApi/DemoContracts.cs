using EvMigration.Core.Models;

namespace EvMigration.DemoApi;

public sealed record DemoMigrationRequest
{
    public int Workers { get; init; } = 4;

    public bool UseCheckpoint { get; init; } = true;

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public string? ArchiveId { get; init; }

    public string? Folder { get; init; }
}

public sealed record DemoStatusResponse(
    bool Busy,
    MigrationReport? LastMigration,
    ReconciliationReport? LastReconciliation);

public sealed record DemoResetResponse(string Status);

public sealed class DemoBusyException : InvalidOperationException
{
    public DemoBusyException()
        : base("Another demo operation is already running.")
    {
    }
}
