using System.Text.Json;
using System.Text.Json.Serialization;
using EvMigration.DemoApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("Demo"));
builder.Services.AddSingleton<DemoCoordinator>();
builder.Services.AddHttpClient("storionx", (serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<DemoOptions>>()
        .Value;
    client.BaseAddress = new Uri($"{options.StorionXUrl.TrimEnd('/')}/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

var allowedOrigins = builder.Configuration["Demo:AllowedOrigins"]?
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? ["http://localhost:3000"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

var coordinator = app.Services.GetRequiredService<DemoCoordinator>();
await coordinator.InitializeAsync();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "healthy",
    Service = "EvMigration.DemoApi"
}));

app.MapGet("/api/status", (DemoCoordinator demo) => Results.Ok(demo.GetStatus()));

app.MapGet("/api/discovery", async (
    DemoCoordinator demo,
    CancellationToken cancellationToken) =>
    Results.Ok(await demo.DiscoverAsync(cancellationToken)));

app.MapGet("/api/target-state", async (
    DemoCoordinator demo,
    CancellationToken cancellationToken) =>
    Results.Ok(await demo.GetTargetStateAsync(cancellationToken)));

app.MapPost("/api/migrate", async (
    DemoMigrationRequest request,
    DemoCoordinator demo,
    CancellationToken cancellationToken) =>
    await RunOperationAsync(() => demo.MigrateAsync(request, false, cancellationToken)));

app.MapPost("/api/dry-run", async (
    DemoMigrationRequest request,
    DemoCoordinator demo,
    CancellationToken cancellationToken) =>
    await RunOperationAsync(() => demo.MigrateAsync(request, true, cancellationToken)));

app.MapPost("/api/reconcile", async (
    DemoCoordinator demo,
    CancellationToken cancellationToken) =>
    await RunOperationAsync(() => demo.ReconcileAsync(cancellationToken)));

app.MapPost("/api/reset", async (
    DemoCoordinator demo,
    CancellationToken cancellationToken) =>
{
    try
    {
        await demo.ResetAsync(cancellationToken);
        return Results.Ok(new DemoResetResponse("reset"));
    }
    catch (DemoBusyException exception)
    {
        return Results.Conflict(new { exception.Message });
    }
});

app.Run();

static async Task<IResult> RunOperationAsync<T>(Func<Task<T>> operation)
{
    try
    {
        return Results.Ok(await operation());
    }
    catch (DemoBusyException exception)
    {
        return Results.Conflict(new { exception.Message });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { exception.Message });
    }
}

public partial class Program;
