using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IImportErrorReportExporter
{
    Task<string?> ExportAsync(
        IReadOnlyCollection<ImportErrorReportRow> rows,
        CancellationToken cancellationToken);
}
