using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
    private const int BaseColumnCount = 16;
    private static readonly XLColor PpbWithinRangeFill = XLColor.FromHtml("#E2EFDA");
    private static readonly XLColor PpbOutOfRangeFill = XLColor.FromHtml("#FF6969");
    private static int FirstAreaColumn => BaseColumnCount + 1;
    private static int FirstPpbColumn => BaseColumnCount + CompoundMap.Analytes.Count + 1;
    private static int LastPpbColumn => BaseColumnCount + CompoundMap.Analytes.Count * 2;

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
        var outputDirectory = ResolveOutputDirectory(firstCandidate);
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

    public async Task<byte[]?> ExportAsync(
        string batchDate,
        IReadOnlyList<Query2ExportRow> rows,
        CancellationToken cancellationToken)
    {
        if (!_options.ExcelExport.Enabled || rows.Count == 0)
        {
            return null;
        }

        var templatePath = ResolveTemplatePath();
        return await Task.Run(() => ExportWorkbookToBytes(templatePath, rows), cancellationToken);
    }

    private string ResolveOutputDirectory(QuantFileCandidate firstCandidate) =>
        string.IsNullOrWhiteSpace(_options.ExportRoot)
            ? Path.Combine(firstCandidate.DayFolderPath, "QC")
            : Path.Combine(_options.ExportRoot, "QC");

    private string ResolveTemplatePath()
    {
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

        return templatePath;
    }

    private static byte[] ExportWorkbookToBytes(string templatePath, IReadOnlyList<Query2ExportRow> exportRows)
    {
        using var workbook = BuildWorkbook(templatePath, exportRows);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return RemovePpbConditionalFormatting(stream.ToArray());
    }

    private static void ExportWorkbook(string templatePath, string outputPath, IReadOnlyList<Query2ExportRow> exportRows)
    {
        using var workbook = BuildWorkbook(templatePath, exportRows);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        File.WriteAllBytes(outputPath, RemovePpbConditionalFormatting(stream.ToArray()));
    }

    private static XLWorkbook BuildWorkbook(string templatePath, IReadOnlyList<Query2ExportRow> exportRows)
    {
        var workbook = new XLWorkbook(templatePath);
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
        var ppbRowNumbers = new List<int>();
        foreach (var exportRow in exportRows)
        {
            var styleRowNumber = ResolveStyleRow(styleRows, exportRow.RowType);
            CopyTemplateRow(templateWorksheet, styleRowNumber, worksheet, targetRow);
            WriteRowValues(worksheet, targetRow, Query2ColumnLayout.BuildValues(exportRow));
            if (exportRow.RowType == Query2ExportRowType.Ppb)
            {
                CopyPpbResultsToPpbBlock(worksheet, targetRow);
                ppbRowNumbers.Add(targetRow);
            }

            targetRow++;
        }

        var copiedCritRowNumbers = new List<int>();
        foreach (var critRowNumber in critRows)
        {
            CopyTemplateRow(templateWorksheet, critRowNumber, worksheet, targetRow);
            copiedCritRowNumbers.Add(targetRow);
            targetRow++;
        }

        ApplyPpbRangeFills(worksheet, ppbRowNumbers, copiedCritRowNumbers);
        templateWorksheet.Delete();
        return workbook;
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

    private static void CopyPpbResultsToPpbBlock(IXLWorksheet worksheet, int rowNumber)
    {
        for (var index = 0; index < CompoundMap.Analytes.Count; index++)
        {
            var sourceCell = worksheet.Cell(rowNumber, FirstAreaColumn + index);
            var targetCell = worksheet.Cell(rowNumber, FirstPpbColumn + index);
            if (targetCell.IsEmpty() && !sourceCell.IsEmpty())
            {
                targetCell.Value = sourceCell.Value;
            }
        }
    }

    private static void ApplyPpbRangeFills(
        IXLWorksheet worksheet,
        IReadOnlyCollection<int> ppbRowNumbers,
        IReadOnlyCollection<int> critRowNumbers)
    {
        if (ppbRowNumbers.Count == 0 ||
            !TryResolveCritRow(worksheet, critRowNumbers, "MAX", out var maxRowNumber) ||
            !TryResolveCritRow(worksheet, critRowNumbers, "MIN", out var minRowNumber))
        {
            return;
        }

        foreach (var rowNumber in ppbRowNumbers)
        {
            for (var column = FirstPpbColumn; column <= LastPpbColumn; column++)
            {
                var cell = worksheet.Cell(rowNumber, column);
                if (!TryGetDecimal(cell, out var value) ||
                    !TryGetDecimal(worksheet.Cell(maxRowNumber, column), out var max) ||
                    !TryGetDecimal(worksheet.Cell(minRowNumber, column), out var min))
                {
                    continue;
                }

                var lowerBound = Math.Min(min, max);
                var upperBound = Math.Max(min, max);
                cell.Style.Fill.BackgroundColor = value >= lowerBound && value <= upperBound
                    ? PpbWithinRangeFill
                    : PpbOutOfRangeFill;
            }
        }
    }

    private static bool TryResolveCritRow(
        IXLWorksheet worksheet,
        IReadOnlyCollection<int> critRowNumbers,
        string marker,
        out int rowNumber)
    {
        foreach (var candidate in critRowNumbers)
        {
            var id = worksheet.Cell(candidate, 1).GetString();
            if (id.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                rowNumber = candidate;
                return true;
            }
        }

        rowNumber = 0;
        return false;
    }

    private static bool TryGetDecimal(IXLCell cell, out decimal value)
    {
        if (cell.TryGetValue<decimal>(out value))
        {
            return true;
        }

        var text = cell.GetString();
        return decimal.TryParse(text, out value);
    }

    private static byte[] RemovePpbConditionalFormatting(byte[] workbookContent)
    {
        using var stream = new MemoryStream();
        stream.Write(workbookContent, 0, workbookContent.Length);
        stream.Position = 0;

        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            var workbookPart = document.WorkbookPart;
            var sheet = workbookPart?.Workbook.Descendants<Sheet>()
                .FirstOrDefault(candidate => string.Equals(candidate.Name?.Value, Query2SheetName, StringComparison.OrdinalIgnoreCase));
            if (workbookPart is not null &&
                sheet?.Id?.Value is { } relationshipId &&
                workbookPart.GetPartById(relationshipId) is WorksheetPart worksheetPart)
            {
                foreach (var conditionalFormatting in worksheetPart.Worksheet.Elements<ConditionalFormatting>().ToArray())
                {
                var sqref = conditionalFormatting.GetAttribute("sqref", string.Empty).Value ?? string.Empty;
                    if (SequenceReferencesOverlapPpbColumns(sqref))
                    {
                        conditionalFormatting.Remove();
                    }
                }

                worksheetPart.Worksheet.Save();
            }
        }

        return stream.ToArray();
    }

    private static bool SequenceReferencesOverlapPpbColumns(string sequenceReferences)
    {
        return sequenceReferences
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(reference => RangeOverlapsColumnBand(reference, FirstPpbColumn, LastPpbColumn));
    }

    private static bool RangeOverlapsColumnBand(string reference, int firstColumn, int lastColumn)
    {
        var parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var rangeFirstColumn = ParseColumnNumber(parts[0]);
        var rangeLastColumn = ParseColumnNumber(parts.Length > 1 ? parts[1] : parts[0]);
        if (rangeFirstColumn == 0 || rangeLastColumn == 0)
        {
            return false;
        }

        return Math.Min(rangeFirstColumn, rangeLastColumn) <= lastColumn &&
            Math.Max(rangeFirstColumn, rangeLastColumn) >= firstColumn;
    }

    private static int ParseColumnNumber(string cellReference)
    {
        var columnNumber = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            columnNumber = columnNumber * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return columnNumber;
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
