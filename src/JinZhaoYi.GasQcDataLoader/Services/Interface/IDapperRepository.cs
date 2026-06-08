using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IDapperRepository
{
    Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken);

    Task<MfgJsonImportResult> UpsertMfgJsonLotsAsync(
        IReadOnlyCollection<MfgJsonLotRecord> records,
        string sourceFileName,
        CancellationToken cancellationToken);

    Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken);

    Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsAsync(
        IReadOnlyCollection<PpbRowSelector> selectors,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> GetExistingRawIdentityIdsAsync(
        IReadOnlyCollection<RawDataIdentity> identities,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(
        DateTime batchDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportOption>> GetPortPpbExportOptionsAsync(
        DateTime batchDate,
        CancellationToken cancellationToken);

    Task<PagedResponse<ExportOption>> GetPortPpbExportOptionsAsync(
        DateTime batchDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RfOption>> GetRfOptionsAsync(CancellationToken cancellationToken);

    Task<QcDataRow?> GetRfByIdAsync(string rfId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportOption>> GetStdRawOptionsForRfAsync(
        string? search,
        int limit,
        CancellationToken cancellationToken);

    Task<PagedResponse<ExportOption>> GetStdRawOptionsForRfAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<QcDataRow?> GetStdRawByStableIdAsync(
        string stableId,
        CancellationToken cancellationToken);

    Task<RfOption> UpsertRfAsync(
        QcDataRow row,
        DateTime importDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<QcDataRow>> GetRawRowsForExportAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Query2ExportRow>> GetQuery2ExportRowsAsync(
        DateTime batchDate,
        IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsForExportAsync(
        DateTime batchDate,
        IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken);

    Task UpsertExcelPpbHistoryAsync(
        ExcelPpbHistorySaveRequest request,
        CancellationToken cancellationToken);

    Task InsertQuery2PreviewEditLogsAsync(
        IReadOnlyCollection<Query2PreviewEditLogRow> rows,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Query2DynamicAreaField>> GetQuery2DynamicAreaFieldsAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<Query2DynamicAreaField> UpsertQuery2DynamicAreaFieldAsync(
        Query2DynamicAreaFieldUpsertRequest request,
        string user,
        CancellationToken cancellationToken);

    Task DisableQuery2DynamicAreaFieldAsync(
        string fieldKey,
        string user,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Query2DynamicAreaPortValue>> GetQuery2DynamicAreaPortValuesAsync(
        CancellationToken cancellationToken);

    Task UpsertQuery2DynamicAreaPortValuesAsync(
        IReadOnlyCollection<Query2DynamicAreaPortValueDto> rows,
        string user,
        CancellationToken cancellationToken);

    Task<PagedResponse<ExportOption>> GetExcelPpbExportOptionsAsync(
        DateTime startDate,
        DateTime endDate,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<QcDataRow>> GetExcelPpbRowsForCsvAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken);

    Task ExecuteImportAsync(ImportWriteSet writeSet, QcDataRow rf, DateTime importDate, CancellationToken cancellationToken);

    Task UpsertImportErrorLogsAsync(
        IReadOnlyCollection<ImportErrorReportRow> rows,
        CancellationToken cancellationToken);
}
