using System.Globalization;
using System.IO.Compression;
using System.Text;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class CoaPackageExporter(
    ICoaWorkbookExporter workbookExporter,
    ISpreadsheetPdfConverter pdfConverter) : ICoaPackageExporter
{
    public Task<CoaWorkbookDownload> ExportLargePackageForDownloadAsync(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        CoaLargeTemplateType templateType,
        CancellationToken cancellationToken) =>
        ExportPackageForDownloadAsync(
            rows,
            batchDateText,
            "COA(大卡)",
            row => workbookExporter.ExportLargeForDownload([row], batchDateText, templateType),
            cancellationToken);

    public async Task<CoaWorkbookDownload> ExportSmallPackageForDownloadAsync(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        int cardsPerPage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workbook = workbookExporter.ExportSmallForDownload(rows, batchDateText, cardsPerPage);
        var pdfContent = await pdfConverter.ConvertXlsxToPdfAsync(workbook.Content, workbook.FileName, cancellationToken);
        var pdfFileName = Path.ChangeExtension(workbook.FileName, ".pdf");

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, workbook.FileName, workbook.Content);
            AddEntry(archive, pdfFileName, pdfContent);
        }

        return new CoaWorkbookDownload(
            stream.ToArray(),
            "application/zip",
            $"COA(小卡)_{batchDateText}.zip");
    }

    private async Task<CoaWorkbookDownload> ExportPackageForDownloadAsync(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        string packageName,
        Func<QcDataRow, CoaWorkbookDownload> exportWorkbook,
        CancellationToken cancellationToken)
    {
        var orderedRows = rows
            .OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.SampleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SampleNo)
            .ToArray();

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in orderedRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var workbook = exportWorkbook(row);
                var baseFileName = BuildBaseFileName(row);
                var xlsxFileName = ResolveUniqueFileName($"{baseFileName}.xlsx", usedFileNames);
                AddEntry(archive, xlsxFileName, workbook.Content);

                var pdfContent = await pdfConverter.ConvertXlsxToPdfAsync(workbook.Content, xlsxFileName, cancellationToken);
                var pdfFileName = ResolveUniqueFileName($"{baseFileName}.pdf", usedFileNames);
                AddEntry(archive, pdfFileName, pdfContent);
            }
        }

        return new CoaWorkbookDownload(
            stream.ToArray(),
            "application/zip",
            $"{packageName}_{batchDateText}.zip");
    }

    private static void AddEntry(ZipArchive archive, string fileName, byte[] content)
    {
        var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(content, 0, content.Length);
    }

    private static string BuildBaseFileName(QcDataRow row)
    {
        var certificationDate = row.AnlzTime?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "unknown-date";
        var sampleName = string.IsNullOrWhiteSpace(row.SampleName) ? "unknown-sample" : row.SampleName.Trim();
        return $"{certificationDate}_{SanitizeFileName(sampleName)}";
    }

    private static string ResolveUniqueFileName(string fileName, HashSet<string> usedFileNames)
    {
        if (usedFileNames.Add(fileName))
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName}_{index.ToString(CultureInfo.InvariantCulture)}{extension}";
            if (usedFileNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }
}
