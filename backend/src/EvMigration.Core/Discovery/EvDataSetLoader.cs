using System.Text.Json;
using EvMigration.Core.Models;
using EvMigration.Core.Serialization;

namespace EvMigration.Core.Discovery;

public sealed class EvDataSetLoader
{
    public async Task<EvDataSet> LoadAsync(
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(catalogPath);
        var dataSet = await JsonSerializer.DeserializeAsync<EvDataSet>(
            stream,
            EvJson.Options,
            cancellationToken);

        if (dataSet is null)
        {
            throw new InvalidDataException("EV catalog is empty or invalid.");
        }

        Validate(dataSet);
        return dataSet;
    }

    private static void Validate(EvDataSet dataSet)
    {
        if (dataSet.Archives is null || dataSet.Items is null || dataSet.SisParts is null)
        {
            throw new InvalidDataException("EV catalog collections are required.");
        }

        EnsureUnique(dataSet.Archives.Select(archive => archive.ArchiveId), "archive");
        EnsureUnique(dataSet.Items.Select(item => item.ItemId), "item");
        EnsureUnique(dataSet.SisParts.Select(part => part.PartId), "SIS part");

        var archiveIds = dataSet.Archives
            .Select(archive => archive.ArchiveId)
            .ToHashSet(StringComparer.Ordinal);
        var parts = dataSet.SisParts
            .ToDictionary(part => part.PartId, StringComparer.Ordinal);

        foreach (var item in dataSet.Items)
        {
            if (!archiveIds.Contains(item.ArchiveId))
            {
                throw new InvalidDataException(
                    $"Item '{item.ItemId}' references unknown archive '{item.ArchiveId}'.");
            }

            long expectedSize = 0;
            foreach (var partId in item.ContentParts)
            {
                if (!parts.TryGetValue(partId, out var part))
                {
                    throw new InvalidDataException(
                        $"Item '{item.ItemId}' references unknown SIS part '{partId}'.");
                }

                expectedSize += part.SizeBytes;
            }

            if (item.SizeBytes != expectedSize)
            {
                throw new InvalidDataException(
                    $"Item '{item.ItemId}' size does not match its SIS parts.");
            }
        }
    }

    private static void EnsureUnique(IEnumerable<string> identifiers, string type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in identifiers)
        {
            if (string.IsNullOrWhiteSpace(identifier) || !seen.Add(identifier))
            {
                throw new InvalidDataException($"EV catalog contains an empty or duplicate {type} ID.");
            }
        }
    }
}
