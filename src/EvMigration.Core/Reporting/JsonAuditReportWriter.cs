using EvMigration.Core.Models;
using EvMigration.Core.Persistence;

namespace EvMigration.Core.Reporting;

public sealed class JsonAuditReportWriter
{
    public Task WriteAsync(
        string reportPath,
        MigrationReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        return AtomicJsonFile.WriteAsync(reportPath, report, cancellationToken);
    }
}
