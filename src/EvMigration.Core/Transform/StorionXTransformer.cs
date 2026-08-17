using EvMigration.Core.Models;

namespace EvMigration.Core.Transform;

public sealed class StorionXTransformer
{
    public static string CreateSourceItemId(string archiveId, string itemId) =>
        $"ev:{archiveId}:{itemId}";

    public StorionXIngestRequest Transform(
        EvArchive archive,
        EvItem item,
        string targetArchiveId,
        RehydratedItem rehydratedItem)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetArchiveId);
        ArgumentNullException.ThrowIfNull(rehydratedItem);

        if (item.ArchiveId != archive.ArchiveId || item.ItemId != rehydratedItem.ItemId)
        {
            throw new InvalidDataException("Archive, item and rehydrated content IDs do not match.");
        }

        return new StorionXIngestRequest
        {
            TargetArchiveId = targetArchiveId,
            SourceItemId = CreateSourceItemId(archive.ArchiveId, item.ItemId),
            MessageSha256 = rehydratedItem.ContentSha256,
            Parts = rehydratedItem.Parts
                .Select(part => new StorionXContentPart
                {
                    Sha256 = part.Sha256,
                    SizeBytes = part.Data.LongLength,
                    DataBase64 = Convert.ToBase64String(part.Data)
                })
                .ToArray(),
            Metadata = new StorionXMetadata
            {
                SourceArchiveId = archive.ArchiveId,
                SourceItemId = item.ItemId,
                FolderPath = item.FolderPath,
                Subject = item.Subject,
                SentDate = item.SentDate,
                From = item.From,
                To = item.To
            },
            Retention = new StorionXRetention
            {
                Category = item.RetentionCategory,
                LegalHold = archive.LegalHold
            }
        };
    }
}
