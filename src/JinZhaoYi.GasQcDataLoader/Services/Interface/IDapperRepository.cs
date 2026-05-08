using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IDapperRepository
{
    Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken);

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

    Task<IReadOnlyList<RfOption>> GetRfOptionsAsync(CancellationToken cancellationToken);

    Task<QcDataRow?> GetRfByIdAsync(string rfId, CancellationToken cancellationToken);

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

    Task ExecuteImportAsync(ImportWriteSet writeSet, QcDataRow rf, DateTime importDate, CancellationToken cancellationToken);
}
