using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IQuery2WorkbookExporter
{
    Task<string?> ExportAsync(
        ImportWriteSet writeSet,
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken);

    Task<byte[]?> ExportAsync(
        string batchDate,
        IReadOnlyList<Query2ExportRow> rows,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
        CancellationToken cancellationToken);
}
