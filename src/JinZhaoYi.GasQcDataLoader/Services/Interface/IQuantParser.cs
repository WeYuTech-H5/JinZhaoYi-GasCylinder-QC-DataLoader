using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IQuantParser
{
    Task<ParsedQuantFile> ParseAsync(QuantFileCandidate candidate, CancellationToken cancellationToken);
}
