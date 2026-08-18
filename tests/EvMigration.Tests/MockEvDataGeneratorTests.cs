using System.Security.Cryptography;
using System.Text.Json;
using EvMigration.Core.Mock;
using EvMigration.Core.Models;
using EvMigration.Core.Serialization;

namespace EvMigration.Tests;

public sealed class MockEvDataGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_CreatesRealisticDataWithValidPartHashes()
    {
        var outputPath = CreateTemporaryDirectory();

        try
        {
            var result = await new MockEvDataGenerator().GenerateAsync(outputPath);
            var dataSet = await ReadDataSetAsync(result.CatalogPath);

            Assert.Equal(5, dataSet.Archives.Count);
            Assert.Equal(7, dataSet.Items.Count);
            Assert.Contains(dataSet.Archives, archive => archive.LegalHold);
            Assert.Contains(
                dataSet.Archives,
                archive => archive.OwnerUpn == "former.employee@contoso.com");

            var fsaItem = Assert.Single(
                dataSet.Items,
                item => item.ArchiveId == "F1");
            Assert.Equal(@"\\fileserver\finance\Contracts\signed-contract.pdf", fsaItem.FilePath);
            Assert.NotNull(fsaItem.FileModifiedAt);

            var sharedPart = dataSet.Items
                .SelectMany(item => item.ContentParts)
                .GroupBy(partId => partId)
                .FirstOrDefault(group => group.Count() > 1);

            Assert.NotNull(sharedPart);

            foreach (var part in dataSet.SisParts)
            {
                var blobPath = Path.Combine(outputPath, part.DataRef);
                var data = await File.ReadAllBytesAsync(blobPath);
                var actualHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

                Assert.Equal(part.SizeBytes, data.LongLength);
                Assert.Equal(part.Sha256, actualHash);
            }
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_ProducesTheSameDataOnEveryRun()
    {
        var firstOutputPath = CreateTemporaryDirectory();
        var secondOutputPath = CreateTemporaryDirectory();

        try
        {
            var generator = new MockEvDataGenerator();
            var first = await generator.GenerateAsync(firstOutputPath);
            var second = await generator.GenerateAsync(secondOutputPath);

            Assert.Equal(
                await File.ReadAllTextAsync(first.CatalogPath),
                await File.ReadAllTextAsync(second.CatalogPath));

            var firstBlobs = Directory.GetFiles(Path.Combine(firstOutputPath, "blobs"))
                .OrderBy(Path.GetFileName)
                .ToArray();
            var secondBlobs = Directory.GetFiles(Path.Combine(secondOutputPath, "blobs"))
                .OrderBy(Path.GetFileName)
                .ToArray();

            Assert.Equal(
                firstBlobs.Select(Path.GetFileName),
                secondBlobs.Select(Path.GetFileName));

            for (var index = 0; index < firstBlobs.Length; index++)
            {
                Assert.Equal(
                    await File.ReadAllBytesAsync(firstBlobs[index]),
                    await File.ReadAllBytesAsync(secondBlobs[index]));
            }
        }
        finally
        {
            Directory.Delete(firstOutputPath, recursive: true);
            Directory.Delete(secondOutputPath, recursive: true);
        }
    }

    private static async Task<EvDataSet> ReadDataSetAsync(string catalogPath)
    {
        await using var stream = File.OpenRead(catalogPath);
        return await JsonSerializer.DeserializeAsync<EvDataSet>(stream, EvJson.Options)
            ?? throw new InvalidDataException("Generated EV catalog could not be read.");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ev-migration-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
