using System.Net;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class RfExtractorImportServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "RfExtractorImportServiceTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ImportFromStdAsync_calls_extractor_get_reads_latest_file_and_upserts_rf()
    {
        Directory.CreateDirectory(_tempRoot);
        var stdRow = CreateStdRow();
        var stableId = RawDataIdentity.FromRow(stdRow).ToStableId();
        var repository = new FakeRepository(stdRow);
        var handler = new CapturingHandler(_ =>
        {
            WriteJson("20251030001_20260512_120000.json", 5840, 90m, DateTime.UtcNow.AddMinutes(-5));
            WriteJson("20251030001_20260512_143744.json", 5841, 98.6824733960431m, DateTime.UtcNow);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(repository, handler);

        var result = await service.ImportFromStdAsync(stableId, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests.Single().ToString().Should().Be("http://localhost:5015/api/Export?LotNo=20251030001");
        result.SourceJsonPath.Should().EndWith("20251030001_20260512_143744.json");
        repository.UpsertedRow.Should().NotBeNull();
        repository.UpsertedRow!.LotNo.Should().Be("20251030001");
        repository.UpsertedRow.Id.Should().Be("RF,ppb(5841)");
        repository.UpsertedRow.Si0Id.Should().Be(5841);
        repository.UpsertedRow.Areas["Acetone"].Should().Be(98.6824733960431m);
        repository.UpsertedRow.Areas["Methlene"].Should().Be(93.2100342208844m);
        repository.UpsertedRow.Areas["Chlorobenzene-D5"].Should().Be(0m);
        repository.UpsertedRow.Areas["p-Xylene"].Should().Be(106.465032672439m);
        repository.UpsertedRow.Areas["1,3-Dichlorobenzene"].Should().Be(98.9654093275403m);
        repository.UpsertedRow.Areas["1,4-Dichlorobenzene"].Should().Be(98.3988781733391m);
        repository.UpsertedRow.Areas["1,2-Dichlorobenzene"].Should().Be(98.1140145763186m);
        repository.UpsertCallCount.Should().Be(1);
        result.Rf.Id.Should().Be("RF,ppb(5841)");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportFromStdAsync_rejects_null_values()
    {
        Directory.CreateDirectory(_tempRoot);
        var stdRow = CreateStdRow();
        var repository = new FakeRepository(stdRow);
        var handler = new CapturingHandler(_ =>
        {
            WriteJson("20251030001_20260512_143744.json", 5841, null, DateTime.UtcNow);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(repository, handler);

        var act = () => service.ImportFromStdAsync(RawDataIdentity.FromRow(stdRow).ToStableId(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("API 內 RF 資料有缺失。");
        repository.UpsertCallCount.Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private RfExtractorImportService CreateService(FakeRepository repository, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new RfExtractorImportService(
            httpClient,
            repository,
            Options.Create(new SchedulerOptions
            {
                RfExtractor = new SchedulerRfExtractorOptions
                {
                    BaseUrl = "http://localhost:5015",
                    ExportPath = "/api/Export",
                    OutputDirectory = _tempRoot
                }
            }),
            NullLogger<RfExtractorImportService>.Instance);
    }

    private void WriteJson(string fileName, int sid, decimal? acetoneValue, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(path, $$"""
            {
              "lotNo": "20251030001",
              "sID": {{sid}},
              "exportTime": "2026-05-12T14:37:44.6299271+08:00",
              "data": [
                { "seq": 1, "id": 0, "primeName": "Acetone", "value": {{FormatJsonDecimal(acetoneValue)}} },
                { "seq": 3, "id": 0, "primeName": "Methylene ChlorcIde", "value": 93.2100342208844 },
                { "seq": 12, "id": 0, "primeName": "Chlorobenzene-D5", "value": 0 },
                { "seq": 30, "id": 0, "primeName": "p-Xylene", "value": 106.465032672439 },
                { "seq": 35, "id": 0, "primeName": "1,3-Dichlorobenzene", "value": 98.9654093275403 },
                { "seq": 36, "id": 0, "primeName": "1,4-Dichlorobenzene", "value": 98.3988781733391 },
                { "seq": 37, "id": 0, "primeName": "1,2-Dichlorobenzene", "value": 98.1140145763186 }
              ]
            }
            """);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    private static string FormatJsonDecimal(decimal? value) =>
        value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";

    private static QcDataRow CreateStdRow() =>
        new()
        {
            Id = "20260512904",
            LotNo = "20251030001",
            Port = "STD",
            SourceKind = "Std",
            SourceFolderName = "STD[20260512 1437]_904.D",
            Si0Id = 5841,
            SampleNo = 904,
            SampleName = "RF-904",
            AnlzTime = new DateTime(2026, 5, 12, 14, 37, 44),
            Container = "1L_Cylinder",
            Description = "STD #20251030001",
            DataFilename = @"STD[20260512 1437]_904.D\Quant.txt",
            DataFilepath = @"D:\data\STD[20260512 1437]_904.D"
        };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class FakeRepository(QcDataRow stdRow) : IDapperRepository
    {
        public int UpsertCallCount { get; private set; }

        public QcDataRow? UpsertedRow { get; private set; }

        public Task<QcDataRow?> GetStdRawByStableIdAsync(string stableId, CancellationToken cancellationToken)
        {
            var rowStableId = RawDataIdentity.FromRow(stdRow).ToStableId();
            return Task.FromResult(string.Equals(stableId, rowStableId, StringComparison.OrdinalIgnoreCase)
                ? stdRow
                : null);
        }

        public Task<RfOption> UpsertRfAsync(QcDataRow row, DateTime importDate, CancellationToken cancellationToken)
        {
            UpsertCallCount++;
            UpsertedRow = row;
            return Task.FromResult(new RfOption(row.Id, row.AnlzTime, row.Si0Id, row.SampleName, row.SampleNo, row.Description));
        }

        public Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, MfgLot>>(new Dictionary<string, MfgLot>(StringComparer.OrdinalIgnoreCase));

        public Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(null);

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

        public Task<QcDataRow?> GetRfByIdAsync(string rfId, CancellationToken cancellationToken) =>
            Task.FromResult<QcDataRow?>(null);

        public Task<IReadOnlyList<ExportOption>> GetStdRawOptionsForRfAsync(string? search, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportOption>>([]);

        public Task<PagedResponse<ExportOption>> GetStdRawOptionsForRfAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResponse<ExportOption>(page, pageSize, 0, []));

        public Task<IReadOnlyList<QcDataRow>> GetRawRowsForExportAsync(DateTime startDate, DateTime endDate, IReadOnlyCollection<string> selectedIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QcDataRow>>([]);

        public Task<IReadOnlyList<Query2ExportRow>> GetQuery2ExportRowsAsync(DateTime batchDate, IReadOnlyCollection<string> selectedIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Query2ExportRow>>([]);

        public Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsForExportAsync(DateTime batchDate, IReadOnlyCollection<string> selectedIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QcDataRow>>([]);

        public Task ExecuteImportAsync(ImportWriteSet writeSet, QcDataRow rf, DateTime importDate, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpsertImportErrorLogsAsync(IReadOnlyCollection<ImportErrorReportRow> rows, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
