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

public interface ISpreadsheetPdfConverter
{
    Task<byte[]> ConvertXlsxToPdfAsync(
        byte[] workbookContent,
        string workbookFileName,
        CancellationToken cancellationToken);
}

public interface ICoaPackageExporter
{
    Task<CoaWorkbookDownload> ExportLargePackageForDownloadAsync(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        CoaLargeTemplateType templateType,
        CancellationToken cancellationToken);

    Task<CoaWorkbookDownload> ExportSmallPackageForDownloadAsync(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        int cardsPerPage,
        CancellationToken cancellationToken);
}
