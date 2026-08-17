using System.Security.Cryptography;
using EvMigration.Core.Discovery;
using EvMigration.Core.Mock;
using EvMigration.Core.Models;
using EvMigration.Core.Rehydration;

namespace EvMigration.Tests;

public sealed class SisRehydratorTests
{
    [Fact]
    public async Task RehydrateAsync_CombinesPartsInOrderAndComputesContentHash()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var item = fixture.DataSet.Items.Single(candidate => candidate.ItemId == "I100");
            var rehydrator = new SisRehydrator(fixture.OutputPath, fixture.DataSet.SisParts);

            var result = await rehydrator.RehydrateAsync(item);
            var expectedContent = await ReadExpectedContentAsync(
                fixture.OutputPath,
                fixture.DataSet,
                item);

            Assert.Equal(expectedContent, result.Content);
            Assert.Equal(
                ComputeSha256(expectedContent),
                result.ContentSha256);
            Assert.Equal(item.ContentParts, result.Parts.Select(part => part.PartId));
        }
        finally
        {
            Directory.Delete(fixture.OutputPath, recursive: true);
        }
    }

    [Fact]
    public async Task RehydrateAsync_ReadsSharedPartsOnlyOnce()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var firstItem = fixture.DataSet.Items.Single(candidate => candidate.ItemId == "I100");
            var secondItem = fixture.DataSet.Items.Single(candidate => candidate.ItemId == "I101");
            var rehydrator = new SisRehydrator(fixture.OutputPath, fixture.DataSet.SisParts);

            await rehydrator.RehydrateAsync(firstItem);
            await rehydrator.RehydrateAsync(secondItem);

            Assert.Equal(4, rehydrator.PhysicalReadCount);
            Assert.Equal(4, rehydrator.CachedPartCount);
        }
        finally
        {
            Directory.Delete(fixture.OutputPath, recursive: true);
        }
    }

    [Fact]
    public async Task RehydrateAsync_RejectsACorruptedPart()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var item = fixture.DataSet.Items.Single(candidate => candidate.ItemId == "I100");
            var part = fixture.DataSet.SisParts.Single(candidate => candidate.PartId == "P7");
            await File.WriteAllBytesAsync(Path.Combine(fixture.OutputPath, part.DataRef), [0, 1, 2]);
            var rehydrator = new SisRehydrator(fixture.OutputPath, fixture.DataSet.SisParts);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => rehydrator.RehydrateAsync(item));

            Assert.Contains("P7", exception.Message);
        }
        finally
        {
            Directory.Delete(fixture.OutputPath, recursive: true);
        }
    }

    private static async Task<RehydrationFixture> CreateFixtureAsync()
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"ev-rehydration-tests-{Guid.NewGuid():N}");
        var generated = await new MockEvDataGenerator().GenerateAsync(outputPath);
        var dataSet = await new EvDataSetLoader().LoadAsync(generated.CatalogPath);
        return new RehydrationFixture(outputPath, dataSet);
    }

    private static async Task<byte[]> ReadExpectedContentAsync(
        string outputPath,
        EvDataSet dataSet,
        EvItem item)
    {
        var parts = dataSet.SisParts.ToDictionary(part => part.PartId, StringComparer.Ordinal);
        using var content = new MemoryStream();

        foreach (var partId in item.ContentParts)
        {
            var data = await File.ReadAllBytesAsync(Path.Combine(outputPath, parts[partId].DataRef));
            content.Write(data);
        }

        return content.ToArray();
    }

    private static string ComputeSha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed record RehydrationFixture(string OutputPath, EvDataSet DataSet);
}
