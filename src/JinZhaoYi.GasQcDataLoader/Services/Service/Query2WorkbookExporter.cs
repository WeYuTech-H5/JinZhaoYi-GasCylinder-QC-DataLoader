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
    private const string QcJudgmentSheetName = "QC判定";
    private const string TemplateSheetName = "_Query2Template";
    private const int BaseColumnCount = 16;
    private static readonly XLColor WithinRangeFill = XLColor.FromHtml("#E2EFDA");
    private static readonly XLColor OutOfRangeFill = XLColor.FromHtml("#FF6969");
    private static readonly XLColor QcHeaderFill = XLColor.FromHtml("#F2F4F7");
    private static readonly XLColor QcHeaderFont = XLColor.FromHtml("#344054");
    private static readonly XLColor QcFailureFill = XLColor.FromHtml("#FDECEC");
    private static readonly XLColor QcFailureFont = XLColor.FromHtml("#B42318");
    private static readonly XLColor QcMutedFont = XLColor.FromHtml("#667085");
    private static readonly XLColor QcBorderColor = XLColor.FromHtml("#D0D5DD");
    private static readonly string[] QcJudgmentHeaders =
    [
        "PPB ID",
        "AnlzTime",
        "LotNo",
        "Port",
        "Container",
        "QC_IniPrs",
        "QC_IniPrsMin",
        "QC_FnlPrs",
        "QC_FnlPrsMin",
        "QC_PressureResult",
        "QC_Result",
        "QC_FailDesc"
    ];
    private static int FirstAreaColumn => BaseColumnCount + 1;
    private static int FixedAreaCount => CompoundMap.Analytes.Count;

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

        await Task.Run(() => ExportWorkbook(templatePath, outputPath, writeSet.Query2Rows, []), cancellationToken);

        logger.LogInformation("Query2 Excel exported to {OutputPath}.", outputPath);
        return outputPath;
    }

    public async Task<byte[]?> ExportAsync(
        string batchDate,
        IReadOnlyList<Query2ExportRow> rows,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
        CancellationToken cancellationToken)
        => await ExportAsync(batchDate, rows, dynamicAreaFields, null, cancellationToken);

    public async Task<byte[]?> ExportAsync(
        string batchDate,
        IReadOnlyList<Query2ExportRow> rows,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
        QcResultSettingsDto? qcSettings,
        CancellationToken cancellationToken)
        => await ExportAsync(
            batchDate,
            rows,
            dynamicAreaFields,
            qcSettings,
            [],
            cancellationToken);

    public async Task<byte[]?> ExportAsync(
        string batchDate,
        IReadOnlyList<Query2ExportRow> rows,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
        QcResultSettingsDto? qcSettings,
        IReadOnlyList<QcJudgmentSnapshot> qcJudgments,
        CancellationToken cancellationToken)
    {
        if (!_options.ExcelExport.Enabled || rows.Count == 0)
        {
            return null;
        }

        var templatePath = ResolveTemplatePath();
        return await Task.Run(
            () => ExportWorkbookToBytes(templatePath, rows, dynamicAreaFields, qcSettings, qcJudgments),
            cancellationToken);
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

    private static byte[] ExportWorkbookToBytes(
        string templatePath,
        IReadOnlyList<Query2ExportRow> exportRows,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
        QcResultSettingsDto? qcSettings,
        IReadOnlyList<QcJudgmentSnapshot> qcJudgments)
    {
        using var workbook = BuildWorkbook(templatePath, exportRows, dynamicAreaFields, qcSettings, qcJudgments);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return RemoveResultConditionalFormatting(stream.ToArray(), dynamicAreaFields.Count);
    }

    private static void ExportWorkbook(
        string templatePath,
        string outputPath,
        IReadOnlyList<Query2ExportRow> exportRows,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields)
    {
        using var workbook = BuildWorkbook(templatePath, exportRows, dynamicAreaFields, qcSettings: null, qcJudgments: []);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        File.WriteAllBytes(outputPath, RemoveResultConditionalFormatting(stream.ToArray(), dynamicAreaFields.Count));
    }

    private static XLWorkbook BuildWorkbook(
        string templatePath,
        IReadOnlyList<Query2ExportRow> exportRows,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
        QcResultSettingsDto? qcSettings,
        IReadOnlyList<QcJudgmentSnapshot> qcJudgments)
    {
        var workbook = new XLWorkbook(templatePath);
        var worksheet = workbook.Worksheet(Query2SheetName)
            ?? throw new InvalidOperationException($"Template workbook does not contain worksheet '{Query2SheetName}'.");
        var templateWorksheet = worksheet.CopyTo(TemplateSheetName);
        var headers = Query2ColumnLayout.BuildHeaders(dynamicAreaFields);

        ApplyDynamicAreaColumns(worksheet, dynamicAreaFields);
        ApplyDynamicAreaColumns(templateWorksheet, dynamicAreaFields);

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
            CopyTemplateRow(templateWorksheet, styleRowNumber, worksheet, targetRow, headers.Count);
            WriteRowValues(worksheet, targetRow, Query2ColumnLayout.BuildValues(exportRow, dynamicAreaFields));
            if (exportRow.RowType == Query2ExportRowType.Ppb)
            {
                CopyPpbResultsToPpbBlock(worksheet, targetRow, dynamicAreaFields.Count);
            }

            targetRow++;
        }

        var lastDataRowNumber = targetRow - 1;
        var copiedCritRowNumbers = CopyCritRows(
            templateWorksheet,
            worksheet,
            critRows,
            targetRow,
            headers.Count,
            qcSettings);
        targetRow += copiedCritRowNumbers.Count;

        ApplyQcCritValues(worksheet, Query2ColumnLayout.DataStartRowNumber, lastDataRowNumber, copiedCritRowNumbers, dynamicAreaFields.Count, qcSettings);
        ApplyResultRangeFills(worksheet, Query2ColumnLayout.DataStartRowNumber, lastDataRowNumber, copiedCritRowNumbers, dynamicAreaFields.Count, qcSettings);
        templateWorksheet.Delete();
        if (qcJudgments.Count > 0)
        {
            AddQcJudgmentWorksheet(workbook, qcJudgments);
        }

        return workbook;
    }

    private static void AddQcJudgmentWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<QcJudgmentSnapshot> judgments)
    {
        var worksheet = workbook.AddWorksheet(QcJudgmentSheetName);
        for (var column = 1; column <= QcJudgmentHeaders.Length; column++)
        {
            worksheet.Cell(1, column).Value = QcJudgmentHeaders[column - 1];
        }

        var headerRange = worksheet.Range(1, 1, 1, QcJudgmentHeaders.Length);
        headerRange.Style.Fill.BackgroundColor = QcHeaderFill;
        headerRange.Style.Font.FontColor = QcHeaderFont;
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Height = 24;

        var rowNumber = 2;
        foreach (var judgment in judgments)
        {
            WriteOptionalText(worksheet.Cell(rowNumber, 1), judgment.PpbId);
            if (judgment.AnlzTime.HasValue)
            {
                worksheet.Cell(rowNumber, 2).Value = judgment.AnlzTime.Value;
                worksheet.Cell(rowNumber, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            }

            WriteOptionalText(worksheet.Cell(rowNumber, 3), judgment.LotNo);
            WriteOptionalText(worksheet.Cell(rowNumber, 4), judgment.Port);
            WriteOptionalText(worksheet.Cell(rowNumber, 5), judgment.Container);
            WriteNullableDecimal(worksheet.Cell(rowNumber, 6), judgment.IniPrs);
            WriteNullableDecimal(worksheet.Cell(rowNumber, 7), judgment.IniPrsMin);
            WriteNullableDecimal(worksheet.Cell(rowNumber, 8), judgment.FnlPrs);
            WriteNullableDecimal(worksheet.Cell(rowNumber, 9), judgment.FnlPrsMin);
            worksheet.Cell(rowNumber, 10).Value = judgment.PressureResult;
            worksheet.Cell(rowNumber, 11).Value = judgment.Result;
            WriteOptionalText(worksheet.Cell(rowNumber, 12), judgment.FailDesc);

            ApplyPressureValueStyle(
                worksheet.Cell(rowNumber, 6),
                worksheet.Cell(rowNumber, 7),
                judgment.IniPrsMin,
                judgment.IniPrsFailed);
            ApplyPressureValueStyle(
                worksheet.Cell(rowNumber, 8),
                worksheet.Cell(rowNumber, 9),
                judgment.FnlPrsMin,
                judgment.FnlPrsFailed);
            ApplyQcStatusStyle(worksheet.Cell(rowNumber, 10), judgment.PressureResult);
            ApplyQcStatusStyle(worksheet.Cell(rowNumber, 11), judgment.Result);
            if (!string.IsNullOrWhiteSpace(judgment.FailDesc))
            {
                worksheet.Cell(rowNumber, 12).Style.Font.FontColor = QcFailureFont;
            }

            rowNumber++;
        }

        var lastRowNumber = rowNumber - 1;
        var usedRange = worksheet.Range(1, 1, lastRowNumber, QcJudgmentHeaders.Length);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.OutsideBorderColor = QcBorderColor;
        usedRange.Style.Border.InsideBorderColor = QcBorderColor;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Range(2, 6, lastRowNumber, 9).Style.NumberFormat.Format = "0.################";
        worksheet.Range(2, 12, lastRowNumber, 12).Style.Alignment.WrapText = true;
        usedRange.SetAutoFilter();
        worksheet.SheetView.FreezeRows(1);

        worksheet.Column(1).Width = 16;
        worksheet.Column(2).Width = 21;
        worksheet.Column(3).Width = 18;
        worksheet.Column(4).Width = 12;
        worksheet.Column(5).Width = 16;
        worksheet.Columns(6, 9).Width = 16;
        worksheet.Columns(10, 11).Width = 20;
        worksheet.Column(12).Width = 32;
    }

    private static void WriteNullableDecimal(IXLCell cell, decimal? value)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
        }
    }

    private static void WriteOptionalText(IXLCell cell, string? value)
    {
        if (value is not null)
        {
            cell.Value = value;
        }
    }

    private static void ApplyPressureValueStyle(
        IXLCell valueCell,
        IXLCell minCell,
        decimal? min,
        bool failed)
    {
        if (!min.HasValue)
        {
            valueCell.Style.Font.FontColor = QcMutedFont;
            minCell.Style.Font.FontColor = QcMutedFont;
            return;
        }

        if (failed)
        {
            valueCell.Style.Fill.BackgroundColor = QcFailureFill;
            valueCell.Style.Font.FontColor = QcFailureFont;
            valueCell.Style.Font.Bold = true;
        }
    }

    private static void ApplyQcStatusStyle(IXLCell cell, string status)
    {
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        if (string.Equals(status, QcResultValues.Fail, StringComparison.OrdinalIgnoreCase))
        {
            cell.Style.Font.FontColor = QcFailureFont;
            cell.Style.Font.Bold = true;
            return;
        }

        if (!string.Equals(status, QcResultValues.Pass, StringComparison.OrdinalIgnoreCase))
        {
            cell.Style.Font.FontColor = QcMutedFont;
        }
    }

    private static void ApplyDynamicAreaColumns(
        IXLWorksheet worksheet,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields)
    {
        if (dynamicAreaFields.Count == 0)
        {
            return;
        }

        var insertBeforeColumn = FirstAreaColumn + FixedAreaCount;
        worksheet.Column(insertBeforeColumn).InsertColumnsBefore(dynamicAreaFields.Count);
        var styleColumn = insertBeforeColumn - 1;
        for (var index = 0; index < dynamicAreaFields.Count; index++)
        {
            var columnNumber = insertBeforeColumn + index;
            worksheet.Column(columnNumber).Style = worksheet.Column(styleColumn).Style;
            worksheet.Column(columnNumber).Width = worksheet.Column(styleColumn).Width;
            worksheet.Cell(Query2ColumnLayout.HeaderRowNumber, columnNumber).Value = dynamicAreaFields[index].DisplayName;
        }
    }

    private static void CopyTemplateRow(
        IXLWorksheet sourceWorksheet,
        int sourceRowNumber,
        IXLWorksheet targetWorksheet,
        int targetRowNumber,
        int columnCount)
    {
        var sourceRange = sourceWorksheet.Range(
            sourceRowNumber,
            1,
            sourceRowNumber,
            columnCount);

        sourceRange.CopyTo(targetWorksheet.Cell(targetRowNumber, 1));
        targetWorksheet.Row(targetRowNumber).Height = sourceWorksheet.Row(sourceRowNumber).Height;
    }

    private static IReadOnlyList<int> CopyCritRows(
        IXLWorksheet templateWorksheet,
        IXLWorksheet worksheet,
        IReadOnlyCollection<int> critRows,
        int firstTargetRow,
        int columnCount,
        QcResultSettingsDto? qcSettings)
    {
        if (qcSettings is null ||
            !TryResolveCritRow(templateWorksheet, critRows, "MAX", out var maxTemplateRowNumber) ||
            !TryResolveCritRow(templateWorksheet, critRows, "MIN", out var minTemplateRowNumber))
        {
            return CopyTemplateCritRows(templateWorksheet, worksheet, critRows, firstTargetRow, columnCount);
        }

        var rows = new[]
        {
            new ConfiguredCritRow(maxTemplateRowNumber, QcResultSettingRules.Container05),
            new ConfiguredCritRow(minTemplateRowNumber, QcResultSettingRules.Container05),
            new ConfiguredCritRow(maxTemplateRowNumber, QcResultSettingRules.Container1L),
            new ConfiguredCritRow(minTemplateRowNumber, QcResultSettingRules.Container1L)
        };

        var copiedRows = new List<int>();
        var targetRow = firstTargetRow;
        foreach (var row in rows)
        {
            CopyTemplateRow(templateWorksheet, row.TemplateRowNumber, worksheet, targetRow, columnCount);
            worksheet.Cell(targetRow, 1).Value = FormatConfiguredCritLabel(
                templateWorksheet.Cell(row.TemplateRowNumber, 1).GetString(),
                row.ContainerType);
            copiedRows.Add(targetRow);
            targetRow++;
        }

        return copiedRows;
    }

    private static IReadOnlyList<int> CopyTemplateCritRows(
        IXLWorksheet templateWorksheet,
        IXLWorksheet worksheet,
        IReadOnlyCollection<int> critRows,
        int firstTargetRow,
        int columnCount)
    {
        var copiedRows = new List<int>();
        var targetRow = firstTargetRow;
        foreach (var critRowNumber in critRows)
        {
            CopyTemplateRow(templateWorksheet, critRowNumber, worksheet, targetRow, columnCount);
            copiedRows.Add(targetRow);
            targetRow++;
        }

        return copiedRows;
    }

    private static string FormatConfiguredCritLabel(string templateLabel, string containerType)
    {
        var label = StripConfiguredCritContainerSuffix(templateLabel);
        return $"{label}-{containerType}";
    }

    private static string StripConfiguredCritContainerSuffix(string value)
    {
        var label = value.Trim();
        foreach (var containerType in QcResultSettingRules.SupportedContainers)
        {
            var suffix = $"-{containerType}";
            if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return label[..^suffix.Length].TrimEnd();
            }
        }

        return label;
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

    private static void CopyPpbResultsToPpbBlock(
        IXLWorksheet worksheet,
        int rowNumber,
        int dynamicAreaCount)
    {
        var firstPpbColumn = ResolveFirstPpbColumn(dynamicAreaCount);
        for (var index = 0; index < CompoundMap.Analytes.Count; index++)
        {
            var sourceCell = worksheet.Cell(rowNumber, FirstAreaColumn + index);
            var targetCell = worksheet.Cell(rowNumber, firstPpbColumn + index);
            if (targetCell.IsEmpty() && !sourceCell.IsEmpty())
            {
                targetCell.Value = sourceCell.Value;
            }
        }
    }

    private static void ApplyResultRangeFills(
        IXLWorksheet worksheet,
        int firstDataRowNumber,
        int lastDataRowNumber,
        IReadOnlyCollection<int> critRowNumbers,
        int dynamicAreaCount,
        QcResultSettingsDto? qcSettings)
    {
        if (lastDataRowNumber < firstDataRowNumber)
        {
            return;
        }

        if (qcSettings is not null)
        {
            ApplyConfiguredResultRangeFills(worksheet, firstDataRowNumber, lastDataRowNumber, dynamicAreaCount, qcSettings);
            return;
        }

        if (!TryResolveCritRow(worksheet, critRowNumbers, "MAX", out var maxRowNumber) ||
            !TryResolveCritRow(worksheet, critRowNumbers, "MIN", out var minRowNumber))
        {
            return;
        }

        for (var rowNumber = firstDataRowNumber; rowNumber <= lastDataRowNumber; rowNumber++)
        {
            if (ClassifyRow(worksheet.Cell(rowNumber, 1).GetString()) == Query2ExportRowType.Ppb)
            {
                ApplyRangeFills(
                    worksheet,
                    rowNumber,
                    FirstAreaColumn,
                    ResolveLastAreaColumn(),
                    maxRowNumber,
                    minRowNumber);
            }

            ApplyRangeFills(
                worksheet,
                rowNumber,
                ResolveFirstPpbColumn(dynamicAreaCount),
                ResolveLastPpbColumn(dynamicAreaCount),
                maxRowNumber,
                minRowNumber);
        }
    }

    private static void ApplyQcCritValues(
        IXLWorksheet worksheet,
        int firstDataRowNumber,
        int lastDataRowNumber,
        IReadOnlyCollection<int> critRowNumbers,
        int dynamicAreaCount,
        QcResultSettingsDto? qcSettings)
    {
        if (qcSettings is null ||
            lastDataRowNumber < firstDataRowNumber)
        {
            return;
        }

        foreach (var containerType in QcResultSettingRules.SupportedContainers)
        {
            if (!TryResolveConfiguredCritRow(worksheet, critRowNumbers, "MAX", containerType, out var maxRowNumber) ||
                !TryResolveConfiguredCritRow(worksheet, critRowNumbers, "MIN", containerType, out var minRowNumber))
            {
                continue;
            }

            ClearCritBoundCells(worksheet, maxRowNumber, dynamicAreaCount);
            ClearCritBoundCells(worksheet, minRowNumber, dynamicAreaCount);

            for (var index = 0; index < CompoundMap.Analytes.Count; index++)
            {
                var analyte = CompoundMap.Analytes[index];
                if (!TryResolveConfiguredBounds(qcSettings, containerType, analyte.Suffix, out var min, out var max))
                {
                    continue;
                }

                var areaColumn = FirstAreaColumn + index;
                var ppbColumn = ResolveFirstPpbColumn(dynamicAreaCount) + index;
                WriteCritBound(worksheet.Cell(maxRowNumber, areaColumn), max);
                WriteCritBound(worksheet.Cell(minRowNumber, areaColumn), min);
                WriteCritBound(worksheet.Cell(maxRowNumber, ppbColumn), max);
                WriteCritBound(worksheet.Cell(minRowNumber, ppbColumn), min);
            }
        }
    }

    private static void ClearCritBoundCells(IXLWorksheet worksheet, int rowNumber, int dynamicAreaCount)
    {
        var firstPpbColumn = ResolveFirstPpbColumn(dynamicAreaCount);
        for (var index = 0; index < CompoundMap.Analytes.Count; index++)
        {
            worksheet.Cell(rowNumber, FirstAreaColumn + index).Clear(XLClearOptions.Contents);
            worksheet.Cell(rowNumber, firstPpbColumn + index).Clear(XLClearOptions.Contents);
        }
    }

    private static void ApplyConfiguredResultRangeFills(
        IXLWorksheet worksheet,
        int firstDataRowNumber,
        int lastDataRowNumber,
        int dynamicAreaCount,
        QcResultSettingsDto qcSettings)
    {
        for (var rowNumber = firstDataRowNumber; rowNumber <= lastDataRowNumber; rowNumber++)
        {
            var containerType = ResolveContainerType(worksheet.Cell(rowNumber, 11).GetString());
            if (containerType is null)
            {
                continue;
            }

            var rowType = ClassifyRow(worksheet.Cell(rowNumber, 1).GetString());
            if (rowType == Query2ExportRowType.Ppb)
            {
                ApplyConfiguredRangeFills(
                    worksheet,
                    rowNumber,
                    FirstAreaColumn,
                    qcSettings,
                    containerType,
                    markMissingAsFailure: true);
            }

            ApplyConfiguredRangeFills(
                worksheet,
                rowNumber,
                ResolveFirstPpbColumn(dynamicAreaCount),
                qcSettings,
                containerType,
                markMissingAsFailure: rowType == Query2ExportRowType.Ppb);
        }
    }

    private static void ApplyConfiguredRangeFills(
        IXLWorksheet worksheet,
        int rowNumber,
        int firstColumn,
        QcResultSettingsDto qcSettings,
        string containerType,
        bool markMissingAsFailure)
    {
        for (var index = 0; index < CompoundMap.Analytes.Count; index++)
        {
            var analyte = CompoundMap.Analytes[index];
            if (!TryResolveConfiguredBounds(qcSettings, containerType, analyte.Suffix, out var min, out var max))
            {
                continue;
            }

            var cell = worksheet.Cell(rowNumber, firstColumn + index);
            if (!TryGetDecimal(cell, out var value))
            {
                if (markMissingAsFailure)
                {
                    cell.Style.Fill.BackgroundColor = OutOfRangeFill;
                }

                continue;
            }

            var failed = (min.HasValue && value < min.Value) ||
                (max.HasValue && value > max.Value);
            cell.Style.Fill.BackgroundColor = failed ? OutOfRangeFill : WithinRangeFill;
        }
    }

    private static bool TryResolveConfiguredBounds(
        QcResultSettingsDto qcSettings,
        string containerType,
        string analyteKey,
        out decimal? min,
        out decimal? max)
    {
        min = null;
        max = null;
        var rule = qcSettings.ConcentrationRules.FirstOrDefault(rule =>
            rule.IsActive &&
            string.Equals(rule.AnalyteKey, analyteKey, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            return false;
        }

        (min, max) = string.Equals(containerType, QcResultSettingRules.Container05, StringComparison.OrdinalIgnoreCase)
            ? (rule.Min05, rule.Max05)
            : (rule.Min1L, rule.Max1L);
        return min.HasValue || max.HasValue;
    }

    private static string? ResolveContainerType(string? container)
    {
        try
        {
            return QcResultSettingRules.NormalizeContainerType(container);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void WriteCritBound(IXLCell cell, decimal? value)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            return;
        }

        cell.Clear(XLClearOptions.Contents);
    }

    private static void ApplyRangeFills(
        IXLWorksheet worksheet,
        int rowNumber,
        int firstColumn,
        int lastColumn,
        int maxRowNumber,
        int minRowNumber)
    {
        for (var column = firstColumn; column <= lastColumn; column++)
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
                ? WithinRangeFill
                : OutOfRangeFill;
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

    private static bool TryResolveConfiguredCritRow(
        IXLWorksheet worksheet,
        IReadOnlyCollection<int> critRowNumbers,
        string marker,
        string containerType,
        out int rowNumber)
    {
        foreach (var candidate in critRowNumbers)
        {
            var id = worksheet.Cell(candidate, 1).GetString();
            if (!id.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ResolveContainerType(id), containerType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rowNumber = candidate;
            return true;
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

    private static int ResolveFirstPpbColumn(int dynamicAreaCount) =>
        BaseColumnCount + FixedAreaCount + dynamicAreaCount + 1;

    private static int ResolveLastAreaColumn() =>
        FirstAreaColumn + FixedAreaCount - 1;

    private static int ResolveLastPpbColumn(int dynamicAreaCount) =>
        ResolveFirstPpbColumn(dynamicAreaCount) + CompoundMap.Analytes.Count - 1;

    private static byte[] RemoveResultConditionalFormatting(byte[] workbookContent, int dynamicAreaCount)
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
                    if (SequenceReferencesOverlapResultColumns(sqref, dynamicAreaCount))
                    {
                        conditionalFormatting.Remove();
                    }
                }

                worksheetPart.Worksheet.Save();
            }
        }

        return stream.ToArray();
    }

    private static bool SequenceReferencesOverlapResultColumns(string sequenceReferences, int dynamicAreaCount)
    {
        var firstPpbColumn = ResolveFirstPpbColumn(dynamicAreaCount);
        var lastPpbColumn = ResolveLastPpbColumn(dynamicAreaCount);
        return sequenceReferences
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(reference =>
                RangeOverlapsColumnBand(reference, FirstAreaColumn, ResolveLastAreaColumn()) ||
                RangeOverlapsColumnBand(reference, firstPpbColumn, lastPpbColumn));
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

    private sealed record ConfiguredCritRow(int TemplateRowNumber, string ContainerType);
}
