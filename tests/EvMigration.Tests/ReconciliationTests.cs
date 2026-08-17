using EvMigration.Core.Discovery;
using EvMigration.Core.Migration;
using EvMigration.Core.Mock;
using EvMigration.Core.Models;
using EvMigration.Core.Reconciliation;
using EvMigration.Core.Rehydration;
using EvMigration.Core.Transform;

namespace EvMigration.Tests;

public sealed class ReconciliationTests
{
    [Fact]
    public async Task ReconcileAsync_AcceptsMatchingTargetState()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var report = await new ReconciliationService().ReconcileAsync(
                fixture.DataSet,
                fixture.Discovery,
                fixture.OutputPath,
                fixture.TargetState);

            Assert.True(report.IsReconciled);
            Assert.Equal(5, report.ExpectedItemCount);
            Assert.Equal(5, report.MatchedItemCount);
            Assert.Equal(report.SourceLogicalBytes, report.TargetLogicalBytes);
            Assert.Empty(report.MissingItemIds);
            Assert.Empty(report.Mismatches);
        }
        finally
        {
            Directory.Delete(fixture.OutputPath, recursive: true);
        }
    }

    [Fact]
    public async Task ReconcileAsync_ReportsMissingAndHashMismatchItems()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var changedItems = fixture.TargetState.Items.Take(4).ToArray();
            changedItems[0] = changedItems[0] with { MessageSha256 = new string('0', 64) };
            var changedState = fixture.TargetState with
            {
                ItemCount = changedItems.Length,
                Items = changedItems
            };

            var report = await new ReconciliationService().ReconcileAsync(
                fixture.DataSet,
                fixture.Discovery,
                fixture.OutputPath,
                changedState);

            Assert.False(report.IsReconciled);
            Assert.Single(report.MissingItemIds);
            var mismatch = Assert.Single(report.Mismatches);
            Assert.Contains("message_sha256", mismatch.Fields);
        }
        finally
        {
            Directory.Delete(fixture.OutputPath, recursive: true);
        }
    }

    private static async Task<ReconciliationFixture> CreateFixtureAsync()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"ev-reconcile-tests-{Guid.NewGuid():N}");
        var generated = await new MockEvDataGenerator().GenerateAsync(outputPath);
        var dataSet = await new EvDataSetLoader().LoadAsync(generated.CatalogPath);
        var targetMap = new TargetArchiveMap
        {
            UserArchives = new Dictionary<string, string>
            {
                ["ayse@contoso.com"] = "sx-mailbox-ayse",
                ["mehmet@contoso.com"] = "sx-mailbox-mehmet"
            },
            ComplianceArchiveId = "sx-compliance"
        };
        var discovery = new ArchiveDiscoveryService().Discover(dataSet, targetMap);
        var targets = discovery.Archives
            .Where(archive => archive.Status == ArchiveMappingStatus.Mapped)
            .ToDictionary(archive => archive.SourceArchiveId, archive => archive.TargetArchiveId!);
        var rehydrator = new SisRehydrator(outputPath, dataSet.SisParts);
        var targetItems = new List<StorionXStoredItemSnapshot>();

        foreach (var item in dataSet.Items.Where(item => targets.ContainsKey(item.ArchiveId)))
        {
            var content = await rehydrator.RehydrateAsync(item);
            targetItems.Add(new StorionXStoredItemSnapshot
            {
                SourceItemId = StorionXTransformer.CreateSourceItemId(item.ArchiveId, item.ItemId),
                TargetArchiveId = targets[item.ArchiveId],
                MessageSha256 = content.ContentSha256,
                LogicalBytes = item.SizeBytes
            });
        }

        var targetState = new StorionXStateSnapshot
        {
            ItemCount = targetItems.Count,
            UniquePartCount = rehydrator.CachedPartCount,
            LogicalBytes = targetItems.Sum(item => item.LogicalBytes),
            PhysicalBytes = dataSet.SisParts.Sum(part => part.SizeBytes),
            Items = targetItems
        };
        return new ReconciliationFixture(outputPath, dataSet, discovery, targetState);
    }

    private sealed record ReconciliationFixture(
        string OutputPath,
        EvDataSet DataSet,
        ArchiveDiscoveryReport Discovery,
        StorionXStateSnapshot TargetState);
}
