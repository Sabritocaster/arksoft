using System.Security.Cryptography;
using System.Text;
using EvMigration.Core.Models;
using StorionX.MockApi;

namespace EvMigration.Tests;

public sealed class MockStorionXTests
{
    [Fact]
    public void Ingest_RepeatedRequestIsIdempotent()
    {
        var store = new MockStorionXStore();
        var request = CreateRequest("ev:A1:I100", "shared content");

        var first = store.Ingest("ev:A1:I100", request);
        var second = store.Ingest("ev:A1:I100", request);

        Assert.Equal(StoreStatus.Created, first.Status);
        Assert.Equal(StoreStatus.Existing, second.Status);
        Assert.Equal(1, store.GetState().ItemCount);
    }

    [Fact]
    public void Ingest_StoresSharedPartsOnce()
    {
        var store = new MockStorionXStore();

        store.Ingest("ev:A1:I100", CreateRequest("ev:A1:I100", "shared content"));
        store.Ingest("ev:J1:J100", CreateRequest("ev:J1:J100", "shared content"));

        var state = store.GetState();
        Assert.Equal(2, state.ItemCount);
        Assert.Equal(1, state.UniquePartCount);
        Assert.True(state.PhysicalBytes < state.LogicalBytes);
    }

    [Fact]
    public void TryAcquire_EnforcesTheRequestLimit()
    {
        var limiter = new MockRateLimiter(requestsPerSecond: 2, maxBytesPerMinute: 1_000);
        var now = DateTimeOffset.Parse("2026-08-15T12:00:00Z");

        Assert.True(limiter.TryAcquire(100, now, out _));
        Assert.True(limiter.TryAcquire(100, now, out _));
        Assert.False(limiter.TryAcquire(100, now, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(1), retryAfter);
        Assert.True(limiter.TryAcquire(100, now.AddSeconds(1), out _));
    }

    private static StorionXIngestRequest CreateRequest(string sourceItemId, string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        var sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        return new StorionXIngestRequest
        {
            TargetArchiveId = "sx-user-ayse",
            SourceItemId = sourceItemId,
            MessageSha256 = sha256,
            Parts =
            [
                new StorionXContentPart
                {
                    Sha256 = sha256,
                    SizeBytes = data.LongLength,
                    DataBase64 = Convert.ToBase64String(data)
                }
            ],
            Metadata = new StorionXMetadata
            {
                SourceArchiveId = "A1",
                SourceItemId = sourceItemId,
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
}
