using EvMigration.Core.Discovery;
using EvMigration.Core.Mock;
using EvMigration.Core.Models;

namespace EvMigration.Tests;

public sealed class ArchiveDiscoveryTests
{
    [Fact]
    public async Task Discover_MapsKnownUsersAndDefersTheOrphanedArchive()
    {
        var outputPath = CreateTemporaryDirectory();

        try
        {
            var generated = await new MockEvDataGenerator().GenerateAsync(outputPath);
            var dataSet = await new EvDataSetLoader().LoadAsync(generated.CatalogPath);
            var targetMap = CreateTargetMap();

            var report = new ArchiveDiscoveryService().Discover(dataSet, targetMap);

            Assert.Equal(4, report.ArchiveCount);
            Assert.Equal(3, report.MappedArchiveCount);
            Assert.Equal(1, report.PendingArchiveCount);
            Assert.Equal(5, report.EligibleItemCount);
            Assert.Equal(1, report.PendingItemCount);
            Assert.Equal(2, report.LegalHoldArchiveCount);

            var orphaned = Assert.Single(
                report.Archives,
                archive => archive.SourceArchiveId == "A3");
            Assert.Equal(ArchiveMappingStatus.PendingMapping, orphaned.Status);
            Assert.Null(orphaned.TargetArchiveId);

            var journal = Assert.Single(
                report.Archives,
                archive => archive.SourceArchiveId == "J1");
            Assert.Equal("sx-compliance", journal.TargetArchiveId);
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public void Discover_MatchesUpnWithoutCaseOrOuterWhitespace()
    {
        var dataSet = new EvDataSet
        {
            Archives =
            [
                new EvArchive
                {
                    ArchiveId = "A1",
                    Type = EvArchiveType.Mailbox,
                    OwnerUpn = "  AYSE@CONTOSO.COM ",
                    LegalHold = false
                }
            ],
            Items = [],
            SisParts = []
        };

        var report = new ArchiveDiscoveryService().Discover(dataSet, CreateTargetMap());

        var archive = Assert.Single(report.Archives);
        Assert.Equal(ArchiveMappingStatus.Mapped, archive.Status);
        Assert.Equal("sx-mailbox-ayse", archive.TargetArchiveId);
    }

    [Fact]
    public void Discover_RejectsAmbiguousNormalizedUpns()
    {
        var targetMap = new TargetArchiveMap
        {
            UserArchives = new Dictionary<string, string>
            {
                ["ayse@contoso.com"] = "sx-mailbox-ayse",
                ["AYSE@CONTOSO.COM"] = "sx-mailbox-other"
            },
            ComplianceArchiveId = "sx-compliance"
        };

        var dataSet = new EvDataSet
        {
            Archives = [],
            Items = [],
            SisParts = []
        };

        Assert.Throws<InvalidDataException>(
            () => new ArchiveDiscoveryService().Discover(dataSet, targetMap));
    }

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
        var path = Path.Combine(Path.GetTempPath(), $"ev-discovery-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
