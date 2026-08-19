using System.Collections.Concurrent;
using System.Text.Json;
using EvMigration.Core.Models;
using EvMigration.Core.Serialization;

namespace EvMigration.Core.Persistence;

public sealed class JsonCheckpointStore : IMigrationCheckpointStore, IDisposable
{
    private readonly string _checkpointPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CheckpointItem> _completedItems =
        new(StringComparer.Ordinal);
    private int _initialized;

    public JsonCheckpointStore(string checkpointPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);
        _checkpointPath = Path.GetFullPath(checkpointPath);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _initialized) == 1)
            {
                return;
            }

            if (File.Exists(_checkpointPath))
            {
                _completedItems.Clear();
                await using var stream = File.OpenRead(_checkpointPath);
                var checkpoint = await JsonSerializer.DeserializeAsync<MigrationCheckpoint>(
                    stream,
                    EvJson.Options,
                    cancellationToken)
                    ?? throw new InvalidDataException("Checkpoint file is empty or invalid.");

                if (checkpoint.Version != 1 || checkpoint.CompletedItems is null)
                {
                    throw new InvalidDataException("Checkpoint version or content is not supported.");
                }

                foreach (var item in checkpoint.CompletedItems)
                {
                    if (string.IsNullOrWhiteSpace(item.SourceItemId)
                        || !_completedItems.TryAdd(item.SourceItemId, item))
                    {
                        throw new InvalidDataException("Checkpoint contains an invalid or duplicate item.");
                    }
                }
            }

            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public bool IsCompleted(string sourceItemId, string targetArchiveId)
    {
        EnsureInitialized();
        return _completedItems.TryGetValue(sourceItemId, out var item)
            && string.Equals(item.TargetArchiveId, targetArchiveId, StringComparison.Ordinal);
    }

    public async Task MarkCompletedAsync(
        CheckpointItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureInitialized();

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            _completedItems.TryGetValue(item.SourceItemId, out var previous);
            _completedItems[item.SourceItemId] = item;

            try
            {
                var checkpoint = new MigrationCheckpoint
                {
                    CompletedItems = _completedItems.Values
                        .OrderBy(entry => entry.SourceItemId, StringComparer.Ordinal)
                        .ToArray()
                };
                await AtomicJsonFile.WriteAsync(_checkpointPath, checkpoint, cancellationToken);
            }
            catch
            {
                if (previous is null)
                {
                    _completedItems.TryRemove(item.SourceItemId, out _);
                }
                else
                {
                    _completedItems[item.SourceItemId] = previous;
                }

                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose() => _writeLock.Dispose();

    private void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized) == 0)
        {
            throw new InvalidOperationException("Checkpoint store must be initialized first.");
        }
    }
}
