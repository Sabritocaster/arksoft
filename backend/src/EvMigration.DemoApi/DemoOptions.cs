namespace EvMigration.DemoApi;

public sealed class DemoOptions
{
    public string DataRoot { get; init; } = "../../demo-data";

    public string MappingPath { get; init; } = "../../samples/target-archives.json";

    public string StorionXUrl { get; init; } = "http://127.0.0.1:5099";
}
