using System.Text.Json;
using EvMigration.Core.Models;
using EvMigration.Core.Serialization;

namespace EvMigration.Core.Discovery;

public sealed class TargetArchiveMapLoader
{
    public async Task<TargetArchiveMap> LoadAsync(
        string mappingPath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(mappingPath);
        return await JsonSerializer.DeserializeAsync<TargetArchiveMap>(
            stream,
            EvJson.Options,
            cancellationToken)
            ?? throw new InvalidDataException("Target archive mapping is empty or invalid.");
    }
}
