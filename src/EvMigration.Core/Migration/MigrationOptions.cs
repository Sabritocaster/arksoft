namespace EvMigration.Core.Migration;

public sealed record MigrationOptions
{
    public int WorkerCount { get; init; } = 4;
}
