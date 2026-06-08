using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class MfgJsonImportServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "mfg-json-import-tests", Guid.NewGuid().ToString("N"));

    public MfgJsonImportServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ProcessFile_skips_same_file_name_and_hash_after_success()
    {
        var path = Path.Combine(_tempDirectory, "MFGExport_20260522_093603.json");
        await File.WriteAllTextAsync(path, """[{ "si0_id": 6377, "LotNo": "20260508002" }]""");
        var repository = new FakeRepository();
        var stateStore = new InMemoryStateStore();
        var service = CreateService(repository, stateStore);

        await service.ProcessFileAsync(path, CancellationToken.None);
        await service.ProcessFileAsync(path, CancellationToken.None);

        repository.UpsertCallCount.Should().Be(1);
        stateStore.State.Files["MFGExport_20260522_093603.json"].Status.Should().Be("Succeeded");
        stateStore.State.Lots["20260508002"].Si0Id.Should().Be("6377");
    }

    [Fact]
    public async Task ProcessFile_reimports_same_file_name_when_content_hash_changes()
    {
        var path = Path.Combine(_tempDirectory, "MFGExport_20260522_093603.json");
        var repository = new FakeRepository();
        var stateStore = new InMemoryStateStore();
        var service = CreateService(repository, stateStore);

        await File.WriteAllTextAsync(path, """[{ "si0_id": 6377, "LotNo": "20260508002" }]""");
        await service.ProcessFileAsync(path, CancellationToken.None);
        await File.WriteAllTextAsync(path, """[{ "si0_id": 6378, "LotNo": "20260508003" }]""");
        await service.ProcessFileAsync(path, CancellationToken.None);

        repository.UpsertCallCount.Should().Be(2);
        stateStore.State.Lots.Should().ContainKey("20260508003");
    }

    [Fact]
    public async Task ProcessFile_records_failed_state_for_invalid_json()
    {
        var path = Path.Combine(_tempDirectory, "MFGExport_20260522_093603.json");
        await File.WriteAllTextAsync(path, """{ "si0_id": 6377 }""");
        var stateStore = new InMemoryStateStore();
        var service = CreateService(new FakeRepository(), stateStore);

        var act = () => service.ProcessFileAsync(path, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        stateStore.State.Files["MFGExport_20260522_093603.json"].Status.Should().Be("Failed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static MfgJsonImportService CreateService(FakeRepository repository, InMemoryStateStore stateStore) =>
        new(
            new MfgJsonParser(),
            stateStore,
            repository,
            NullLogger<MfgJsonImportService>.Instance);

    private sealed class InMemoryStateStore : IMfgJsonImportStateStore
    {
        public MfgJsonImportState State { get; private set; } = new();

        public Task<MfgJsonImportState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);

        public Task SaveAsync(MfgJsonImportState state, CancellationToken cancellationToken)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository : IDapperRepository
    {
        public int UpsertCallCount { get; private set; }

        public Task<MfgJsonImportResult> UpsertMfgJsonLotsAsync(
            IReadOnlyCollection<MfgJsonLotRecord> records,
            string sourceFileName,
            CancellationToken cancellationToken)
        {
            UpsertCallCount++;
            return Task.FromResult(new MfgJsonImportResult
            {
                InsertedCount = records.Count,
                Lots = records
                    .Select(row => new MfgJsonImportedLot { LotNo = row.LotNo, Si0Id = row.Si0Id, Action = "Inserted" })
                    .ToArray()
            });
        }

        public Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, MfgLot>>(new Dictionary<string, MfgLot>(StringComparer.OrdinalIgnoreCase));

        public Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken) => Task.FromResult<QcDataRow?>(null);

        public Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsAsync(IReadOnlyCollection<PpbRowSelector> selectors, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QcDataRow>>([]);

        public Task<IReadOnlySet<string>> GetExistingRawIdentityIdsAsync(IReadOnlyCollection<RawDataIdentity> identities, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

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

        public Task<QcDataRow?> GetRfByIdAsync(string rfId, CancellationToken cancellationToken) => Task.FromResult<QcDataRow?>(null);

        public Task<IReadOnlyList<ExportOption>> GetStdRawOptionsForRfAsync(string? search, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<PagedResponse<ExportOption>> GetStdRawOptionsForRfAsync(string? search, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResponse<ExportOption>(page, pageSize, 0, []));

        public Task<QcDataRow?> GetStdRawByStableIdAsync(string stableId, CancellationToken cancellationToken) => Task.FromResult<QcDataRow?>(null);

        public Task<RfOption> UpsertRfAsync(QcDataRow row, DateTime importDate, CancellationToken cancellationToken) =>
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

        public Task UpsertExcelPpbHistoryAsync(ExcelPpbHistorySaveRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InsertQuery2PreviewEditLogsAsync(IReadOnlyCollection<Query2PreviewEditLogRow> rows, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<Query2DynamicAreaField>> GetQuery2DynamicAreaFieldsAsync(bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Query2DynamicAreaField>>([]);

        public Task<Query2DynamicAreaField> UpsertQuery2DynamicAreaFieldAsync(Query2DynamicAreaFieldUpsertRequest request, string user, CancellationToken cancellationToken) =>
            Task.FromResult(Query2DynamicAreaRules.NormalizeFieldRequest(request, []));

        public Task DisableQuery2DynamicAreaFieldAsync(string fieldKey, string user, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Query2DynamicAreaPortValue>> GetQuery2DynamicAreaPortValuesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Query2DynamicAreaPortValue>>([]);

        public Task UpsertQuery2DynamicAreaPortValuesAsync(IReadOnlyCollection<Query2DynamicAreaPortValueDto> rows, string user, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PagedResponse<ExportOption>> GetExcelPpbExportOptionsAsync(
            DateTime startDate,
            DateTime endDate,
            string? search,
            Guid? exportSessionId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResponse<ExportOption>(page, pageSize, 0, []));

        public Task<IReadOnlyList<QcDataRow>> GetExcelPpbRowsForCsvAsync(
            DateTime startDate,
            DateTime endDate,
            IReadOnlyCollection<string> selectedIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QcDataRow>>([]);

        public Task ExecuteImportAsync(ImportWriteSet writeSet, QcDataRow rf, DateTime importDate, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertImportErrorLogsAsync(IReadOnlyCollection<ImportErrorReportRow> rows, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
