using System.Net;
using System.Security.Cryptography;
using System.Text;
using EvMigration.Core.Discovery;
using EvMigration.Core.Ingestion;
using EvMigration.Core.Migration;
using EvMigration.Core.Mock;
using EvMigration.Core.Models;
using EvMigration.Core.Rehydration;
using EvMigration.Core.Transform;

namespace EvMigration.Tests;

public sealed class MigrationEngineTests
{
    [Fact]
    public async Task Transform_PreservesMetadataRetentionAndLegalHold()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var archive = fixture.DataSet.Archives.Single(candidate => candidate.ArchiveId == "A2");
            var item = fixture.DataSet.Items.Single(candidate => candidate.ItemId == "I200");
            var rehydrated = await new SisRehydrator(fixture.OutputPath, fixture.DataSet.SisParts)
                .RehydrateAsync(item);

            var request = new StorionXTransformer().Transform(
                archive,
                item,
                "sx-mailbox-mehmet",
                rehydrated);

            Assert.Equal("ev:A2:I200", request.SourceItemId);
            Assert.Equal(item.FolderPath, request.Metadata.FolderPath);
            Assert.Equal(item.SentDate, request.Metadata.SentDate);
            Assert.Equal(item.RetentionCategory, request.Retention.Category);
            Assert.True(request.Retention.LegalHold);
            Assert.Equal(item.ContentParts.Count, request.Parts.Count);
        }
        finally
        {
            Directory.Delete(fixture.OutputPath, recursive: true);
        }
    }

    [Fact]
    public async Task HttpClient_RetriesAServiceUnavailableResponse()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.Created));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var delays = new List<TimeSpan>();
        var client = new StorionXHttpClient(
            httpClient,
            maxRetries: 2,
            initialRetryDelay: TimeSpan.FromMilliseconds(10),
            delay: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            jitter: () => 0);

        var result = await client.IngestAsync(CreateRequest());

        Assert.Equal(IngestOutcome.Created, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal([TimeSpan.FromMilliseconds(10)], delays);
    }

    [Fact]
    public async Task MigrateAsync_SkipsOrphanAndMigratesLegalHoldItems()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var targetMap = new TargetArchiveMap
            {
                UserArchives = new Dictionary<string, string>
                {
                    ["ayse@contoso.com"] = "sx-mailbox-ayse",
                    ["mehmet@contoso.com"] = "sx-mailbox-mehmet"
                },
                ComplianceArchiveId = "sx-compliance"
            };
            var discovery = new ArchiveDiscoveryService().Discover(fixture.DataSet, targetMap);
            var client = new RecordingClient();

            var report = await new MigrationEngine().MigrateAsync(
                fixture.DataSet,
                discovery,
                fixture.OutputPath,
                client);

            Assert.Equal(6, report.ScannedItemCount);
            Assert.Equal(1, report.PendingMappingItemCount);
            Assert.Equal(5, report.UploadedItemCount);
            Assert.Equal(0, report.FailedItemCount);
            Assert.Equal(8, report.PhysicalSisReads);
            Assert.Equal(3, client.Requests.Count(request => request.Retention.LegalHold));
            Assert.DoesNotContain(client.Requests, request => request.Metadata.SourceArchiveId == "A3");
        }
        finally
        {
            Directory.Delete(fixture.OutputPath, recursive: true);
        }
    }

    private static async Task<MigrationFixture> CreateFixtureAsync()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"ev-migration-tests-{Guid.NewGuid():N}");
        var generated = await new MockEvDataGenerator().GenerateAsync(outputPath);
        var dataSet = await new EvDataSetLoader().LoadAsync(generated.CatalogPath);
        return new MigrationFixture(outputPath, dataSet);
    }

    private static StorionXIngestRequest CreateRequest()
    {
        var data = Encoding.UTF8.GetBytes("test");
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        return new StorionXIngestRequest
        {
            TargetArchiveId = "sx-mailbox-ayse",
            SourceItemId = "ev:A1:I100",
            MessageSha256 = hash,
            Parts =
            [
                new StorionXContentPart
                {
                    Sha256 = hash,
                    SizeBytes = data.LongLength,
                    DataBase64 = Convert.ToBase64String(data)
                }
            ],
            Metadata = new StorionXMetadata
            {
                SourceArchiveId = "A1",
                SourceItemId = "I100",
                FolderPath = "Inbox",
                Subject = "Test",
                SentDate = DateTimeOffset.Parse("2021-03-04T09:12:00Z"),
                From = "bob@partner.com",
                To = ["ayse@contoso.com"]
            },
            Retention = new StorionXRetention
            {
                Category = "7y",
                LegalHold = false
            }
        };
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
    }

    private sealed class RecordingClient : IStorionXClient
    {
        public List<StorionXIngestRequest> Requests { get; } = [];

        public Task<StorionXIngestResult> IngestAsync(
            StorionXIngestRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new StorionXIngestResult(IngestOutcome.Created, 1, 201));
        }
    }

    private sealed record MigrationFixture(string OutputPath, EvDataSet DataSet);
}
