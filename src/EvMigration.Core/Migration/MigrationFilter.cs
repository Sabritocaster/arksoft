using EvMigration.Core.Models;

namespace EvMigration.Core.Migration;

public sealed record MigrationFilter
{
    public DateTimeOffset? FromInclusive { get; init; }

    public DateTimeOffset? ToInclusive { get; init; }

    public string? ArchiveId { get; init; }

    public string? FolderPrefix { get; init; }

    public bool IsEmpty =>
        FromInclusive is null
        && ToInclusive is null
        && string.IsNullOrWhiteSpace(ArchiveId)
        && string.IsNullOrWhiteSpace(FolderPrefix);

    public bool Matches(EvItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (FromInclusive is not null && item.SentDate < FromInclusive
            || ToInclusive is not null && item.SentDate > ToInclusive
            || !string.IsNullOrWhiteSpace(ArchiveId)
               && !string.Equals(item.ArchiveId, ArchiveId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FolderPrefix))
        {
            return true;
        }

        var prefix = FolderPrefix.Trim().TrimEnd('/', '\\');
        return string.Equals(item.FolderPath, prefix, StringComparison.OrdinalIgnoreCase)
            || item.FolderPath.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase)
            || item.FolderPath.StartsWith($"{prefix}\\", StringComparison.OrdinalIgnoreCase);
    }

    public void Validate()
    {
        if (FromInclusive is not null && ToInclusive is not null && FromInclusive > ToInclusive)
        {
            throw new ArgumentException("The from date must not be later than the to date.");
        }
    }
}
