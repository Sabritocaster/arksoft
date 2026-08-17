using System.Net;
using System.Text;
using System.Text.Json;
using EvMigration.Core.Models;
using EvMigration.Core.Serialization;

namespace EvMigration.Core.Ingestion;

public sealed class StorionXHttpClient : IStorionXClient
{
    private readonly HttpClient _httpClient;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialRetryDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<double> _jitter;

    public StorionXHttpClient(
        HttpClient httpClient,
        int maxRetries = 4,
        TimeSpan? initialRetryDelay = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<double>? jitter = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);

        _httpClient = httpClient;
        _maxRetries = maxRetries;
        _initialRetryDelay = initialRetryDelay ?? TimeSpan.FromMilliseconds(200);
        _delay = delay ?? Task.Delay;
        _jitter = jitter ?? Random.Shared.NextDouble;
    }

    public async Task<StorionXIngestResult> IngestAsync(
        StorionXIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = JsonSerializer.Serialize(request, EvJson.Options);
        var maxAttempts = _maxRetries + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "ingest");
                httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", request.SourceItemId);
                httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Created)
                {
                    return new StorionXIngestResult(IngestOutcome.Created, attempt, (int)response.StatusCode);
                }

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return new StorionXIngestResult(IngestOutcome.Existing, attempt, (int)response.StatusCode);
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsTransient(response.StatusCode) && attempt < maxAttempts)
                {
                    var retryDelay = GetRetryDelay(response, attempt);
                    await _delay(retryDelay, cancellationToken);
                    continue;
                }

                return new StorionXIngestResult(
                    IngestOutcome.Failed,
                    attempt,
                    (int)response.StatusCode,
                    Shorten(responseBody));
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await _delay(GetExponentialDelay(attempt), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await _delay(GetExponentialDelay(attempt), cancellationToken);
            }
        }

        return new StorionXIngestResult(
            IngestOutcome.Failed,
            maxAttempts,
            Error: "storionX could not be reached after all retry attempts.");
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            return date > DateTimeOffset.UtcNow
                ? date - DateTimeOffset.UtcNow
                : TimeSpan.Zero;
        }

        return GetExponentialDelay(attempt);
    }

    private TimeSpan GetExponentialDelay(int attempt)
    {
        var baseMilliseconds = _initialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitterMilliseconds = baseMilliseconds * 0.2 * _jitter();
        return TimeSpan.FromMilliseconds(Math.Min(baseMilliseconds + jitterMilliseconds, 5_000));
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static string Shorten(string value) =>
        value.Length <= 500 ? value : $"{value[..500]}...";
}
