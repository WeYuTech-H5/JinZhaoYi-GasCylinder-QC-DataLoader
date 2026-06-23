using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IStdCylinderSummaryExporter
{
    StdCylinderSummaryDownload ExportForDownload(IReadOnlyCollection<StdCylinderSummaryRow> rows);
}

public sealed record StdCylinderSummaryDownload(byte[] Content, string ContentType, string FileName);
