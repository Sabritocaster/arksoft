using EvMigration.Core.Models;

namespace EvMigration.Core.Discovery;

public sealed class ArchiveDiscoveryService
{
    public ArchiveDiscoveryReport Discover(EvDataSet dataSet, TargetArchiveMap targetMap)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(targetMap);

        var userTargets = NormalizeUserTargets(targetMap.UserArchives);
        var fileTargets = NormalizeFileTargets(targetMap.FileArchives);
        var itemsByArchive = dataSet.Items.ToLookup(item => item.ArchiveId, StringComparer.Ordinal);
        var archiveResults = new List<ArchiveDiscoveryResult>(dataSet.Archives.Count);

        foreach (var archive in dataSet.Archives)
        {
            var items = itemsByArchive[archive.ArchiveId].ToArray();
            var mapping = MapArchive(
                archive,
                targetMap.ComplianceArchiveId,
                userTargets,
                fileTargets);

            archiveResults.Add(new ArchiveDiscoveryResult
            {
                SourceArchiveId = archive.ArchiveId,
                ArchiveType = archive.Type,
                OwnerUpn = archive.OwnerUpn,
                TargetArchiveId = mapping.TargetArchiveId,
                Status = mapping.Status,
                Reason = mapping.Reason,
                LegalHold = archive.LegalHold,
                ItemCount = items.Length,
                TotalBytes = items.Sum(item => item.SizeBytes)
            });
        }

        var mappedArchiveIds = archiveResults
            .Where(result => result.Status == ArchiveMappingStatus.Mapped)
            .Select(result => result.SourceArchiveId)
            .ToHashSet(StringComparer.Ordinal);
        var eligibleItems = dataSet.Items
            .Where(item => mappedArchiveIds.Contains(item.ArchiveId))
            .ToArray();

        return new ArchiveDiscoveryReport
        {
            ArchiveCount = archiveResults.Count,
            ItemCount = dataSet.Items.Count,
            MappedArchiveCount = archiveResults.Count(result => result.Status == ArchiveMappingStatus.Mapped),
            PendingArchiveCount = archiveResults.Count(result => result.Status == ArchiveMappingStatus.PendingMapping),
            EligibleItemCount = eligibleItems.Length,
            PendingItemCount = dataSet.Items.Count - eligibleItems.Length,
            EligibleBytes = eligibleItems.Sum(item => item.SizeBytes),
            LegalHoldArchiveCount = archiveResults.Count(result => result.LegalHold),
            Archives = archiveResults
        };
    }

    private static ArchiveMapping MapArchive(
        EvArchive archive,
        string complianceArchiveId,
        IReadOnlyDictionary<string, string> userTargets,
        IReadOnlyDictionary<string, string> fileTargets)
    {
        if (archive.Type == EvArchiveType.Journal)
        {
            return string.IsNullOrWhiteSpace(complianceArchiveId)
                ? ArchiveMapping.Pending("Compliance archive is not configured.")
                : ArchiveMapping.Mapped(complianceArchiveId);
        }

        if (archive.Type == EvArchiveType.Fsa)
        {
            return fileTargets.TryGetValue(archive.ArchiveId, out var fileTargetArchiveId)
                ? ArchiveMapping.Mapped(fileTargetArchiveId)
                : ArchiveMapping.Pending("File archive was not found in the target mapping.");
        }

        if (string.IsNullOrWhiteSpace(archive.OwnerUpn))
        {
            return ArchiveMapping.Pending("Mailbox archive has no owner UPN.");
        }

        return userTargets.TryGetValue(NormalizeUpn(archive.OwnerUpn), out var targetArchiveId)
            ? ArchiveMapping.Mapped(targetArchiveId)
            : ArchiveMapping.Pending("Owner was not found in the target mapping.");
    }

    private static IReadOnlyDictionary<string, string> NormalizeUserTargets(
        IReadOnlyDictionary<string, string> targets)
    {
        if (targets is null)
        {
            throw new InvalidDataException("User archive mappings are required.");
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var upn = NormalizeUpn(target.Key);
            if (string.IsNullOrWhiteSpace(upn)
                || string.IsNullOrWhiteSpace(target.Value)
                || !normalized.TryAdd(upn, target.Value))
            {
                throw new InvalidDataException("User archive mapping contains an invalid or duplicate UPN.");
            }
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, string> NormalizeFileTargets(
        IReadOnlyDictionary<string, string> targets)
    {
        if (targets is null)
        {
            throw new InvalidDataException("File archive mappings are required.");
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.Key)
                || string.IsNullOrWhiteSpace(target.Value)
                || !normalized.TryAdd(target.Key.Trim(), target.Value))
            {
                throw new InvalidDataException("File archive mapping contains an invalid or duplicate archive ID.");
            }
        }

        return normalized;
    }

    private static string NormalizeUpn(string upn) => upn.Trim().ToLowerInvariant();

    private sealed record ArchiveMapping(
        ArchiveMappingStatus Status,
        string? TargetArchiveId,
        string? Reason)
    {
        public static ArchiveMapping Mapped(string targetArchiveId) =>
            new(ArchiveMappingStatus.Mapped, targetArchiveId, null);

        public static ArchiveMapping Pending(string reason) =>
            new(ArchiveMappingStatus.PendingMapping, null, reason);
    }
}
