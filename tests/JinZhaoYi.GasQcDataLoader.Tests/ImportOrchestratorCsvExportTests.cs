using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class ImportOrchestratorCsvExportTests
{
    [Fact]
    public async Task ImportCandidatesAsync_does_not_export_csv_after_successful_db_commit()
    {
        var context = CreateContext(dryRun: false);

        var result = await context.Orchestrator.ImportCandidatesAsync([context.Candidate], CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        context.Repository.ExecuteImportCallCount.Should().Be(1);
        context.Repository.GetPortPpbCallCount.Should().Be(0);
        context.CsvExporter.ExportCallCount.Should().Be(0);
        result.Messages.Should().NotContain(message => message.Contains("TO14C PPB CSV", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportCandidatesAsync_does_not_export_csv_in_dry_run()
    {
        var context = CreateContext(dryRun: true);

        var result = await context.Orchestrator.ImportCandidatesAsync([context.Candidate], CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        context.Repository.ExecuteImportCallCount.Should().Be(0);
        context.Repository.GetPortPpbCallCount.Should().Be(0);
        context.CsvExporter.ExportCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportCandidatesAsync_does_not_export_csv_when_db_commit_fails()
    {
        var context = CreateContext(dryRun: false);
        context.Repository.ThrowOnExecute = true;

        var act = () => context.Orchestrator.ImportCandidatesAsync([context.Candidate], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        context.Repository.GetPortPpbCallCount.Should().Be(0);
        context.CsvExporter.ExportCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportCandidatesAsync_skips_duplicate_raw_identity_before_db_commit()
    {
        var context = CreateContext(dryRun: false);
        context.Repository.ExistingRawIdentityIds.Add(new RawDataIdentity(
            "20251118001",
            "PORT 2",
            24,
            "TSMC-024",
            new DateTime(2025, 11, 18, 14, 30, 0)).ToStableId());

        var result = await context.Orchestrator.ImportCandidatesAsync([context.Candidate], CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        context.Repository.ExecuteImportCallCount.Should().Be(0);
        result.PlannedRowCount.Should().Be(0);
        result.Messages.Should().Contain(message => message.Contains("DB", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportCandidatesAsync_skips_non_11_digit_lot()
    {
        var context = CreateContext(dryRun: false, lotNo: "63265");

        var result = await context.Orchestrator.ImportCandidatesAsync([context.Candidate], CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        context.Repository.ExecuteImportCallCount.Should().Be(0);
        result.PlannedRowCount.Should().Be(0);
        result.Messages.Should().Contain(message => message.Contains("非 11 位數字 LOT", StringComparison.Ordinal));
    }

    private static TestContext CreateContext(bool dryRun, string lotNo = "20251118001")
    {
        var candidate = new QuantFileCandidate(
            FullPath: @"C:\GAS\20251118\PORT 2\PORT 2[20251118 1430]_024.D\Quant.txt",
            DayFolderPath: @"C:\GAS\20251118",
            SourceRootPath: @"C:\GAS\20251118",
            OutputRootPath: @"C:\GAS",
            LogicalBatchDate: "20251118",
            IsArchivedInput: false,
            TopFolderName: "PORT 2",
            SourceKind: QuantSourceKind.Port,
            Port: "PORT 2",
            DataFilename: @"PORT 2[20251118 1430]_024.D\Quant.txt",
            DataFilepath: @"C:\GAS\20251118\PORT 2\PORT 2[20251118 1430]_024.D");
        var parsed = new ParsedQuantFile
        {
            Source = candidate,
            AcquiredAt = new DateTime(2025, 11, 18, 14, 30, 0),
            DataFile = "Quant.txt",
            DataPath = @"C:\Data",
            Sample = "Sample",
            Misc = $"desc #{lotNo}",
            LotNo = lotNo,
            SampleNo = 24
        };
        var portPpbRow = new QcDataRow
        {
            Id = "ppb(5900)",
            Port = "PORT 2",
            LotNo = lotNo,
            DataFilename = candidate.DataFilename,
            SampleName = "TSMC-024",
            AnlzTime = parsed.AcquiredAt
        };
        var writeSet = new ImportWriteSet();
        writeSet.PortPpbRows.Add(portPpbRow);

        var repository = new FakeRepository(portPpbRow, lotNo);
        var csvExporter = new FakeCsvExporter();
        var orchestrator = new ImportOrchestrator(
            new FakeScanner(candidate),
            new FakeParser(parsed),
            repository,
            new FakeWriteSetBuilder(writeSet),
            Options.Create(new SchedulerOptions
            {
                DryRun = dryRun,
                CsvExport = new SchedulerCsvExportOptions { Enabled = true }
            }),
            NullLogger<ImportOrchestrator>.Instance);

        return new TestContext(orchestrator, candidate, repository, csvExporter);
    }

    private sealed record TestContext(
        ImportOrchestrator Orchestrator,
        QuantFileCandidate Candidate,
        FakeRepository Repository,
        FakeCsvExporter CsvExporter);

    private sealed class FakeScanner(QuantFileCandidate candidate) : IGasFolderScanner
    {
        public IReadOnlyList<string> FindStableDayFolders(string watchRoot, TimeSpan stableAge) => [];

        public IReadOnlyList<QuantFileCandidate> FindStableQuantFiles(string watchRoot, TimeSpan stableAge) => [candidate];

        public IReadOnlyList<QuantFileCandidate> FindQuantFiles(string dayFolderPath) => [candidate];
    }

    private sealed class FakeParser(ParsedQuantFile parsed) : IQuantParser
    {
        public Task<ParsedQuantFile> ParseAsync(QuantFileCandidate candidate, CancellationToken cancellationToken) =>
            Task.FromResult(parsed);
    }

    private sealed class FakeRepository(QcDataRow committedPpbRow, string lotNo) : IDapperRepository
    {
        public HashSet<string> ExistingRawIdentityIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int ExecuteImportCallCount { get; private set; }

        public int GetPortPpbCallCount { get; private set; }

        public bool ThrowOnExecute { get; set; }

        public Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, MfgLot>>(
                new Dictionary<string, MfgLot>(StringComparer.OrdinalIgnoreCase)
                {
                    [lotNo] = new() { LotNo = lotNo, SampleName = "TSMC-024" }
                });

        public Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(new QcDataRow { Id = "RF,ppb(5841)" });

        public Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsAsync(IReadOnlyCollection<PpbRowSelector> selectors, CancellationToken cancellationToken)
        {
            GetPortPpbCallCount++;
            return Task.FromResult<IReadOnlyList<QcDataRow>>([committedPpbRow]);
        }

        public Task<IReadOnlySet<string>> GetExistingRawIdentityIdsAsync(
            IReadOnlyCollection<RawDataIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(ExistingRawIdentityIds);

        public Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(DateTime batchDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<IReadOnlyList<ExportOption>> GetPortPpbExportOptionsAsync(DateTime batchDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<PagedResponse<ExportOption>> GetPortPpbExportOptionsAsync(DateTime batchDate, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResponse<ExportOption>(page, pageSize, 0, []));

        public Task<IReadOnlyList<RfOption>> GetRfOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RfOption>>([]);

        public Task<QcDataRow?> GetRfByIdAsync(string rfId, CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(null);

        public Task<IReadOnlyList<ExportOption>> GetStdRawOptionsForRfAsync(
            string? search,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<PagedResponse<ExportOption>> GetStdRawOptionsForRfAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResponse<ExportOption>(page, pageSize, 0, []));

        public Task<QcDataRow?> GetStdRawByStableIdAsync(
            string stableId,
            CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(null);

        public Task<RfOption> UpsertRfAsync(
            QcDataRow row,
            DateTime importDate,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RfOption(row.Id, row.AnlzTime, row.Si0Id, row.SampleName, row.SampleNo, row.Description));

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

        public Task UpsertImportErrorLogsAsync(
            IReadOnlyCollection<ImportErrorReportRow> rows,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ExecuteImportAsync(ImportWriteSet writeSet, QcDataRow rf, DateTime importDate, CancellationToken cancellationToken)
        {
            ExecuteImportCallCount++;
            if (ThrowOnExecute)
            {
                throw new InvalidOperationException("commit failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeWriteSetBuilder(ImportWriteSet writeSet) : IImportWriteSetBuilder
    {
        public ImportWriteSet BuildSingleFileWriteSet(ParsedQuantFile parsed, MfgLot lot) => writeSet;

        public ImportWriteSet BuildWriteSet(
            IReadOnlyCollection<ParsedQuantFile> parsedFiles,
            IReadOnlyDictionary<string, MfgLot> lots,
            QcDataRow rf) => writeSet;
    }

    private sealed class FakeCsvExporter : IPortPpbCsvExporter
    {
        public int ExportCallCount { get; private set; }

        public Task<IReadOnlyList<string>> ExportAsync(
            IReadOnlyCollection<QcDataRow> portPpbRows,
            IReadOnlyCollection<QuantFileCandidate> candidates,
            CancellationToken cancellationToken)
        {
            ExportCallCount++;
            return Task.FromResult<IReadOnlyList<string>>([@"C:\GAS\20251118\QC\2025-11-18_TSMC-024_20251118001_pass.csv"]);
        }

        public byte[] ExportToBytes(IReadOnlyCollection<QcDataRow> portPpbRows) => [];

        public CsvDownload ExportForDownload(IReadOnlyCollection<QcDataRow> portPpbRows, string batchDateText) =>
            new([], "text/csv; charset=utf-8", $"TO14C_PPB[{batchDateText}].csv");
    }
}
