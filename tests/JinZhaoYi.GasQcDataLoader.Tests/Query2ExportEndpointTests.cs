using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class Query2ExportEndpointTests
{
    private const string UndeterminedMessage =
        "QC 判定包含未判定資料，已停止正式匯出與資料庫寫入。請確認 Container 與分析前／後壓力門檻設定。";

    [Fact]
    public async Task Direct_export_returns_400_without_file_or_database_writes_when_qc_is_undetermined()
    {
        var repository = new SpyRepository();
        var exporter = new SpyWorkbookExporter();
        await using var factory = new Query2ApiFactory(repository, exporter);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/exports/query2-excel",
            new Query2ExcelExportRequest
            {
                StartDate = "20260601",
                EndDate = "20260601",
                RfId = repository.Rf.Id,
                StdRawIds = repository.StdRows.Select(row => row.Id!).ToArray(),
                PortRawIds = repository.PortRows.Select(row => row.Id!).ToArray()
            });

        await AssertUndeterminedResponseAsync(response);
        exporter.ExportCallCount.Should().Be(0);
        repository.HistoryWriteCallCount.Should().Be(0);
        repository.MfgWriteCallCount.Should().Be(0);
        repository.EditLogWriteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Preview_export_returns_400_without_file_or_database_writes_when_qc_is_undetermined()
    {
        var repository = new SpyRepository();
        var exporter = new SpyWorkbookExporter();
        await using var factory = new Query2ApiFactory(repository, exporter);
        using var client = factory.CreateClient();
        var preview = CreatePreview(repository);

        using var response = await client.PostAsJsonAsync(
            "/api/exports/query2-excel/from-preview",
            new Query2PreviewExportRequest { Preview = preview });

        await AssertUndeterminedResponseAsync(response);
        exporter.ExportCallCount.Should().Be(0);
        repository.HistoryWriteCallCount.Should().Be(0);
        repository.MfgWriteCallCount.Should().Be(0);
        repository.EditLogWriteCallCount.Should().Be(0);
    }

    private static async Task AssertUndeterminedResponseAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("message").GetString().Should().Be(UndeterminedMessage);
    }

    private static Query2PreviewState CreatePreview(SpyRepository repository)
    {
        var builder = new Query2SelectionExportBuilder(
            new CalculationService(),
            NullLogger<Query2SelectionExportBuilder>.Instance);
        var previewService = new Query2PreviewService(new CalculationService());
        var rows = builder.BuildRows(repository.Rf, repository.StdRows, repository.PortRows);

        return previewService.CreatePreview(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 1),
            repository.Rf.Id!,
            repository.StdRows.Select(row => row.Id!).ToArray(),
            repository.PortRows.Select(row => row.Id!).ToArray(),
            rows,
            [],
            [],
            repository.QcSettings);
    }

    private sealed class Query2ApiFactory(
        SpyRepository repository,
        SpyWorkbookExporter exporter) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("AppLogging:File:Enabled", "false");
            builder.UseSetting("AppLogging:Seq:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDapperRepository>();
                services.AddSingleton<IDapperRepository>(repository);
                services.RemoveAll<IQuery2WorkbookExporter>();
                services.AddSingleton<IQuery2WorkbookExporter>(exporter);
            });
        }
    }

    private sealed class SpyWorkbookExporter : IQuery2WorkbookExporter
    {
        public int ExportCallCount { get; private set; }

        public Task<string?> ExportAsync(
            ImportWriteSet writeSet,
            IReadOnlyCollection<QuantFileCandidate> candidates,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]?> ExportAsync(
            string batchDate,
            IReadOnlyList<Query2ExportRow> rows,
            IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]?> ExportAsync(
            string batchDate,
            IReadOnlyList<Query2ExportRow> rows,
            IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
            QcResultSettingsDto? qcSettings,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]?> ExportAsync(
            string batchDate,
            IReadOnlyList<Query2ExportRow> rows,
            IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
            QcResultSettingsDto? qcSettings,
            IReadOnlyList<QcJudgmentSnapshot> qcJudgments,
            CancellationToken cancellationToken)
        {
            ExportCallCount++;
            return Task.FromResult<byte[]?>([1, 2, 3]);
        }
    }

    private sealed class SpyRepository : IDapperRepository
    {
        public SpyRepository()
        {
            Rf = CreateRow(
                "RF1",
                "STD",
                "RFLOT",
                900,
                new DateTime(2026, 5, 31, 8, 0, 0),
                "RF",
                10m);
            StdRows =
            [
                CreateRow("STD1", "STD", "STDLOT", 1, new DateTime(2026, 6, 1, 8, 0, 0), "STD", 100m),
                CreateRow("STD2", "STD", "STDLOT", 1, new DateTime(2026, 6, 1, 8, 15, 0), "STD", 300m)
            ];
            PortRows =
            [
                CreatePortRow("PORT1", 3, new DateTime(2026, 6, 1, 9, 0, 0), 50m),
                CreatePortRow("PORT2", 3, new DateTime(2026, 6, 1, 9, 15, 0), 150m)
            ];
        }

        public QcDataRow Rf { get; }

        public IReadOnlyList<QcDataRow> StdRows { get; }

        public IReadOnlyList<QcDataRow> PortRows { get; }

        public QcResultSettingsDto QcSettings { get; } = new();

        public int HistoryWriteCallCount { get; private set; }

        public int MfgWriteCallCount { get; private set; }

        public int EditLogWriteCallCount { get; private set; }

        public Task<QcDataRow?> GetRfByIdAsync(string rfId, CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(
                string.Equals(rfId, Rf.Id, StringComparison.OrdinalIgnoreCase) ? Rf.DeepClone() : null);

        public Task<IReadOnlyList<QcDataRow>> GetRawRowsForExportAsync(
            DateTime startDate,
            DateTime endDate,
            IReadOnlyCollection<string> selectedIds,
            CancellationToken cancellationToken)
        {
            var source = selectedIds.All(id => id.StartsWith("STD", StringComparison.OrdinalIgnoreCase))
                ? StdRows
                : PortRows;
            var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyList<QcDataRow>>(
                source.Where(row => selected.Contains(row.Id!)).Select(row => row.DeepClone()).ToArray());
        }

        public Task<IReadOnlyList<Query2DynamicAreaField>> GetQuery2DynamicAreaFieldsAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Query2DynamicAreaField>>([]);

        public Task<IReadOnlyList<Query2DynamicAreaPortValue>> GetQuery2DynamicAreaPortValuesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Query2DynamicAreaPortValue>>([]);

        public Task<QcResultSettingsDto> GetQcResultSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(QcSettings);

        public Task UpsertExcelPpbHistoryAsync(
            ExcelPpbHistorySaveRequest request,
            CancellationToken cancellationToken)
        {
            HistoryWriteCallCount++;
            return Task.CompletedTask;
        }

        public Task UpsertMfgLotQcResultsAsync(
            IReadOnlyCollection<MfgLotQcUpdate> updates,
            string? user,
            CancellationToken cancellationToken)
        {
            MfgWriteCallCount++;
            return Task.CompletedTask;
        }

        public Task InsertQuery2PreviewEditLogsAsync(
            IReadOnlyCollection<Query2PreviewEditLogRow> rows,
            CancellationToken cancellationToken)
        {
            EditLogWriteCallCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(
            IEnumerable<string> lotNos,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MfgJsonImportResult> UpsertMfgJsonLotsAsync(
            IReadOnlyCollection<MfgJsonLotRecord> records,
            string sourceFileName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsAsync(
            IReadOnlyCollection<PpbRowSelector> selectors,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<string>> GetExistingRawIdentityIdsAsync(
            IReadOnlyCollection<RawDataIdentity> identities,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(
            DateTime batchDate,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExportOption>> GetPortPpbExportOptionsAsync(
            DateTime batchDate,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PagedResponse<ExportOption>> GetPortPpbExportOptionsAsync(
            DateTime batchDate,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RfOption>> GetRfOptionsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExportOption>> GetStdRawOptionsForRfAsync(
            string? search,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PagedResponse<ExportOption>> GetStdRawOptionsForRfAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<QcDataRow?> GetStdRawByStableIdAsync(
            string stableId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RfOption> UpsertRfAsync(
            QcDataRow row,
            DateTime importDate,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Query2ExportRow>> GetQuery2ExportRowsAsync(
            DateTime batchDate,
            IReadOnlyCollection<string> selectedIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsForExportAsync(
            DateTime batchDate,
            IReadOnlyCollection<string> selectedIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Query2DynamicAreaField> UpsertQuery2DynamicAreaFieldAsync(
            Query2DynamicAreaFieldUpsertRequest request,
            string user,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisableQuery2DynamicAreaFieldAsync(
            string fieldKey,
            string user,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertQuery2DynamicAreaPortValuesAsync(
            IReadOnlyCollection<Query2DynamicAreaPortValueDto> rows,
            string user,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<QcResultSettingsDto> UpsertQcResultSettingsAsync(
            QcResultSettingsUpsertRequest request,
            string user,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PagedResponse<ExportOption>> GetExcelPpbExportOptionsAsync(
            DateTime startDate,
            DateTime endDate,
            string? search,
            Guid? exportSessionId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<QcDataRow>> GetExcelPpbRowsForCsvAsync(
            DateTime startDate,
            DateTime endDate,
            IReadOnlyCollection<string> selectedIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StdCylinderSummaryRow>> GetStdCylinderSummaryRowsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ExecuteImportAsync(
            ImportWriteSet writeSet,
            QcDataRow rf,
            DateTime importDate,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertImportErrorLogsAsync(
            IReadOnlyCollection<ImportErrorReportRow> rows,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static QcDataRow CreatePortRow(
            string id,
            int sampleNo,
            DateTime anlzTime,
            decimal acetone)
        {
            var row = CreateRow(id, "PORT 2", "PORTLOT", sampleNo, anlzTime, "PORT", acetone);
            row.Container = "0.5L_Cylinder";
            row.Description = "port 2 1050>950 #PORTLOT";
            return row;
        }

        private static QcDataRow CreateRow(
            string id,
            string port,
            string lotNo,
            int sampleNo,
            DateTime anlzTime,
            string sourceKind,
            decimal acetone)
        {
            var row = new QcDataRow
            {
                Id = id,
                Port = port,
                LotNo = lotNo,
                SampleNo = sampleNo,
                AnlzTime = anlzTime,
                SourceKind = sourceKind,
                SourceFolderName = $"{port}[{anlzTime:yyyyMMdd HHmm}]_{sampleNo:000}.D",
                DataFilename = "Quant.txt",
                DataFilepath = @"C:\GAS\Quant.txt",
                SampleName = $"{port}-{sampleNo:000}",
                SampleType = "TO14C1",
                Si0Id = sampleNo
            };
            row.Areas["Acetone"] = acetone;
            return row;
        }
    }
}
