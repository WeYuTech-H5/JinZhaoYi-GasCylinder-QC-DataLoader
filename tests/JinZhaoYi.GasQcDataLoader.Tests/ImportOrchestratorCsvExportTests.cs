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
    public async Task ImportCandidatesAsync_exports_csv_after_successful_db_commit()
    {
        var context = CreateContext(dryRun: false);

        var result = await context.Orchestrator.ImportCandidatesAsync([context.Candidate], CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        context.Repository.ExecuteImportCallCount.Should().Be(1);
        context.Repository.GetPortPpbCallCount.Should().Be(1);
        context.CsvExporter.ExportCallCount.Should().Be(1);
        result.Messages.Should().Contain(message => message.Contains("TO14C PPB CSV 已輸出", StringComparison.Ordinal));
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

    private static TestContext CreateContext(bool dryRun)
    {
        var candidate = new QuantFileCandidate(
            FullPath: @"C:\GAS\20251118\PORT 2\Quant.txt",
            DayFolderPath: @"C:\GAS\20251118",
            SourceRootPath: @"C:\GAS\20251118",
            OutputRootPath: @"C:\GAS",
            LogicalBatchDate: "20251118",
            IsArchivedInput: false,
            TopFolderName: "PORT 2",
            SourceKind: QuantSourceKind.Port,
            Port: "PORT 2",
            DataFilename: "PORT 2\\Quant.txt",
            DataFilepath: @"C:\GAS\20251118\PORT 2");
        var parsed = new ParsedQuantFile
        {
            Source = candidate,
            AcquiredAt = new DateTime(2025, 11, 18, 14, 30, 0),
            DataFile = "Quant.txt",
            DataPath = @"C:\Data",
            Sample = "Sample",
            Misc = "desc",
            LotNo = "CC-706988",
            SampleNo = 24
        };
        var portPpbRow = new QcDataRow
        {
            Id = "ppb(5900)",
            Port = "PORT 2",
            LotNo = "CC-706988",
            DataFilename = "PORT 2\\Quant.txt",
            SampleName = "TSMC-024",
            AnlzTime = parsed.AcquiredAt
        };
        var writeSet = new ImportWriteSet();
        writeSet.PortPpbRows.Add(portPpbRow);

        var repository = new FakeRepository(portPpbRow);
        var csvExporter = new FakeCsvExporter();
        var orchestrator = new ImportOrchestrator(
            new FakeScanner(candidate),
            new FakeParser(parsed),
            repository,
            new FakeWriteSetBuilder(writeSet),
            new FakeWorkbookExporter(),
            csvExporter,
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

    private sealed class FakeRepository(QcDataRow committedPpbRow) : IDapperRepository
    {
        public int ExecuteImportCallCount { get; private set; }

        public int GetPortPpbCallCount { get; private set; }

        public bool ThrowOnExecute { get; set; }

        public Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, MfgLot>>(
                new Dictionary<string, MfgLot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CC-706988"] = new() { LotNo = "CC-706988", SampleName = "TSMC-024" }
                });

        public Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(new QcDataRow { Id = "RF,ppb(5841)" });

        public Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsAsync(IReadOnlyCollection<PpbRowSelector> selectors, CancellationToken cancellationToken)
        {
            GetPortPpbCallCount++;
            return Task.FromResult<IReadOnlyList<QcDataRow>>([committedPpbRow]);
        }

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

    private sealed class FakeWorkbookExporter : IQuery2WorkbookExporter
    {
        public Task<string?> ExportAsync(
            ImportWriteSet writeSet,
            IReadOnlyCollection<QuantFileCandidate> candidates,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
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
            return Task.FromResult<IReadOnlyList<string>>([@"C:\GAS\20251118\QC\2025-11-18_TSMC-024_CC-706988_pass.csv"]);
        }
    }
}
