using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IProcessedQuantFileStore
{
    Task<IReadOnlyList<QuantFileCandidate>> FilterUnprocessedAsync(
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken);
}
