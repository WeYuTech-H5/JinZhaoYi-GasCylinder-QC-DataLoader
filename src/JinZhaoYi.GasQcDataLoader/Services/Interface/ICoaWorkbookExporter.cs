using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface ICoaWorkbookExporter
{
    CoaWorkbookDownload ExportLargeForDownload(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        CoaLargeTemplateType templateType);

    CoaWorkbookDownload ExportSmallForDownload(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        int cardsPerPage);
}

public sealed record CoaWorkbookDownload(byte[] Content, string ContentType, string FileName);
