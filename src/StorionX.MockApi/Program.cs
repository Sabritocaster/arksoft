var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "healthy",
    Service = "StorionX.MockApi"
}));

app.Run();
