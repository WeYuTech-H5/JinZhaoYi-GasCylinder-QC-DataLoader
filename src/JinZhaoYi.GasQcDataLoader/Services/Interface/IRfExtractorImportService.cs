using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IRfExtractorImportService
{
    Task<RfImportResult> ImportFromStdAsync(
        string stdRawId,
        CancellationToken cancellationToken);
}
