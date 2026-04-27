using ClosedXML.Excel;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class Query2WorkbookExporter(
    IOptions<SchedulerOptions> options,
    ILogger<Query2WorkbookExporter> logger) : IQuery2WorkbookExporter
{
    private const string Query2SheetName = "Query2";
    private const string TemplateSheetName = "_Query2Template";

    private readonly SchedulerOptions _options = options.Value;

    public async Task<string?> ExportAsync(
        ImportWriteSet writeSet,
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (!_options.ExcelExport.Enabled || candidates.Count == 0)
        {
            return null;
        }

        var templatePath = _options.ExcelExport.TemplatePath;
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            throw new InvalidOperationException("Scheduler:ExcelExport:TemplatePath is required when Scheduler:ExcelExport:Enabled is true.");
        }

        templatePath = Path.GetFullPath(templatePath);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Query2 Excel template not found: {templatePath}", templatePath);
        }

        var firstCandidate = candidates.First();
        var batchDate = firstCandidate.LogicalBatchDate;
        var outputDirectory = Path.Combine(firstCandidate.DayFolderPath, "QC");
        var outputPath = Path.GetFullPath(Path.Combine(outputDirectory, $"Cylinder_Qc[{batchDate}].xlsx"));

        if (string.Equals(outputPath, templatePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Query2 Excel output path resolves to the same file as the template. Use a different template path.");
        }

        Directory.CreateDirectory(outputDirectory);

        await Task.Run(() => ExportWorkbook(templatePath, outputPath, writeSet.Query2Rows), cancellationToken);

        logger.LogInformation("Query2 Excel exported to {OutputPath}.", outputPath);
        return outputPath;
    }

    private static void ExportWorkbook(string templatePath, string outputPath, IReadOnlyList<Query2ExportRow> exportRows)
    {
        using var workbook = new XLWorkbook(templatePath);
        var worksheet = workbook.Worksheet(Query2SheetName)
            ?? throw new InvalidOperationException($"Template workbook does not contain worksheet '{Query2SheetName}'.");
        var templateWorksheet = worksheet.CopyTo(TemplateSheetName);

        foreach (var otherSheet in workbook.Worksheets.Where(sheet => sheet.Name != Query2SheetName && sheet.Name != TemplateSheetName).ToList())
        {
            otherSheet.Delete();
        }

        var lastUsedRow = templateWorksheet.LastRowUsed()?.RowNumber() ?? Query2ColumnLayout.HeaderRowNumber;
        var styleRows = DetectStyleRows(templateWorksheet, lastUsedRow);
        var critRows = DetectCritRows(templateWorksheet, lastUsedRow);

        if (worksheet.LastRowUsed() is { } usedRow && usedRow.RowNumber() >= Query2ColumnLayout.DataStartRowNumber)
        {
            worksheet.Rows(Query2ColumnLayout.DataStartRowNumber, usedRow.RowNumber()).Delete();
        }

        var targetRow = Query2ColumnLayout.DataStartRowNumber;
        foreach (var exportRow in exportRows)
        {
            var styleRowNumber = ResolveStyleRow(styleRows, exportRow.RowType);
            CopyTemplateRow(templateWorksheet, styleRowNumber, worksheet, targetRow);
            WriteRowValues(worksheet, targetRow, Query2ColumnLayout.BuildValues(exportRow));
            targetRow++;
        }

        foreach (var critRowNumber in critRows)
        {
            CopyTemplateRow(templateWorksheet, critRowNumber, worksheet, targetRow);
            targetRow++;
        }

        templateWorksheet.Delete();
        workbook.SaveAs(outputPath);
    }

    private static void CopyTemplateRow(IXLWorksheet sourceWorksheet, int sourceRowNumber, IXLWorksheet targetWorksheet, int targetRowNumber)
    {
        var sourceRange = sourceWorksheet.Range(
            sourceRowNumber,
            1,
            sourceRowNumber,
            Query2ColumnLayout.Headers.Count);

        sourceRange.CopyTo(targetWorksheet.Cell(targetRowNumber, 1));
        targetWorksheet.Row(targetRowNumber).Height = sourceWorksheet.Row(sourceRowNumber).Height;
    }

    private static void WriteRowValues(IXLWorksheet worksheet, int rowNumber, IReadOnlyList<object?> values)
    {
        for (var column = 1; column <= values.Count; column++)
        {
            var cell = worksheet.Cell(rowNumber, column);
            var value = values[column - 1];

            if (value is null)
            {
                cell.Clear(XLClearOptions.Contents);
                continue;
            }

            switch (value)
            {
                case DateTime dateTime:
                    cell.Value = dateTime;
                    break;
                case int intValue:
                    cell.Value = intValue;
                    break;
                case decimal decimalValue:
                    cell.Value = decimalValue;
                    break;
                case double doubleValue:
                    cell.Value = doubleValue;
                    break;
                case bool boolValue:
                    cell.Value = boolValue;
                    break;
                default:
                    cell.Value = value.ToString();
                    break;
            }
        }
    }

    private static Dictionary<Query2ExportRowType, int> DetectStyleRows(IXLWorksheet templateWorksheet, int lastUsedRow)
    {
        var styleRows = new Dictionary<Query2ExportRowType, int>();

        for (var rowNumber = Query2ColumnLayout.DataStartRowNumber; rowNumber <= lastUsedRow; rowNumber++)
        {
            var rowType = ClassifyRow(templateWorksheet.Cell(rowNumber, 1).GetString());
            if (rowType is null || rowType == Query2ExportRowType.Crit || styleRows.ContainsKey(rowType.Value))
            {
                continue;
            }

            styleRows[rowType.Value] = rowNumber;
        }

        if (!styleRows.ContainsKey(Query2ExportRowType.Qc) && styleRows.TryGetValue(Query2ExportRowType.Rpd, out var rpdRow))
        {
            styleRows[Query2ExportRowType.Qc] = rpdRow;
        }

        var required = new[]
        {
            Query2ExportRowType.Rf,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Ppb,
            Query2ExportRowType.Rpd,
            Query2ExportRowType.Qc
        };

        var missing = required.Where(rowType => !styleRows.ContainsKey(rowType)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Template workbook is missing example rows for: {string.Join(", ", missing)}.");
        }

        return styleRows;
    }

    private static IReadOnlyList<int> DetectCritRows(IXLWorksheet templateWorksheet, int lastUsedRow)
    {
        var rows = new List<int>();

        for (var rowNumber = Query2ColumnLayout.DataStartRowNumber; rowNumber <= lastUsedRow; rowNumber++)
        {
            if (ClassifyRow(templateWorksheet.Cell(rowNumber, 1).GetString()) == Query2ExportRowType.Crit)
            {
                rows.Add(rowNumber);
            }
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Template workbook does not contain Crit rows.");
        }

        return rows;
    }

    private static int ResolveStyleRow(
        IReadOnlyDictionary<Query2ExportRowType, int> styleRows,
        Query2ExportRowType rowType) =>
        styleRows.TryGetValue(rowType, out var rowNumber)
            ? rowNumber
            : throw new InvalidOperationException($"Template workbook is missing style row for {rowType}.");

    private static Query2ExportRowType? ClassifyRow(string idValue)
    {
        if (string.IsNullOrWhiteSpace(idValue))
        {
            return null;
        }

        var trimmed = idValue.Trim();
        if (trimmed.StartsWith("RF,", StringComparison.OrdinalIgnoreCase))
        {
            return Query2ExportRowType.Rf;
        }

        if (trimmed.StartsWith("AVG(", StringComparison.OrdinalIgnoreCase))
        {
            return Query2ExportRowType.Avg;
        }

        if (trimmed.StartsWith("ppb(", StringComparison.OrdinalIgnoreCase))
        {
            return Query2ExportRowType.Ppb;
        }

        if (trimmed.StartsWith("RPD(", StringComparison.OrdinalIgnoreCase))
        {
            return Query2ExportRowType.Rpd;
        }

        if (trimmed.StartsWith("QC(", StringComparison.OrdinalIgnoreCase))
        {
            return Query2ExportRowType.Qc;
        }

        if (trimmed.StartsWith("Crit(", StringComparison.OrdinalIgnoreCase))
        {
            return Query2ExportRowType.Crit;
        }

        return Query2ExportRowType.Raw;
    }
}
