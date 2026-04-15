using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IDapperRepository
{
    Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken);

    Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken);

    Task ExecuteImportAsync(ImportWriteSet writeSet, QcDataRow rf, DateTime importDate, CancellationToken cancellationToken);
}
