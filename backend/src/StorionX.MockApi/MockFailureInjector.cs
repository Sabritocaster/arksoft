namespace StorionX.MockApi;

public sealed class MockFailureInjector
{
    private readonly object _gate = new();
    private Random _random;
    private readonly double _failureRate;
    private readonly int _randomSeed;

    public MockFailureInjector(double failureRate, int randomSeed)
    {
        if (failureRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failureRate));
        }

        _failureRate = failureRate;
        _randomSeed = randomSeed;
        _random = new Random(randomSeed);
    }

    public bool ShouldFail()
    {
        lock (_gate)
        {
            return _random.NextDouble() < _failureRate;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _random = new Random(_randomSeed);
        }
    }
}
