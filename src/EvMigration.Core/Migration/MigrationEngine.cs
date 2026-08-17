using System.Collections.Concurrent;
using System.Text.Json;
using EvMigration.Core.Ingestion;
using EvMigration.Core.Models;
using EvMigration.Core.Persistence;
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
        IMigrationCheckpointStore? checkpointStore = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(storionXClient);
        options ??= new MigrationOptions();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid().ToString("N");

        if (options.WorkerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Worker count must be greater than zero.");
        }

        options.Filter.Validate();

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
        var selectedItems = dataSet.Items
            .Where(options.Filter.Matches)
            .ToArray();
        var eligibleItems = selectedItems
            .Where(item => targets.ContainsKey(item.ArchiveId))
            .ToArray();
        var plannedBytes = eligibleItems.Sum(item => item.SizeBytes);

        if (options.DryRun)
        {
            return new MigrationReport
            {
                RunId = runId,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                WorkerCount = options.WorkerCount,
                DryRun = true,
                ScannedItemCount = dataSet.Items.Count,
                FilteredOutItemCount = dataSet.Items.Count - selectedItems.Length,
                EligibleItemCount = eligibleItems.Length,
                PendingMappingItemCount = selectedItems.Length - eligibleItems.Length,
                CheckpointSkippedItemCount = 0,
                AttemptedItemCount = 0,
                UploadedItemCount = 0,
                ExistingItemCount = 0,
                FailedItemCount = 0,
                RetryCount = 0,
                MigratedBytes = 0,
                PlannedBytes = plannedBytes,
                PhysicalSisReads = 0,
                CachedSisParts = 0,
                ErrorBreakdown = new Dictionary<string, int>(),
                Failures = []
            };
        }

        if (checkpointStore is not null)
        {
            await checkpointStore.InitializeAsync(cancellationToken);
        }

        var migrationItems = eligibleItems
            .Where(item => checkpointStore is null
                           || !checkpointStore.IsCompleted(
                               StorionXTransformer.CreateSourceItemId(item.ArchiveId, item.ItemId),
                               targets[item.ArchiveId]))
            .ToArray();
        var uploaded = 0;
        var existing = 0;
        var retryCount = 0;
        long migratedBytes = 0;

        await Parallel.ForEachAsync(
            migrationItems,
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
                        failures.Add(new MigrationFailure(
                            item.ItemId,
                            "ingestion",
                            result.Error ?? "Ingest failed.",
                            result.HttpStatusCode));
                        return;
                    }

                    if (checkpointStore is not null)
                    {
                        await checkpointStore.MarkCompletedAsync(
                            new CheckpointItem
                            {
                                SourceItemId = request.SourceItemId,
                                TargetArchiveId = request.TargetArchiveId,
                                ContentSha256 = request.MessageSha256,
                                Outcome = result.Outcome,
                                CompletedAtUtc = DateTimeOffset.UtcNow
                            },
                            workerCancellationToken);
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
                    failures.Add(new MigrationFailure(
                        item.ItemId,
                        GetErrorCategory(exception),
                        exception.Message));
                }
            });

        var orderedFailures = failures
            .OrderBy(failure => failure.ItemId, StringComparer.Ordinal)
            .ToArray();

        return new MigrationReport
        {
            RunId = runId,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            WorkerCount = options.WorkerCount,
            DryRun = false,
            ScannedItemCount = dataSet.Items.Count,
            FilteredOutItemCount = dataSet.Items.Count - selectedItems.Length,
            EligibleItemCount = eligibleItems.Length,
            PendingMappingItemCount = selectedItems.Length - eligibleItems.Length,
            CheckpointSkippedItemCount = eligibleItems.Length - migrationItems.Length,
            AttemptedItemCount = migrationItems.Length,
            UploadedItemCount = uploaded,
            ExistingItemCount = existing,
            FailedItemCount = failures.Count,
            RetryCount = retryCount,
            MigratedBytes = migratedBytes,
            PlannedBytes = plannedBytes,
            PhysicalSisReads = rehydrator.PhysicalReadCount,
            CachedSisParts = rehydrator.CachedPartCount,
            ErrorBreakdown = orderedFailures
                .GroupBy(failure => failure.Category, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            Failures = orderedFailures
        };
    }

    private static string GetErrorCategory(Exception exception) => exception switch
    {
        InvalidDataException => "source_validation",
        HttpRequestException => "network",
        JsonException => "serialization",
        IOException => "io",
        _ => "unknown"
    };
}
