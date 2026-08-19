namespace StorionX.MockApi;

public sealed class MockRateLimiter
{
    private static readonly TimeSpan RequestWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ByteWindow = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly int _requestsPerSecond;
    private readonly Queue<DateTimeOffset> _requests = new();
    private readonly Queue<ByteUsage> _byteUsage = new();
    private long _usedBytes;

    public MockRateLimiter(int requestsPerSecond, long maxBytesPerMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestsPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytesPerMinute);

        _requestsPerSecond = requestsPerSecond;
        MaxBytesPerMinute = maxBytesPerMinute;
    }

    public long MaxBytesPerMinute { get; }

    public bool TryAcquire(long contentBytes, DateTimeOffset now, out TimeSpan retryAfter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contentBytes);

        lock (_gate)
        {
            RemoveExpiredEntries(now);

            var requestWait = GetRequestWait(now);
            var byteWait = GetByteWait(contentBytes, now);
            retryAfter = requestWait > byteWait ? requestWait : byteWait;

            if (retryAfter > TimeSpan.Zero)
            {
                return false;
            }

            _requests.Enqueue(now);
            _byteUsage.Enqueue(new ByteUsage(now, contentBytes));
            _usedBytes += contentBytes;
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _requests.Clear();
            _byteUsage.Clear();
            _usedBytes = 0;
        }
    }

    private void RemoveExpiredEntries(DateTimeOffset now)
    {
        while (_requests.TryPeek(out var requestTime) && now - requestTime >= RequestWindow)
        {
            _requests.Dequeue();
        }

        while (_byteUsage.TryPeek(out var usage) && now - usage.Timestamp >= ByteWindow)
        {
            _byteUsage.Dequeue();
            _usedBytes -= usage.Bytes;
        }
    }

    private TimeSpan GetRequestWait(DateTimeOffset now)
    {
        if (_requests.Count < _requestsPerSecond)
        {
            return TimeSpan.Zero;
        }

        return Positive(_requests.Peek() + RequestWindow - now);
    }

    private TimeSpan GetByteWait(long contentBytes, DateTimeOffset now)
    {
        var excessBytes = _usedBytes + contentBytes - MaxBytesPerMinute;
        if (excessBytes <= 0)
        {
            return TimeSpan.Zero;
        }

        long releasedBytes = 0;
        foreach (var usage in _byteUsage)
        {
            releasedBytes += usage.Bytes;
            if (releasedBytes >= excessBytes)
            {
                return Positive(usage.Timestamp + ByteWindow - now);
            }
        }

        return ByteWindow;
    }

    private static TimeSpan Positive(TimeSpan value) =>
        value > TimeSpan.Zero ? value : TimeSpan.FromMilliseconds(1);

    private sealed record ByteUsage(DateTimeOffset Timestamp, long Bytes);
}
