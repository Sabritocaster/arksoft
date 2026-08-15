namespace StorionX.MockApi;

public sealed class MockStorionXOptions
{
    public int RequestsPerSecond { get; init; } = 4;

    public int MegabytesPerMinute { get; init; } = 1;

    public double TransientFailureRate { get; init; } = 0.05;

    public int RandomSeed { get; init; } = 17;
}
