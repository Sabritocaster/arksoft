namespace EvMigration.Core.Models;

public sealed record RehydratedPart(
    string PartId,
    string Sha256,
    byte[] Data);

public sealed record RehydratedItem(
    string ItemId,
    byte[] Content,
    string ContentSha256,
    IReadOnlyList<RehydratedPart> Parts);
