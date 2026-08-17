using System.Collections.Concurrent;
using System.Text.Json;
using EvMigration.Core.Discovery;
using EvMigration.Core.Ingestion;
using EvMigration.Core.Migration;
using EvMigration.Core.Mock;
using EvMigration.Core.Models;
using EvMigration.Core.Persistence;
using EvMigration.Core.Reporting;
using EvMigration.Core.Serialization;

namespace EvMigration.Tests;

public sealed class CheckpointTests
{
    [Fact]
    public async Task JsonCheckpointStore_PersistsAndReloadsCompletedItems()
    {
        var outputPath = CreateTemporaryDirectory();
        var checkpointPath = Path.Combine(outputPath, "checkpoint.json");

        try
        {
            using (var store = new JsonCheckpointStore(checkpointPath))
            {
                await store.InitializeAsync();
                await store.MarkCompletedAsync(CreateCheckpointItem("ev:A1:I100"));
                Assert.True(store.IsCompleted("ev:A1:I100", "sx-mailbox-ayse"));
                Assert.False(store.IsCompleted("ev:A1:I100", "sx-mailbox-other"));
            }

            using var reloadedStore = new JsonCheckpointStore(checkpointPath);
            await reloadedStore.InitializeAsync();

            Assert.True(reloadedStore.IsCompleted("ev:A1:I100", "sx-mailbox-ayse"));
            Assert.Empty(Directory.GetFiles(outputPath, "*.tmp"));
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateAsync_ResumesWithoutSendingCompletedItemsAgain()
    {
        var outputPath = CreateTemporaryDirectory();

        try
        {
            var generated = await new MockEvDataGenerator().GenerateAsync(outputPath);
            var dataSet = await new EvDataSetLoader().LoadAsync(generated.CatalogPath);
            var discovery = new ArchiveDiscoveryService().Discover(dataSet, CreateTargetMap());
            var checkpointPath = Path.Combine(outputPath, "checkpoint.json");
            var firstClient = new RecordingClient();
            MigrationReport firstReport;

            using (var firstStore = new JsonCheckpointStore(checkpointPath))
            {
                firstReport = await new MigrationEngine().MigrateAsync(
                    dataSet,
                    discovery,
                    outputPath,
                    firstClient,
                    new MigrationOptions { WorkerCount = 3 },
                    firstStore);
            }

            var secondClient = new RecordingClient();
            MigrationReport secondReport;
            using (var secondStore = new JsonCheckpointStore(checkpointPath))
            {
                secondReport = await new MigrationEngine().MigrateAsync(
                    dataSet,
                    discovery,
                    outputPath,
                    secondClient,
                    new MigrationOptions { WorkerCount = 3 },
                    secondStore);
            }

            Assert.Equal(5, firstReport.UploadedItemCount);
            Assert.Equal(5, firstClient.Requests.Count);
            Assert.Equal(5, secondReport.CheckpointSkippedItemCount);
            Assert.Equal(0, secondReport.AttemptedItemCount);
            Assert.Empty(secondClient.Requests);

            var reportPath = Path.Combine(outputPath, "audit.json");
            await new JsonAuditReportWriter().WriteAsync(reportPath, secondReport);
            await using var stream = File.OpenRead(reportPath);
            var savedReport = await JsonSerializer.DeserializeAsync<MigrationReport>(stream, EvJson.Options);
            Assert.Equal(secondReport.RunId, savedReport?.RunId);
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    private static CheckpointItem CreateCheckpointItem(string sourceItemId) =>
        new()
        {
            SourceItemId = sourceItemId,
            TargetArchiveId = "sx-mailbox-ayse",
            ContentSha256 = new string('a', 64),
            Outcome = IngestOutcome.Created,
            CompletedAtUtc = DateTimeOffset.Parse("2026-08-17T12:00:00Z")
        };

    private static TargetArchiveMap CreateTargetMap() =>
        new()
        {
            UserArchives = new Dictionary<string, string>
            {
                ["ayse@contoso.com"] = "sx-mailbox-ayse",
                ["mehmet@contoso.com"] = "sx-mailbox-mehmet"
            },
            ComplianceArchiveId = "sx-compliance"
        };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ev-checkpoint-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingClient : IStorionXClient
    {
        public ConcurrentBag<StorionXIngestRequest> Requests { get; } = [];

        public Task<StorionXIngestResult> IngestAsync(
            StorionXIngestRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new StorionXIngestResult(IngestOutcome.Created, 1, 201));
        }
    }
}
