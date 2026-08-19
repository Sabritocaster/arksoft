using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EvMigration.Core.Models;
using EvMigration.Core.Serialization;

namespace StorionX.MockApi;

public sealed class MockStorionXStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _parts = new(StringComparer.OrdinalIgnoreCase);

    public StoreResult Ingest(string idempotencyKey, StorionXIngestRequest request)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return StoreResult.Invalid("Idempotency-Key is required.");
        }

        var validation = ValidateAndDecode(request);
        if (validation.Error is not null)
        {
            return StoreResult.Invalid(validation.Error);
        }

        var requestFingerprint = ComputeRequestFingerprint(request);

        lock (_gate)
        {
            if (_items.TryGetValue(idempotencyKey, out var existing))
            {
                return existing.RequestFingerprint == requestFingerprint
                    ? StoreResult.Existing(existing.SourceItemId)
                    : StoreResult.Conflict("Idempotency key was already used with a different payload.");
            }

            foreach (var part in validation.Parts)
            {
                _parts.TryAdd(part.Sha256, part.Data);
            }

            _items.Add(idempotencyKey, new StoredItem(
                request.SourceItemId,
                request.TargetArchiveId,
                request.MessageSha256,
                requestFingerprint,
                validation.Parts.Select(part => part.Sha256).ToArray(),
                validation.Parts.Sum(part => part.Data.LongLength),
                request.Retention.LegalHold));

            return StoreResult.Created(request.SourceItemId);
        }
    }

    public StorionXState GetState()
    {
        lock (_gate)
        {
            return new StorionXState(
                _items.Count,
                _parts.Count,
                _items.Values.Sum(item => item.LogicalBytes),
                _parts.Values.Sum(part => part.LongLength),
                _items.Values
                    .OrderBy(item => item.SourceItemId, StringComparer.Ordinal)
                    .Select(item => new StoredItemSummary(
                        item.SourceItemId,
                        item.TargetArchiveId,
                        item.MessageSha256,
                        item.PartHashes,
                        item.LogicalBytes,
                        item.LegalHold))
                    .ToArray());
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _items.Clear();
            _parts.Clear();
        }
    }

    private static DecodedRequest ValidateAndDecode(StorionXIngestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetArchiveId)
            || string.IsNullOrWhiteSpace(request.SourceItemId)
            || string.IsNullOrWhiteSpace(request.MessageSha256)
            || request.Parts is null
            || request.Parts.Count == 0
            || request.Metadata is null
            || request.Retention is null)
        {
            return DecodedRequest.Failed("Required ingest fields are missing.");
        }

        var decodedParts = new List<DecodedPart>(request.Parts.Count);
        using var messageHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var part in request.Parts)
        {
            byte[] data;
            try
            {
                data = Convert.FromBase64String(part.DataBase64);
            }
            catch (FormatException)
            {
                return DecodedRequest.Failed("A content part is not valid base64.");
            }

            var actualHash = ComputeSha256(data);
            if (part.SizeBytes != data.LongLength
                || !string.Equals(part.Sha256, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                return DecodedRequest.Failed("A content part failed size or SHA-256 validation.");
            }

            messageHash.AppendData(data);
            decodedParts.Add(new DecodedPart(actualHash, data));
        }

        var actualMessageHash = Convert.ToHexString(messageHash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(request.MessageSha256, actualMessageHash, StringComparison.OrdinalIgnoreCase))
        {
            return DecodedRequest.Failed("The rehydrated message SHA-256 is invalid.");
        }

        return DecodedRequest.Success(decodedParts);
    }

    private static string ComputeRequestFingerprint(StorionXIngestRequest request)
    {
        var json = JsonSerializer.Serialize(request, EvJson.Options);
        return ComputeSha256(Encoding.UTF8.GetBytes(json));
    }

    private static string ComputeSha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed record StoredItem(
        string SourceItemId,
        string TargetArchiveId,
        string MessageSha256,
        string RequestFingerprint,
        IReadOnlyList<string> PartHashes,
        long LogicalBytes,
        bool LegalHold);

    private sealed record DecodedPart(string Sha256, byte[] Data);

    private sealed record DecodedRequest(IReadOnlyList<DecodedPart> Parts, string? Error)
    {
        public static DecodedRequest Success(IReadOnlyList<DecodedPart> parts) => new(parts, null);

        public static DecodedRequest Failed(string error) => new([], error);
    }
}

public enum StoreStatus
{
    Created,
    Existing,
    Conflict,
    Invalid
}

public sealed record StoreResult(StoreStatus Status, string? SourceItemId, string? Error)
{
    public static StoreResult Created(string sourceItemId) => new(StoreStatus.Created, sourceItemId, null);

    public static StoreResult Existing(string sourceItemId) => new(StoreStatus.Existing, sourceItemId, null);

    public static StoreResult Conflict(string error) => new(StoreStatus.Conflict, null, error);

    public static StoreResult Invalid(string error) => new(StoreStatus.Invalid, null, error);
}

public sealed record StorionXState(
    int ItemCount,
    int UniquePartCount,
    long LogicalBytes,
    long PhysicalBytes,
    IReadOnlyList<StoredItemSummary> Items);

public sealed record StoredItemSummary(
    string SourceItemId,
    string TargetArchiveId,
    string MessageSha256,
    IReadOnlyList<string> PartHashes,
    long LogicalBytes,
    bool LegalHold);
