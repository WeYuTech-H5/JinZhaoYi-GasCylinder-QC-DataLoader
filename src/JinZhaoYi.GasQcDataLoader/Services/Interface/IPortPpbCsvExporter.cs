using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IPortPpbCsvExporter
{
    Task<IReadOnlyList<string>> ExportAsync(
        IReadOnlyCollection<QcDataRow> portPpbRows,
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken);

    byte[] ExportToBytes(IReadOnlyCollection<QcDataRow> portPpbRows);

    CsvDownload ExportForDownload(IReadOnlyCollection<QcDataRow> portPpbRows, string batchDateText);
}

public sealed record CsvDownload(byte[] Content, string ContentType, string FileName);
