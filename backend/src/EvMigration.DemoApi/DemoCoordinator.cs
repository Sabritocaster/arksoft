using EvMigration.Core.Discovery;
using EvMigration.Core.Ingestion;
using EvMigration.Core.Migration;
using EvMigration.Core.Mock;
using EvMigration.Core.Models;
using EvMigration.Core.Persistence;
using EvMigration.Core.Reconciliation;
using EvMigration.Core.Reporting;
using Microsoft.Extensions.Options;

namespace EvMigration.DemoApi;

public sealed class DemoCoordinator : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DemoOptions _options;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _stateGate = new();
    private MigrationReport? _lastMigration;
    private ReconciliationReport? _lastReconciliation;

    public DemoCoordinator(
        IHttpClientFactory httpClientFactory,
        IOptions<DemoOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    private string DataRoot => Path.GetFullPath(_options.DataRoot);

    private string SourceDirectory => Path.Combine(DataRoot, "source");

    private string SourcePath => Path.Combine(SourceDirectory, "ev-data.json");

    private string OutputDirectory => Path.Combine(DataRoot, "output");

    private string CheckpointPath => Path.Combine(OutputDirectory, "checkpoint.json");

    private string MigrationReportPath => Path.Combine(OutputDirectory, "migration-report.json");

    private string DryRunReportPath => Path.Combine(OutputDirectory, "dry-run-report.json");

    private string ReconciliationReportPath => Path.Combine(OutputDirectory, "reconciliation-report.json");

    public bool IsBusy => _operationLock.CurrentCount == 0;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(OutputDirectory);
        if (!File.Exists(SourcePath))
        {
            await new MockEvDataGenerator().GenerateAsync(SourceDirectory, cancellationToken);
        }
    }

    public DemoStatusResponse GetStatus()
    {
        lock (_stateGate)
        {
            return new DemoStatusResponse(IsBusy, _lastMigration, _lastReconciliation);
        }
    }

    public async Task<ArchiveDiscoveryReport> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var (dataSet, targetMap) = await LoadInputsAsync(cancellationToken);
        return new ArchiveDiscoveryService().Discover(dataSet, targetMap);
    }

    public async Task<MigrationReport> MigrateAsync(
        DemoMigrationRequest request,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        await EnterOperationAsync(cancellationToken);

        try
        {
            var (dataSet, targetMap) = await LoadInputsAsync(cancellationToken);
            var discovery = new ArchiveDiscoveryService().Discover(dataSet, targetMap);
            using var checkpointStore = request.UseCheckpoint && !dryRun
                ? new JsonCheckpointStore(CheckpointPath)
                : null;
            var report = await new MigrationEngine().MigrateAsync(
                dataSet,
                discovery,
                SourceDirectory,
                new StorionXHttpClient(_httpClientFactory.CreateClient("storionx")),
                new MigrationOptions
                {
                    WorkerCount = request.Workers,
                    DryRun = dryRun,
                    Filter = CreateFilter(request)
                },
                checkpointStore,
                cancellationToken);

            await new JsonAuditReportWriter().WriteAsync(
                dryRun ? DryRunReportPath : MigrationReportPath,
                report,
                cancellationToken);

            lock (_stateGate)
            {
                _lastMigration = report;
                if (!dryRun)
                {
                    _lastReconciliation = null;
                }
            }

            return report;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        await EnterOperationAsync(cancellationToken);

        try
        {
            var (dataSet, targetMap) = await LoadInputsAsync(cancellationToken);
            var discovery = new ArchiveDiscoveryService().Discover(dataSet, targetMap);
            var targetState = await new StorionXHttpClient(
                    _httpClientFactory.CreateClient("storionx"))
                .GetStateAsync(cancellationToken);
            var report = await new ReconciliationService().ReconcileAsync(
                dataSet,
                discovery,
                SourceDirectory,
                targetState,
                cancellationToken: cancellationToken);

            await new JsonAuditReportWriter().WriteAsync(
                ReconciliationReportPath,
                report,
                cancellationToken);

            lock (_stateGate)
            {
                _lastReconciliation = report;
            }

            return report;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public Task<StorionXStateSnapshot> GetTargetStateAsync(
        CancellationToken cancellationToken = default) =>
        new StorionXHttpClient(_httpClientFactory.CreateClient("storionx"))
            .GetStateAsync(cancellationToken);

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await EnterOperationAsync(cancellationToken);

        try
        {
            using var response = await _httpClientFactory
                .CreateClient("storionx")
                .PostAsync("reset", null, cancellationToken);
            response.EnsureSuccessStatusCode();

            foreach (var path in new[]
                     {
                         CheckpointPath,
                         MigrationReportPath,
                         DryRunReportPath,
                         ReconciliationReportPath
                     })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            lock (_stateGate)
            {
                _lastMigration = null;
                _lastReconciliation = null;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Dispose() => _operationLock.Dispose();

    private async Task<(EvDataSet DataSet, TargetArchiveMap TargetMap)> LoadInputsAsync(
        CancellationToken cancellationToken)
    {
        var dataSet = await new EvDataSetLoader().LoadAsync(SourcePath, cancellationToken);
        var targetMap = await new TargetArchiveMapLoader().LoadAsync(
            Path.GetFullPath(_options.MappingPath),
            cancellationToken);
        return (dataSet, targetMap);
    }

    private async Task EnterOperationAsync(CancellationToken cancellationToken)
    {
        if (!await _operationLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new DemoBusyException();
        }
    }

    private static MigrationFilter CreateFilter(DemoMigrationRequest request)
    {
        var filter = new MigrationFilter
        {
            FromInclusive = request.From,
            ToInclusive = request.To,
            ArchiveId = NullIfWhiteSpace(request.ArchiveId),
            FolderPrefix = NullIfWhiteSpace(request.Folder)
        };
        filter.Validate();
        return filter;
    }

    private static void ValidateRequest(DemoMigrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Workers is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Workers must be between 1 and 8.");
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
