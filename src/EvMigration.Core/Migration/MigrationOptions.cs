namespace EvMigration.Core.Migration;

public sealed record MigrationOptions
{
    public int WorkerCount { get; init; } = 4;

    public bool DryRun { get; init; }

    public MigrationFilter Filter { get; init; } = new();
}
