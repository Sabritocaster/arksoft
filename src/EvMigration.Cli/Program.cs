using System.Text.Json;
using EvMigration.Core.Discovery;
using EvMigration.Core.Ingestion;
using EvMigration.Core.Migration;
using EvMigration.Core.Mock;
using EvMigration.Core.Rehydration;
using EvMigration.Core.Serialization;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintUsage();
    return 0;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "generate" => await RunGenerateAsync(args[1..]),
        "discover" => await RunDiscoverAsync(args[1..]),
        "rehydrate" => await RunRehydrateAsync(args[1..]),
        "migrate" => await RunMigrateAsync(args[1..]),
        _ => UnknownCommand(args[0])
    };
}
catch (Exception exception) when (exception is ArgumentException
                                  or IOException
                                  or JsonException)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}

static async Task<int> RunGenerateAsync(string[] commandArguments)
{
    var options = ParseOptions(commandArguments, "--output");
    var outputDirectory = options.GetValueOrDefault("--output", "samples/generated");
    var result = await new MockEvDataGenerator().GenerateAsync(outputDirectory);

    Console.WriteLine(JsonSerializer.Serialize(result, EvJson.Options));
    return 0;
}

static async Task<int> RunDiscoverAsync(string[] commandArguments)
{
    var options = ParseOptions(commandArguments, "--source", "--mapping");
    var sourcePath = options.GetValueOrDefault("--source", "samples/generated/ev-data.json");
    var mappingPath = options.GetValueOrDefault("--mapping", "samples/target-archives.json");

    var dataSet = await new EvDataSetLoader().LoadAsync(sourcePath);
    var targetMap = await new TargetArchiveMapLoader().LoadAsync(mappingPath);
    var report = new ArchiveDiscoveryService().Discover(dataSet, targetMap);

    Console.WriteLine(JsonSerializer.Serialize(report, EvJson.Options));
    return 0;
}

static async Task<int> RunRehydrateAsync(string[] commandArguments)
{
    var options = ParseOptions(commandArguments, "--source", "--item");
    var sourcePath = options.GetValueOrDefault("--source", "samples/generated/ev-data.json");
    if (!options.TryGetValue("--item", out var itemId))
    {
        throw new ArgumentException("The --item option is required.");
    }

    var dataSet = await new EvDataSetLoader().LoadAsync(sourcePath);
    var item = dataSet.Items.SingleOrDefault(
        candidate => string.Equals(candidate.ItemId, itemId, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"Item '{itemId}' was not found.");
    var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(sourcePath))
        ?? throw new InvalidDataException("Source catalog directory could not be resolved.");
    var rehydrator = new SisRehydrator(sourceRoot, dataSet.SisParts);
    var result = await rehydrator.RehydrateAsync(item);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        result.ItemId,
        ContentBytes = result.Content.LongLength,
        result.ContentSha256,
        PartCount = result.Parts.Count,
        rehydrator.PhysicalReadCount,
        rehydrator.CachedPartCount
    }, EvJson.Options));
    return 0;
}

static async Task<int> RunMigrateAsync(string[] commandArguments)
{
    var options = ParseOptions(commandArguments, "--source", "--mapping", "--api");
    var sourcePath = options.GetValueOrDefault("--source", "samples/generated/ev-data.json");
    var mappingPath = options.GetValueOrDefault("--mapping", "samples/target-archives.json");
    var apiUrl = options.GetValueOrDefault("--api", "http://127.0.0.1:5099");

    var dataSet = await new EvDataSetLoader().LoadAsync(sourcePath);
    var targetMap = await new TargetArchiveMapLoader().LoadAsync(mappingPath);
    var discovery = new ArchiveDiscoveryService().Discover(dataSet, targetMap);
    var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(sourcePath))
        ?? throw new InvalidDataException("Source catalog directory could not be resolved.");

    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri($"{apiUrl.TrimEnd('/')}/"),
        Timeout = TimeSpan.FromSeconds(10)
    };
    var report = await new MigrationEngine().MigrateAsync(
        dataSet,
        discovery,
        sourceRoot,
        new StorionXHttpClient(httpClient));

    Console.WriteLine(JsonSerializer.Serialize(report, EvJson.Options));
    return report.FailedItemCount == 0 ? 0 : 1;
}

static Dictionary<string, string> ParseOptions(string[] arguments, params string[] allowedOptions)
{
    var allowed = allowedOptions.ToHashSet(StringComparer.Ordinal);
    var parsed = new Dictionary<string, string>(StringComparer.Ordinal);

    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length
            || !allowed.Contains(arguments[index])
            || !parsed.TryAdd(arguments[index], arguments[index + 1]))
        {
            throw new ArgumentException($"Unknown, duplicate or incomplete option: {arguments[index]}");
        }
    }

    return parsed;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("Enterprise Vault migration CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  generate [--output <directory>]");
    Console.WriteLine("  discover [--source <ev-data.json>] [--mapping <target-archives.json>]");
    Console.WriteLine("  rehydrate --item <item-id> [--source <ev-data.json>]");
    Console.WriteLine("  migrate [--source <ev-data.json>] [--mapping <target-archives.json>] [--api <url>]");
}
