using System.Collections.Concurrent;
using System.Text.Json;
using EvMigration.Core.Ingestion;
using EvMigration.Core.Models;
using EvMigration.Core.Rehydration;
using EvMigration.Core.Transform;

namespace EvMigration.Core.Migration;

public sealed class MigrationEngine
{
    public async Task<MigrationReport> MigrateAsync(
        EvDataSet dataSet,
        ArchiveDiscoveryReport discovery,
        string sourceRoot,
        IStorionXClient storionXClient,
        MigrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(storionXClient);
        options ??= new MigrationOptions();

        if (options.WorkerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Worker count must be greater than zero.");
        }

        var archives = dataSet.Archives.ToDictionary(archive => archive.ArchiveId, StringComparer.Ordinal);
        var targets = discovery.Archives
            .Where(archive => archive.Status == ArchiveMappingStatus.Mapped)
            .ToDictionary(
                archive => archive.SourceArchiveId,
                archive => archive.TargetArchiveId!,
                StringComparer.Ordinal);
        var rehydrator = new SisRehydrator(sourceRoot, dataSet.SisParts);
        var transformer = new StorionXTransformer();
        var failures = new ConcurrentBag<MigrationFailure>();
        var eligibleItems = dataSet.Items
            .Where(item => targets.ContainsKey(item.ArchiveId))
            .ToArray();
        var uploaded = 0;
        var existing = 0;
        var retryCount = 0;
        long migratedBytes = 0;

        await Parallel.ForEachAsync(
            eligibleItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.WorkerCount,
                CancellationToken = cancellationToken
            },
            async (item, workerCancellationToken) =>
            {
                try
                {
                    var rehydrated = await rehydrator.RehydrateAsync(item, workerCancellationToken);
                    var request = transformer.Transform(
                        archives[item.ArchiveId],
                        item,
                        targets[item.ArchiveId],
                        rehydrated);
                    var result = await storionXClient.IngestAsync(request, workerCancellationToken);
                    Interlocked.Add(ref retryCount, Math.Max(0, result.Attempts - 1));

                    if (result.Outcome == IngestOutcome.Failed)
                    {
                        failures.Add(new MigrationFailure(item.ItemId, result.Error ?? "Ingest failed."));
                        return;
                    }

                    if (result.Outcome == IngestOutcome.Created)
                    {
                        Interlocked.Increment(ref uploaded);
                    }
                    else
                    {
                        Interlocked.Increment(ref existing);
                    }

                    Interlocked.Add(ref migratedBytes, item.SizeBytes);
                }
                catch (OperationCanceledException) when (workerCancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException
                                                  or InvalidDataException
                                                  or HttpRequestException
                                                  or JsonException)
                {
                    failures.Add(new MigrationFailure(item.ItemId, exception.Message));
                }
            });

        return new MigrationReport
        {
            WorkerCount = options.WorkerCount,
            ScannedItemCount = dataSet.Items.Count,
            PendingMappingItemCount = dataSet.Items.Count - eligibleItems.Length,
            AttemptedItemCount = eligibleItems.Length,
            UploadedItemCount = uploaded,
            ExistingItemCount = existing,
            FailedItemCount = failures.Count,
            RetryCount = retryCount,
            MigratedBytes = migratedBytes,
            PhysicalSisReads = rehydrator.PhysicalReadCount,
            CachedSisParts = rehydrator.CachedPartCount,
            Failures = failures.OrderBy(failure => failure.ItemId, StringComparer.Ordinal).ToArray()
        };
    }
}
