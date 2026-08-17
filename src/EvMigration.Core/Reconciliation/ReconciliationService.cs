using EvMigration.Core.Migration;
using EvMigration.Core.Models;
using EvMigration.Core.Rehydration;
using EvMigration.Core.Transform;

namespace EvMigration.Core.Reconciliation;

public sealed class ReconciliationService
{
    public async Task<ReconciliationReport> ReconcileAsync(
        EvDataSet dataSet,
        ArchiveDiscoveryReport discovery,
        string sourceRoot,
        StorionXStateSnapshot targetState,
        MigrationFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(targetState);
        filter ??= new MigrationFilter();
        filter.Validate();

        var targets = discovery.Archives
            .Where(archive => archive.Status == ArchiveMappingStatus.Mapped)
            .ToDictionary(
                archive => archive.SourceArchiveId,
                archive => archive.TargetArchiveId!,
                StringComparer.Ordinal);
        var sourceItems = dataSet.Items
            .Where(filter.Matches)
            .Where(item => targets.ContainsKey(item.ArchiveId))
            .ToArray();
        var rehydrator = new SisRehydrator(sourceRoot, dataSet.SisParts);
        var expectedItems = new Dictionary<string, ExpectedItem>(StringComparer.Ordinal);

        foreach (var item in sourceItems)
        {
            var rehydrated = await rehydrator.RehydrateAsync(item, cancellationToken);
            var sourceItemId = StorionXTransformer.CreateSourceItemId(item.ArchiveId, item.ItemId);
            expectedItems.Add(sourceItemId, new ExpectedItem(
                targets[item.ArchiveId],
                rehydrated.ContentSha256,
                item.SizeBytes));
        }

        var mappedTargetIds = targets.Values.ToHashSet(StringComparer.Ordinal);
        var targetItemsInScope = targetState.Items
            .Where(item => filter.IsEmpty
                ? mappedTargetIds.Contains(item.TargetArchiveId)
                : expectedItems.ContainsKey(item.SourceItemId))
            .ToArray();
        var targetItems = targetItemsInScope.ToDictionary(
            item => item.SourceItemId,
            StringComparer.Ordinal);
        var missing = new List<string>();
        var mismatches = new List<ReconciliationMismatch>();
        var matched = 0;

        foreach (var expected in expectedItems)
        {
            if (!targetItems.TryGetValue(expected.Key, out var actual))
            {
                missing.Add(expected.Key);
                continue;
            }

            var mismatchFields = new List<string>();
            if (actual.TargetArchiveId != expected.Value.TargetArchiveId)
            {
                mismatchFields.Add("target_archive_id");
            }

            if (!string.Equals(
                actual.MessageSha256,
                expected.Value.MessageSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                mismatchFields.Add("message_sha256");
            }

            if (actual.LogicalBytes != expected.Value.LogicalBytes)
            {
                mismatchFields.Add("logical_bytes");
            }

            if (mismatchFields.Count == 0)
            {
                matched++;
            }
            else
            {
                mismatches.Add(new ReconciliationMismatch(expected.Key, mismatchFields));
            }
        }

        var unexpected = filter.IsEmpty
            ? targetItems.Keys
                .Except(expectedItems.Keys, StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()
            : [];

        return new ReconciliationReport
        {
            RunId = Guid.NewGuid().ToString("N"),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            IsReconciled = missing.Count == 0 && unexpected.Length == 0 && mismatches.Count == 0,
            ExpectedItemCount = expectedItems.Count,
            TargetItemCount = targetItems.Count,
            MatchedItemCount = matched,
            SourceLogicalBytes = expectedItems.Values.Sum(item => item.LogicalBytes),
            TargetLogicalBytes = targetItems.Values.Sum(item => item.LogicalBytes),
            MissingItemIds = missing.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            UnexpectedItemIds = unexpected,
            Mismatches = mismatches.OrderBy(item => item.SourceItemId, StringComparer.Ordinal).ToArray()
        };
    }

    private sealed record ExpectedItem(
        string TargetArchiveId,
        string MessageSha256,
        long LogicalBytes);
}
