using System.Text.Json;
using EvMigration.Core.Mock;
using EvMigration.Core.Serialization;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintUsage();
    return 0;
}

if (!string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Unknown command: {args[0]}");
    PrintUsage();
    return 2;
}

var outputDirectory = "samples/generated";
for (var index = 1; index < args.Length; index++)
{
    if (args[index] == "--output" && index + 1 < args.Length)
    {
        outputDirectory = args[++index];
        continue;
    }

    Console.Error.WriteLine($"Unknown or incomplete option: {args[index]}");
    PrintUsage();
    return 2;
}

var generator = new MockEvDataGenerator();
var result = await generator.GenerateAsync(outputDirectory);
Console.WriteLine(JsonSerializer.Serialize(result, EvJson.Options));
return 0;

static void PrintUsage()
{
    Console.WriteLine("Enterprise Vault migration CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  generate [--output <directory>]");
}
