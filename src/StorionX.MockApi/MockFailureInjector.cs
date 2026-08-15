namespace StorionX.MockApi;

public sealed class MockFailureInjector
{
    private readonly object _gate = new();
    private readonly Random _random;
    private readonly double _failureRate;

    public MockFailureInjector(double failureRate, int randomSeed)
    {
        if (failureRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failureRate));
        }

        _failureRate = failureRate;
        _random = new Random(randomSeed);
    }

    public bool ShouldFail()
    {
        lock (_gate)
        {
            return _random.NextDouble() < _failureRate;
        }
    }
}
