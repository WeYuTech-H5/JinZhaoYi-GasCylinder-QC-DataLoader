using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class GasQcImportJobTests
{
    [Fact]
    public async Task ExecuteAsync_writes_import_error_rows_to_db_when_real_run_fails()
    {
        using var context = CreateContext(dryRun: false);

        await context.Job.ExecuteAsync(CancellationToken.None);

        context.ErrorReportExporter.ExportCallCount.Should().Be(1);
        context.Repository.UpsertImportErrorLogsCallCount.Should().Be(1);
        context.Repository.UpsertedRows.Should().ContainSingle();

        var row = context.Repository.UpsertedRows.Single();
        row.LotNo.Should().Be("20260507005");
        row.QuantPath.Should().Be(context.Candidate.FullPath);
        row.DataFolderPath.Should().Be(context.Candidate.DataFilepath);
        row.ErrorType.Should().Be("ImportValidationFailed");
        row.Message.Should().Be("ZZ_NF_GAS_MFG_LOT 查無 LOT：20260507005。");
        row.SuggestedAction.Should().Contain("ZZ_NF_GAS_MFG_LOT");
    }

    [Fact]
    public async Task ExecuteAsync_exports_error_report_but_skips_db_error_log_in_dry_run()
    {
        using var context = CreateContext(dryRun: true);

        await context.Job.ExecuteAsync(CancellationToken.None);

        context.ErrorReportExporter.ExportCallCount.Should().Be(1);
        context.Repository.UpsertImportErrorLogsCallCount.Should().Be(0);
    }

    private static TestContext CreateContext(bool dryRun)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "GasQcImportJobTests", Guid.NewGuid().ToString("N"));
        var candidate = CreateCandidate(tempRoot);
        var repository = new FakeRepository();
        var errorReportExporter = new FakeImportErrorReportExporter();
        var job = new GasQcImportJob(
            NullLogger<GasQcImportJob>.Instance,
            new FakeScanner(candidate),
            new FakeOrchestrator("ZZ_NF_GAS_MFG_LOT 查無 LOT：20260507005。"),
            new FakeProcessedQuantFileStore(),
            errorReportExporter,
            repository,
            Options.Create(new SchedulerOptions
            {
                WatchRoot = tempRoot,
                TargetMode = SchedulerTargetMode.AllNewStableFiles,
                DryRun = dryRun
            }));

        return new TestContext(tempRoot, candidate, job, repository, errorReportExporter);
    }

    private static QuantFileCandidate CreateCandidate(string tempRoot)
    {
        var dayFolderPath = Path.Combine(tempRoot, "20260507");
        var sourceRootPath = Path.Combine(dayFolderPath, "PORT2");
        var dataFolderPath = Path.Combine(sourceRootPath, "PORT 2[20260507 1010]_005.D");
        Directory.CreateDirectory(dataFolderPath);

        var quantPath = Path.Combine(dataFolderPath, "Quant.txt");
        File.WriteAllLines(quantPath,
        [
            "Data File\tPORT 2[20260507 1010]_005.D",
            "Misc\tQC sample #20260507005"
        ]);

        return new QuantFileCandidate(
            FullPath: quantPath,
            DayFolderPath: dayFolderPath,
            SourceRootPath: sourceRootPath,
            OutputRootPath: dayFolderPath,
            LogicalBatchDate: "20260507",
            IsArchivedInput: false,
            TopFolderName: "PORT2",
            SourceKind: QuantSourceKind.Port,
            Port: "PORT 2",
            DataFilename: @"PORT 2[20260507 1010]_005.D\Quant.txt",
            DataFilepath: dataFolderPath);
    }

    private sealed record TestContext(
        string TempRoot,
        QuantFileCandidate Candidate,
        GasQcImportJob Job,
        FakeRepository Repository,
        FakeImportErrorReportExporter ErrorReportExporter) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
    }

    private sealed class FakeScanner(QuantFileCandidate candidate) : IGasFolderScanner
    {
        public IReadOnlyList<string> FindStableDayFolders(string watchRoot, TimeSpan stableAge) => [];

        public IReadOnlyList<QuantFileCandidate> FindStableQuantFiles(string watchRoot, TimeSpan stableAge) => [candidate];

        public IReadOnlyList<QuantFileCandidate> FindQuantFiles(string dayFolderPath) => [candidate];
    }

    private sealed class FakeOrchestrator(string message) : IImportOrchestrator
    {
        public Task<ImportResult> ImportDayFolderAsync(string dayFolderPath, CancellationToken cancellationToken) =>
            Task.FromResult(ImportResult.Failed(dayFolderPath, 1, message));

        public Task<ImportResult> ImportCandidatesAsync(IReadOnlyCollection<QuantFileCandidate> candidates, CancellationToken cancellationToken) =>
            Task.FromResult(ImportResult.Failed(candidates.First().DayFolderPath, candidates.Count, message));

        public Task<ImportResult> ImportQuantFileAsync(QuantFileCandidate candidate, CancellationToken cancellationToken) =>
            Task.FromResult(ImportResult.Failed(candidate.DayFolderPath, 1, message));
    }

    private sealed class FakeProcessedQuantFileStore : IProcessedQuantFileStore
    {
        public Task<IReadOnlyList<QuantFileCandidate>> FilterUnprocessedAsync(
            IReadOnlyCollection<QuantFileCandidate> candidates,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QuantFileCandidate>>(candidates.ToArray());

        public Task MarkProcessedAsync(
            IReadOnlyCollection<QuantFileCandidate> candidates,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeImportErrorReportExporter : IImportErrorReportExporter
    {
        public int ExportCallCount { get; private set; }

        public IReadOnlyList<ImportErrorReportRow> ExportedRows { get; private set; } = [];

        public Task<string?> ExportAsync(
            IReadOnlyCollection<ImportErrorReportRow> rows,
            CancellationToken cancellationToken)
        {
            ExportCallCount++;
            ExportedRows = rows.ToArray();
            return Task.FromResult<string?>("error-report.xlsx");
        }
    }

    private sealed class FakeRepository : IDapperRepository
    {
        public int UpsertImportErrorLogsCallCount { get; private set; }

        public IReadOnlyList<ImportErrorReportRow> UpsertedRows { get; private set; } = [];

        public Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, MfgLot>>(new Dictionary<string, MfgLot>(StringComparer.OrdinalIgnoreCase));

        public Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(null);

        public Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsAsync(IReadOnlyCollection<PpbRowSelector> selectors, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QcDataRow>>([]);

        public Task<IReadOnlySet<string>> GetExistingRawIdentityIdsAsync(
            IReadOnlyCollection<RawDataIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(DateTime batchDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<IReadOnlyList<ExportOption>> GetPortPpbExportOptionsAsync(DateTime batchDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<IReadOnlyList<RfOption>> GetRfOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RfOption>>([]);

        public Task<QcDataRow?> GetRfByIdAsync(string rfId, CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(null);

        public Task<IReadOnlyList<QcDataRow>> GetRawRowsForExportAsync(
            DateTime startDate,
            DateTime endDate,
            IReadOnlyCollection<string> selectedIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QcDataRow>>([]);

        public Task<IReadOnlyList<Query2ExportRow>> GetQuery2ExportRowsAsync(
            DateTime batchDate,
            IReadOnlyCollection<string> selectedIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Query2ExportRow>>([]);

        public Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsForExportAsync(
            DateTime batchDate,
            IReadOnlyCollection<string> selectedIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QcDataRow>>([]);

        public Task ExecuteImportAsync(ImportWriteSet writeSet, QcDataRow rf, DateTime importDate, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpsertImportErrorLogsAsync(
            IReadOnlyCollection<ImportErrorReportRow> rows,
            CancellationToken cancellationToken)
        {
            UpsertImportErrorLogsCallCount++;
            UpsertedRows = rows.ToArray();
            return Task.CompletedTask;
        }
    }
}
