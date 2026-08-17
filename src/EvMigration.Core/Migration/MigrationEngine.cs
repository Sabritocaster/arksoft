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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(storionXClient);

        var archives = dataSet.Archives.ToDictionary(archive => archive.ArchiveId, StringComparer.Ordinal);
        var targets = discovery.Archives
            .Where(archive => archive.Status == ArchiveMappingStatus.Mapped)
            .ToDictionary(
                archive => archive.SourceArchiveId,
                archive => archive.TargetArchiveId!,
                StringComparer.Ordinal);
        var rehydrator = new SisRehydrator(sourceRoot, dataSet.SisParts);
        var transformer = new StorionXTransformer();
        var failures = new List<MigrationFailure>();
        var attempted = 0;
        var uploaded = 0;
        var existing = 0;
        var retryCount = 0;
        long migratedBytes = 0;

        foreach (var item in dataSet.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!targets.TryGetValue(item.ArchiveId, out var targetArchiveId))
            {
                continue;
            }

            attempted++;
            try
            {
                var rehydrated = await rehydrator.RehydrateAsync(item, cancellationToken);
                var request = transformer.Transform(
                    archives[item.ArchiveId],
                    item,
                    targetArchiveId,
                    rehydrated);
                var result = await storionXClient.IngestAsync(request, cancellationToken);
                retryCount += Math.Max(0, result.Attempts - 1);

                if (result.Outcome == IngestOutcome.Failed)
                {
                    failures.Add(new MigrationFailure(item.ItemId, result.Error ?? "Ingest failed."));
                    continue;
                }

                if (result.Outcome == IngestOutcome.Created)
                {
                    uploaded++;
                }
                else
                {
                    existing++;
                }

                migratedBytes += item.SizeBytes;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        }

        return new MigrationReport
        {
            ScannedItemCount = dataSet.Items.Count,
            PendingMappingItemCount = dataSet.Items.Count - attempted,
            AttemptedItemCount = attempted,
            UploadedItemCount = uploaded,
            ExistingItemCount = existing,
            FailedItemCount = failures.Count,
            RetryCount = retryCount,
            MigratedBytes = migratedBytes,
            PhysicalSisReads = rehydrator.PhysicalReadCount,
            CachedSisParts = rehydrator.CachedPartCount,
            Failures = failures
        };
    }
}
