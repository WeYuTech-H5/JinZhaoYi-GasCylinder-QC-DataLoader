using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IImportOrchestrator
{
    Task<ImportResult> ImportDayFolderAsync(string dayFolderPath, CancellationToken cancellationToken);

    Task<ImportResult> ImportCandidatesAsync(IReadOnlyCollection<QuantFileCandidate> candidates, CancellationToken cancellationToken);

    Task<ImportResult> ImportQuantFileAsync(QuantFileCandidate candidate, CancellationToken cancellationToken);
}
