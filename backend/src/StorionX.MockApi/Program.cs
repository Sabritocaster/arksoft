using System.Text.Json;
using System.Text.Json.Serialization;
using EvMigration.Core.Models;
using StorionX.MockApi;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration
    .GetSection("StorionXMock")
    .Get<MockStorionXOptions>() ?? new MockStorionXOptions();

builder.Services.AddSingleton<MockStorionXStore>();
builder.Services.AddSingleton(new MockRateLimiter(
    options.RequestsPerSecond,
    options.MegabytesPerMinute * 1024L * 1024L));
builder.Services.AddSingleton(new MockFailureInjector(
    options.TransientFailureRate,
    options.RandomSeed));
builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
{
    jsonOptions.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    jsonOptions.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "healthy",
    Service = "StorionX.MockApi"
}));

app.MapPost("/ingest", (
    HttpContext context,
    StorionXIngestRequest request,
    MockRateLimiter rateLimiter,
    MockFailureInjector failureInjector,
    MockStorionXStore store) =>
{
    var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
        return Results.Json(
            new { Error = "Idempotency-Key header is required." },
            statusCode: StatusCodes.Status400BadRequest);
    }

    if (request.Parts is null || request.Parts.Any(part => part.SizeBytes < 0))
    {
        return Results.Json(
            new { Error = "Content part sizes must be valid." },
            statusCode: StatusCodes.Status400BadRequest);
    }

    var contentBytes = request.Parts.Sum(part => part.SizeBytes);
    if (contentBytes > rateLimiter.MaxBytesPerMinute)
    {
        return Results.Json(
            new { Error = "Message exceeds the per-minute byte limit." },
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    if (!rateLimiter.TryAcquire(contentBytes, DateTimeOffset.UtcNow, out var retryAfter))
    {
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        return Results.Json(
            new { Error = "Rate limit exceeded." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    if (failureInjector.ShouldFail())
    {
        return Results.Json(
            new { Error = "Temporary storionX failure." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var result = store.Ingest(idempotencyKey, request);
    return result.Status switch
    {
        StoreStatus.Created => Results.Json(
            new { Status = "created", result.SourceItemId },
            statusCode: StatusCodes.Status201Created),
        StoreStatus.Existing => Results.Ok(new { Status = "already_exists", result.SourceItemId }),
        StoreStatus.Conflict => Results.Conflict(new { result.Error }),
        _ => Results.BadRequest(new { result.Error })
    };
});

app.MapGet("/state", (MockStorionXStore store) => Results.Ok(store.GetState()));

app.MapPost("/reset", (
    MockStorionXStore store,
    MockRateLimiter rateLimiter,
    MockFailureInjector failureInjector) =>
{
    store.Reset();
    rateLimiter.Reset();
    failureInjector.Reset();
    return Results.NoContent();
});

app.Run();

public partial class Program;
