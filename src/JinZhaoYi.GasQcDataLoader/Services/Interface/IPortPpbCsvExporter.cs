using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IPortPpbCsvExporter
{
    Task<IReadOnlyList<string>> ExportAsync(
        IReadOnlyCollection<QcDataRow> portPpbRows,
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken);
}
