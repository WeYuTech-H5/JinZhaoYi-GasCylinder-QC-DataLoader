using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IQuery2WorkbookExporter
{
    Task<string?> ExportAsync(
        ImportWriteSet writeSet,
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken);
}
