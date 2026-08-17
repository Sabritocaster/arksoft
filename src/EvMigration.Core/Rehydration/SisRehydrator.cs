using System.Collections.Concurrent;
using System.Security.Cryptography;
using EvMigration.Core.Models;

namespace EvMigration.Core.Rehydration;

public sealed class SisRehydrator
{
    private readonly string _sourceRoot;
    private readonly IReadOnlyDictionary<string, EvSisPart> _partCatalog;
    private readonly ConcurrentDictionary<string, Lazy<Task<RehydratedPart>>> _cache =
        new(StringComparer.Ordinal);
    private int _physicalReadCount;

    public SisRehydrator(string sourceRoot, IEnumerable<EvSisPart> sisParts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentNullException.ThrowIfNull(sisParts);

        _sourceRoot = Path.GetFullPath(sourceRoot);
        _partCatalog = sisParts.ToDictionary(part => part.PartId, StringComparer.Ordinal);
    }

    public int CachedPartCount => _cache.Count;

    public int PhysicalReadCount => Volatile.Read(ref _physicalReadCount);

    public async Task<RehydratedItem> RehydrateAsync(
        EvItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var parts = new List<RehydratedPart>(item.ContentParts.Count);
        using var content = new MemoryStream();
        using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var partId in item.ContentParts)
        {
            if (!_partCatalog.TryGetValue(partId, out var partMetadata))
            {
                throw new InvalidDataException(
                    $"Item '{item.ItemId}' references unknown SIS part '{partId}'.");
            }

            var part = await GetPartAsync(partMetadata, cancellationToken);
            content.Write(part.Data);
            contentHash.AppendData(part.Data);
            parts.Add(part);
        }

        if (content.Length != item.SizeBytes)
        {
            throw new InvalidDataException(
                $"Rehydrated item '{item.ItemId}' does not match its expected size.");
        }

        var sha256 = Convert.ToHexString(contentHash.GetHashAndReset()).ToLowerInvariant();
        return new RehydratedItem(item.ItemId, content.ToArray(), sha256, parts);
    }

    private async Task<RehydratedPart> GetPartAsync(
        EvSisPart partMetadata,
        CancellationToken cancellationToken)
    {
        var lazyPart = _cache.GetOrAdd(
            partMetadata.PartId,
            _ => new Lazy<Task<RehydratedPart>>(
                () => ReadAndValidatePartAsync(partMetadata),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var loadTask = lazyPart.Value;

        try
        {
            return await loadTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (loadTask.IsFaulted)
            {
                _cache.TryRemove(partMetadata.PartId, out _);
            }

            throw;
        }
    }

    private async Task<RehydratedPart> ReadAndValidatePartAsync(EvSisPart partMetadata)
    {
        var filePath = ResolveSafePath(partMetadata.DataRef);
        Interlocked.Increment(ref _physicalReadCount);
        var data = await File.ReadAllBytesAsync(filePath);

        if (data.LongLength != partMetadata.SizeBytes)
        {
            throw new InvalidDataException(
                $"SIS part '{partMetadata.PartId}' failed size validation.");
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (!string.Equals(actualHash, partMetadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SIS part '{partMetadata.PartId}' failed SHA-256 validation.");
        }

        return new RehydratedPart(partMetadata.PartId, actualHash, data);
    }

    private string ResolveSafePath(string dataReference)
    {
        if (string.IsNullOrWhiteSpace(dataReference) || Path.IsPathRooted(dataReference))
        {
            throw new InvalidDataException("SIS data reference must be a relative path.");
        }

        var normalizedReference = dataReference.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_sourceRoot, normalizedReference));
        var relativePath = Path.GetRelativePath(_sourceRoot, fullPath);

        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("SIS data reference escapes the source directory.");
        }

        return fullPath;
    }
}
