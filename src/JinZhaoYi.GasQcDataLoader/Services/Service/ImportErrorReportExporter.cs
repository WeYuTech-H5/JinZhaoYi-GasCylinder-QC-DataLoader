using ClosedXML.Excel;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class ImportErrorReportExporter(
    IOptions<SchedulerOptions> options,
    ILogger<ImportErrorReportExporter> logger) : IImportErrorReportExporter
{
    private static readonly string[] Headers =
    [
        "發生時間",
        "批次日期",
        "PORT",
        "來源資料夾",
        "LOT",
        "Quant檔案路徑",
        "資料資料夾路徑",
        "錯誤類型",
        "錯誤訊息",
        "建議處理方式"
    ];

    private readonly SchedulerOptions _options = options.Value;

    public async Task<string?> ExportAsync(
        IReadOnlyCollection<ImportErrorReportRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var outputDirectory = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var outputPath = ResolveUniquePath(Path.Combine(outputDirectory, $"ImportErrors[{timestamp}].xlsx"));

        await Task.Run(() => ExportWorkbook(outputPath, rows), cancellationToken);

        logger.LogWarning("Import error report exported to {OutputPath}. Count={Count}.", outputPath, rows.Count);
        return outputPath;
    }

    private string ResolveOutputDirectory()
    {
        var root = string.IsNullOrWhiteSpace(_options.ExportRoot)
            ? _options.WatchRoot
            : _options.ExportRoot;

        return Path.Combine(root, "QC");
    }

    private static void ExportWorkbook(string outputPath, IReadOnlyCollection<ImportErrorReportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("ImportErrors");

        for (var column = 0; column < Headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = Headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F4B084");
        }

        var rowNumber = 2;
        foreach (var row in rows
            .OrderBy(row => row.LogicalBatchDate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Port, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.QuantPath, StringComparer.OrdinalIgnoreCase))
        {
            worksheet.Cell(rowNumber, 1).Value = row.OccurredAt.LocalDateTime;
            worksheet.Cell(rowNumber, 2).Value = row.LogicalBatchDate;
            worksheet.Cell(rowNumber, 3).Value = row.Port;
            worksheet.Cell(rowNumber, 4).Value = row.TopFolderName;
            worksheet.Cell(rowNumber, 5).Value = row.LotNo;
            worksheet.Cell(rowNumber, 6).Value = row.QuantPath;
            worksheet.Cell(rowNumber, 7).Value = row.DataFolderPath;
            worksheet.Cell(rowNumber, 8).Value = row.ErrorType;
            worksheet.Cell(rowNumber, 9).Value = row.Message;
            worksheet.Cell(rowNumber, 10).Value = row.SuggestedAction;
            rowNumber++;
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.RangeUsed()?.SetAutoFilter();
        worksheet.Columns().AdjustToContents(1, 80);
        worksheet.Column(6).Width = Math.Min(80, worksheet.Column(6).Width);
        worksheet.Column(7).Width = Math.Min(80, worksheet.Column(7).Width);
        worksheet.Column(9).Width = Math.Min(80, worksheet.Column(9).Width);
        worksheet.Column(10).Width = Math.Min(80, worksheet.Column(10).Width);

        workbook.SaveAs(outputPath);
    }

    private static string ResolveUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName}_{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
