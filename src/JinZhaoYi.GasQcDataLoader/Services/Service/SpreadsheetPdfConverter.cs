using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class SpreadsheetPdfConverter(IOptions<SchedulerOptions> options) : ISpreadsheetPdfConverter
{
    private const string SmallCardPdfPrintArea = "$B$2:$AA$66";
    private const double SmallCardLeftMarginInches = 0.72D;
    private const double SmallCardRightMarginInches = 0.25D;
    private const double SmallCardTopMarginInches = 0.8D;
    private const double SmallCardBottomMarginInches = 0.75D;
    private const double SmallCardRowHeightScale = 0.96D;
    private const string SmallCardPdfFontName = "Times New Roman";
    private const uint SmallCardPdfPaperSize = 1U; // Letter, matching the approved Excel-rendered PDF.
    private const uint SmallCardPdfScale = 65U;
    private const double SmallCardWidthPoints = 246.6D;
    private const double SmallCardHeightPoints = 360.5D;
    private const double SmallCardHorizontalGapPoints = 2.4D;
    private const double SmallCardVerticalGapPoints = 3.75D;
    private const int SmallCardColumnsPerPage = 3;
    private static readonly string[] SmallCardSampleCells =
    [
        "F6", "O6", "X6",
        "F28", "O28", "X28",
        "F50", "O50", "X50"
    ];
    private static readonly string[] SmallCardPdfSlotRanges =
    [
        "B2:I22", "K2:R22", "T2:AA22",
        "B24:I44", "K24:R44", "T24:AA44",
        "B46:I66", "K46:R66", "T46:AA66"
    ];
    private readonly SchedulerCoaExportOptions _options = options.Value.CoaExport;

    public async Task<byte[]> ConvertXlsxToPdfAsync(
        byte[] workbookContent,
        string workbookFileName,
        CancellationToken cancellationToken)
    {
        var isCoaLargeWorkbook = WorkbookContainsCoaLargeWorksheet(workbookContent);
        var isCoaSmallWorkbook = WorkbookContainsCoaSmallWorksheet(workbookContent);
        var pdfWorkbookContent = isCoaLargeWorkbook || isCoaSmallWorkbook
            ? PrepareWorkbookForPdfConversion(workbookContent)
            : workbookContent;

        if (!isCoaLargeWorkbook && !isCoaSmallWorkbook)
        {
            var excelPdfContent = TryConvertWithExcel(pdfWorkbookContent);
            if (excelPdfContent is not null)
            {
                return excelPdfContent;
            }
        }

        if (string.IsNullOrWhiteSpace(_options.LibreOfficePath) &&
            !IsExecutableAvailable("soffice"))
        {
            if (isCoaLargeWorkbook || isCoaSmallWorkbook)
            {
                throw new InvalidOperationException(
                    "COA PDF export requires LibreOffice to keep rendering consistent across machines.");
            }

            return ConvertWithFallback(workbookContent, workbookFileName);
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"gas-qc-coa-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var workbookPath = Path.Combine(tempDirectory, "input.xlsx");
            await File.WriteAllBytesAsync(workbookPath, pdfWorkbookContent, cancellationToken);
            var userProfileDirectory = Path.Combine(tempDirectory, "lo-profile");
            Directory.CreateDirectory(userProfileDirectory);

            var executablePath = string.IsNullOrWhiteSpace(_options.LibreOfficePath)
                ? "soffice"
                : _options.LibreOfficePath.Trim();

            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = tempDirectory
            };
            processStartInfo.ArgumentList.Add("--headless");
            processStartInfo.ArgumentList.Add($"-env:UserInstallation={BuildFileUri(userProfileDirectory)}");
            processStartInfo.ArgumentList.Add("--convert-to");
            processStartInfo.ArgumentList.Add("pdf");
            processStartInfo.ArgumentList.Add("--outdir");
            processStartInfo.ArgumentList.Add(tempDirectory);
            processStartInfo.ArgumentList.Add(workbookPath);

            using var process = Process.Start(processStartInfo)
                ?? throw new InvalidOperationException("Unable to start LibreOffice PDF converter.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.PdfConversionTimeoutSeconds));
            var exited = await WaitForExitAsync(process, timeout, cancellationToken);

            if (!exited)
            {
                TryKill(process);
                throw new InvalidOperationException($"LibreOffice PDF conversion timed out after {timeout.TotalSeconds:0} seconds.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"LibreOffice PDF conversion failed with exit code {process.ExitCode}. {stderr}{stdout}");
            }

            var pdfPath = Path.ChangeExtension(workbookPath, ".pdf");
            if (!File.Exists(pdfPath))
            {
                throw new InvalidOperationException($"LibreOffice did not create PDF output for '{workbookFileName}'. {stderr}{stdout}");
            }

            var pdfContent = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
            pdfContent = OverlayPdfHeaderImageIfNeeded(pdfContent, pdfWorkbookContent);
            var smallCardHeaderImagePath = isCoaSmallWorkbook ? ResolveSmallCardHeaderImagePath() : null;
            if (smallCardHeaderImagePath is not null)
            {
                pdfContent = SmallCardCompanyNameOverlay.Add(
                    pdfContent,
                    GetSmallCardCounts(pdfWorkbookContent),
                    smallCardHeaderImagePath);
            }

            return pdfContent;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (isCoaLargeWorkbook || isCoaSmallWorkbook)
            {
                throw new InvalidOperationException(
                    "COA PDF export requires LibreOffice to keep rendering consistent across machines.",
                    ex);
            }

            if (_options.UseBasicPdfFallback)
            {
                return ConvertWithFallback(workbookContent, workbookFileName);
            }

            throw new InvalidOperationException(
                "LibreOffice PDF converter was not found. Set Scheduler:CoaExport:LibreOfficePath or add soffice to PATH.",
                ex);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string BuildFileUri(string directoryPath)
    {
        var path = directoryPath.EndsWith(Path.DirectorySeparatorChar)
            ? directoryPath
            : directoryPath + Path.DirectorySeparatorChar;
        return new Uri(path).AbsoluteUri;
    }

    private static byte[] PrepareWorkbookForPdfConversion(byte[] workbookContent)
    {
        using var stream = new MemoryStream();
        stream.Write(workbookContent, 0, workbookContent.Length);
        stream.Position = 0;

        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            var workbookPart = document.WorkbookPart;
            if (workbookPart is null)
            {
                return workbookContent;
            }

            var pdfHeaderImagePath = ResolvePdfHeaderImagePath();
            foreach (var worksheetPart in workbookPart.WorksheetParts)
            {
                if (IsCoaLargeWorksheet(workbookPart, worksheetPart))
                {
                    ApplyCoaLargePdfPrintLayout(worksheetPart);
                }
                else if (IsCoaSmallWorksheet(workbookPart, worksheetPart))
                {
                    ApplyCoaSmallPdfPrintLayout(workbookPart, worksheetPart);
                    ApplyCoaSmallPdfFonts(workbookPart);
                }

                ReplaceHeaderFooterWithPdfHeaderImage(workbookPart, worksheetPart, pdfHeaderImagePath);
            }

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static void ApplyCoaLargePdfPrintLayout(WorksheetPart worksheetPart)
    {
        var worksheet = worksheetPart.Worksheet;
        var pageMargins = worksheet.GetFirstChild<PageMargins>();
        if (pageMargins is null)
        {
            pageMargins = new PageMargins
            {
                Left = 0.25D,
                Right = 0.25D,
                Top = 0.75D,
                Bottom = 0.75D,
                Header = 0.25D,
                Footer = 0.05D
            };
            worksheet.Append(pageMargins);
        }
        else
        {
            pageMargins.Right = pageMargins.Left;
        }

        var printOptions = worksheet.GetFirstChild<PrintOptions>();
        if (printOptions is null)
        {
            printOptions = new PrintOptions();
            worksheet.InsertBefore(printOptions, pageMargins);
        }

        printOptions.HorizontalCentered = true;
        worksheet.Save();
    }

    private static void ApplyCoaSmallPdfPrintLayout(WorkbookPart workbookPart, WorksheetPart worksheetPart)
    {
        var worksheet = worksheetPart.Worksheet;
        var sheetProperties = worksheet.GetFirstChild<SheetProperties>();
        if (sheetProperties is null)
        {
            sheetProperties = new SheetProperties();
            worksheet.InsertAt(sheetProperties, 0);
        }

        sheetProperties.PageSetupProperties ??= new PageSetupProperties();
        sheetProperties.PageSetupProperties.FitToPage = false;
        sheetProperties.PageSetupProperties.AutoPageBreaks = false;

        var pageMargins = worksheet.GetFirstChild<PageMargins>();
        if (pageMargins is null)
        {
            pageMargins = new PageMargins();
            worksheet.Append(pageMargins);
        }

        // Keep the template's Excel print geometry while pinning the physical page size.
        pageMargins.Left = SmallCardLeftMarginInches;
        pageMargins.Right = SmallCardRightMarginInches;
        pageMargins.Top = SmallCardTopMarginInches;
        pageMargins.Bottom = SmallCardBottomMarginInches;
        pageMargins.Header = 0.3D;
        pageMargins.Footer = 0.3D;

        var printOptions = worksheet.GetFirstChild<PrintOptions>();
        if (printOptions is null)
        {
            printOptions = new PrintOptions();
            worksheet.InsertBefore(printOptions, pageMargins);
        }

        printOptions.HorizontalCentered = false;
        printOptions.VerticalCentered = false;

        var pageSetup = worksheet.GetFirstChild<PageSetup>();
        if (pageSetup is null)
        {
            pageSetup = new PageSetup();
            worksheet.Append(pageSetup);
        }

        pageSetup.PaperSize = SmallCardPdfPaperSize;
        pageSetup.Orientation = OrientationValues.Portrait;
        pageSetup.Scale = SmallCardPdfScale;
        pageSetup.FitToWidth = null;
        pageSetup.FitToHeight = null;

        ScaleSmallCardRowHeights(worksheet);
        SetPrintArea(workbookPart, worksheetPart, SmallCardPdfPrintArea);
        ClearUnusedSmallCardPdfSlots(workbookPart, worksheetPart);
        worksheet.Save();
    }

    private static void ScaleSmallCardRowHeights(Worksheet worksheet)
    {
        var sheetFormat = worksheet.GetFirstChild<SheetFormatProperties>();
        if (sheetFormat?.DefaultRowHeight?.Value is { } defaultRowHeight)
        {
            sheetFormat.DefaultRowHeight = defaultRowHeight * SmallCardRowHeightScale;
        }

        foreach (var row in worksheet.Descendants<Row>())
        {
            if (row.Height?.Value is { } height)
            {
                row.Height = height * SmallCardRowHeightScale;
            }
        }
    }

    private static void ApplyCoaSmallPdfFonts(WorkbookPart workbookPart)
    {
        var fonts = workbookPart.WorkbookStylesPart?.Stylesheet.Fonts;
        if (fonts is null)
        {
            return;
        }

        foreach (var font in fonts.Elements<Font>())
        {
            var fontName = font.GetFirstChild<FontName>();
            if (string.Equals(fontName?.Val?.Value, "Microsoft JhengHei UI", StringComparison.OrdinalIgnoreCase))
            {
                fontName!.Val = SmallCardPdfFontName;
            }
        }

        workbookPart.WorkbookStylesPart!.Stylesheet.Save();
    }

    private static string? ResolvePdfHeaderImagePath()
    {
        var baseDirectory = Path.GetDirectoryName(typeof(SpreadsheetPdfConverter).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var imageDirectory = Path.Combine(baseDirectory, "wwwroot", "image");
        if (!Directory.Exists(imageDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(imageDirectory, "*.png")
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? ResolveSmallCardHeaderImagePath()
    {
        var baseDirectory = Path.GetDirectoryName(typeof(SpreadsheetPdfConverter).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var imagePath = Path.Combine(baseDirectory, "wwwroot", "image", "coa-small-card-header.png");
        return File.Exists(imagePath) ? imagePath : null;
    }

    private static void ReplaceHeaderFooterWithPdfHeaderImage(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        string? pdfHeaderImagePath)
    {
        if (string.IsNullOrWhiteSpace(pdfHeaderImagePath) ||
            !File.Exists(pdfHeaderImagePath) ||
            !IsCoaLargeWorksheet(workbookPart, worksheetPart))
        {
            return;
        }

        if (worksheetPart.Worksheet.Elements<LegacyDrawingHeaderFooter>().Any())
        {
            ReplaceHeaderFooterImagesWithPdfHeaderImage(worksheetPart, pdfHeaderImagePath);
        }

        worksheetPart.Worksheet.Save();
    }

    private static bool IsCoaLargeWorksheet(WorkbookPart workbookPart, WorksheetPart worksheetPart)
    {
        return worksheetPart.Worksheet
            .Descendants<Row>()
            .Where(row => (row.RowIndex?.Value ?? 0) <= 30)
            .SelectMany(row => row.Elements<Cell>())
            .Select(cell => GetCellText(workbookPart, cell))
            .Any(text => text.Contains("Certificate Of Analysis", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCoaSmallWorksheet(WorkbookPart workbookPart, WorksheetPart worksheetPart)
    {
        var sheetTexts = worksheetPart.Worksheet
            .Descendants<Row>()
            .Where(row => (row.RowIndex?.Value ?? 0) <= 70)
            .SelectMany(row => row.Elements<Cell>())
            .Select(cell => GetCellText(workbookPart, cell))
            .ToArray();

        return sheetTexts.Any(text => text.Contains("NF-SEMI STD", StringComparison.OrdinalIgnoreCase)) &&
            sheetTexts.Any(text => text.Contains("Compounds", StringComparison.OrdinalIgnoreCase)) &&
            sheetTexts.Any(text => text.Contains("Analytical Results", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetCellText(WorkbookPart workbookPart, Cell cell)
    {
        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(cell.CellValue?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex))
        {
            var sharedString = workbookPart.SharedStringTablePart?.SharedStringTable
                .Elements<SharedStringItem>()
                .ElementAtOrDefault(sharedStringIndex);
            return sharedString?.InnerText ?? string.Empty;
        }

        return cell.CellValue?.Text ?? cell.InnerText ?? string.Empty;
    }

    private static byte[] OverlayPdfHeaderImageIfNeeded(byte[] pdfContent, byte[] workbookContent)
    {
        var pdfHeaderImagePath = ResolvePdfHeaderImagePath();
        if (string.IsNullOrWhiteSpace(pdfHeaderImagePath) ||
            !File.Exists(pdfHeaderImagePath) ||
            !WorkbookContainsCoaLargeWorksheet(workbookContent))
        {
            return pdfContent;
        }

        try
        {
            return PdfHeaderImageOverlay.Add(pdfContent, pdfHeaderImagePath);
        }
        catch
        {
            return pdfContent;
        }
    }

    private static bool WorkbookContainsCoaLargeWorksheet(byte[] workbookContent)
    {
        using var stream = new MemoryStream(workbookContent);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart;
        return workbookPart is not null &&
            (workbookPart.WorksheetParts.Any(worksheetPart => IsCoaLargeWorksheet(workbookPart, worksheetPart)) ||
                SharedStringsContainCoaLargeMarkers(workbookPart));
    }

    private static bool WorkbookContainsCoaSmallWorksheet(byte[] workbookContent)
    {
        using var stream = new MemoryStream(workbookContent);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart;
        return workbookPart is not null &&
            (workbookPart.WorksheetParts.Any(worksheetPart => IsCoaSmallWorksheet(workbookPart, worksheetPart)) ||
                SharedStringsContainCoaSmallMarkers(workbookPart));
    }

    private static IReadOnlyList<int> GetSmallCardCounts(byte[] workbookContent)
    {
        using var stream = new MemoryStream(workbookContent);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart?.Workbook.Sheets is null)
        {
            return [];
        }

        var counts = new List<int>();
        foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
        {
            if (sheet.Id?.Value is not { } relationshipId ||
                workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart ||
                !IsCoaSmallWorksheet(workbookPart, worksheetPart))
            {
                continue;
            }

            var cells = worksheetPart.Worksheet.Descendants<Cell>()
                .Where(cell => cell.CellReference?.Value is not null)
                .ToDictionary(
                    cell => cell.CellReference!.Value!,
                    StringComparer.OrdinalIgnoreCase);
            counts.Add(SmallCardSampleCells.Count(cellReference =>
                cells.TryGetValue(cellReference, out var cell) &&
                !string.IsNullOrWhiteSpace(GetCellText(workbookPart, cell))));
        }

        return counts;
    }

    private static int GetSmallCardCount(WorkbookPart workbookPart, WorksheetPart worksheetPart)
    {
        var cells = worksheetPart.Worksheet.Descendants<Cell>()
            .Where(cell => cell.CellReference?.Value is not null)
            .ToDictionary(
                cell => cell.CellReference!.Value!,
                StringComparer.OrdinalIgnoreCase);

        return SmallCardSampleCells.Count(cellReference =>
            cells.TryGetValue(cellReference, out var cell) &&
            !string.IsNullOrWhiteSpace(GetCellText(workbookPart, cell)));
    }

    private static void ClearUnusedSmallCardPdfSlots(WorkbookPart workbookPart, WorksheetPart worksheetPart)
    {
        var cardsOnPage = GetSmallCardCount(workbookPart, worksheetPart);
        for (var index = cardsOnPage; index < SmallCardPdfSlotRanges.Length; index++)
        {
            ClearSmallCardPdfSlot(worksheetPart, ParseCellRange(SmallCardPdfSlotRanges[index]));
        }
    }

    private static void ClearSmallCardPdfSlot(WorksheetPart worksheetPart, CellRange range)
    {
        RemoveMergedCellsInRange(worksheetPart, range);
        RemoveDrawingsInRange(worksheetPart, range);

        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
        if (sheetData is null)
        {
            return;
        }

        foreach (var row in sheetData.Elements<Row>())
        {
            var rowIndex = row.RowIndex?.Value ?? 0U;
            if (rowIndex < range.StartRow || rowIndex > range.EndRow)
            {
                continue;
            }

            foreach (var cell in row.Elements<Cell>())
            {
                var reference = SplitCellReference(cell.CellReference?.Value ?? "A1");
                var columnIndex = ColumnIndex(reference.Column);
                if (columnIndex < range.StartColumn || columnIndex > range.EndColumn)
                {
                    continue;
                }

                cell.CellFormula?.Remove();
                cell.CellValue = null;
                cell.DataType = null;
                cell.StyleIndex = null;
            }
        }
    }

    private static void RemoveMergedCellsInRange(WorksheetPart worksheetPart, CellRange range)
    {
        foreach (var mergeCells in worksheetPart.Worksheet.Elements<MergeCells>().ToArray())
        {
            foreach (var mergeCell in mergeCells.Elements<MergeCell>().ToArray())
            {
                var reference = mergeCell.Reference?.Value;
                if (!string.IsNullOrWhiteSpace(reference) &&
                    RangesIntersect(ParseCellRange(reference), range))
                {
                    mergeCell.Remove();
                }
            }

            if (!mergeCells.Elements<MergeCell>().Any())
            {
                mergeCells.Remove();
            }
        }
    }

    private static void RemoveDrawingsInRange(WorksheetPart worksheetPart, CellRange range)
    {
        var drawingPart = worksheetPart.DrawingsPart;
        var worksheetDrawing = drawingPart?.WorksheetDrawing;
        if (worksheetDrawing is null)
        {
            return;
        }

        foreach (var anchor in worksheetDrawing.ChildElements.ToArray())
        {
            if (TryGetDrawingAnchorRange(anchor, out var anchorRange) &&
                RangesIntersect(anchorRange, range))
            {
                anchor.Remove();
            }
        }

        worksheetDrawing.Save();
    }

    private static bool TryGetDrawingAnchorRange(OpenXmlElement anchor, out CellRange range)
    {
        var fromMarker = anchor.GetFirstChild<Xdr.FromMarker>();
        if (fromMarker is null || !TryGetMarkerCell(fromMarker, out var fromColumn, out var fromRow))
        {
            range = default;
            return false;
        }

        var toMarker = anchor.GetFirstChild<Xdr.ToMarker>();
        if (toMarker is null || !TryGetMarkerCell(toMarker, out var toColumn, out var toRow))
        {
            toColumn = fromColumn;
            toRow = fromRow;
        }

        range = new CellRange(
            Math.Min(fromColumn, toColumn),
            Math.Max(fromColumn, toColumn),
            Math.Min(fromRow, toRow),
            Math.Max(fromRow, toRow));
        return true;
    }

    private static bool TryGetMarkerCell(OpenXmlCompositeElement marker, out int column, out uint row)
    {
        column = 0;
        row = 0;
        if (!int.TryParse(marker.GetFirstChild<Xdr.ColumnId>()?.Text, CultureInfo.InvariantCulture, out var zeroBasedColumn) ||
            !uint.TryParse(marker.GetFirstChild<Xdr.RowId>()?.Text, CultureInfo.InvariantCulture, out var zeroBasedRow))
        {
            return false;
        }

        column = zeroBasedColumn + 1;
        row = zeroBasedRow + 1;
        return true;
    }

    private static CellRange ParseCellRange(string rangeReference)
    {
        var parts = rangeReference.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var start = SplitCellReference(parts[0]);
        var end = parts.Length == 1 ? start : SplitCellReference(parts[1]);
        var startColumn = ColumnIndex(start.Column);
        var endColumn = ColumnIndex(end.Column);
        return new CellRange(
            Math.Min(startColumn, endColumn),
            Math.Max(startColumn, endColumn),
            Math.Min(start.Row, end.Row),
            Math.Max(start.Row, end.Row));
    }

    private static (string Column, uint Row) SplitCellReference(string cellReference)
    {
        var column = new string(cellReference.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        var row = new string(cellReference.Where(char.IsDigit).ToArray());
        return (column, uint.Parse(row, CultureInfo.InvariantCulture));
    }

    private static int ColumnIndex(string column)
    {
        var index = 0;
        foreach (var character in column)
        {
            index = index * 26 + (character - 'A' + 1);
        }

        return index;
    }

    private static bool RangesIntersect(CellRange left, CellRange right) =>
        left.StartColumn <= right.EndColumn &&
        left.EndColumn >= right.StartColumn &&
        left.StartRow <= right.EndRow &&
        left.EndRow >= right.StartRow;

    private readonly record struct CellRange(int StartColumn, int EndColumn, uint StartRow, uint EndRow);

    private static bool SharedStringsContainCoaLargeMarkers(WorkbookPart workbookPart)
    {
        var sharedText = workbookPart.SharedStringTablePart?.SharedStringTable?.InnerText ?? string.Empty;
        return sharedText.Contains("Certificate Of Analysis", StringComparison.OrdinalIgnoreCase) &&
            sharedText.Contains("General Information", StringComparison.OrdinalIgnoreCase) &&
            sharedText.Contains("CAS Number", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SharedStringsContainCoaSmallMarkers(WorkbookPart workbookPart)
    {
        var sharedText = workbookPart.SharedStringTablePart?.SharedStringTable?.InnerText ?? string.Empty;
        return sharedText.Contains("NF-SEMI STD", StringComparison.OrdinalIgnoreCase) &&
            sharedText.Contains("Compounds", StringComparison.OrdinalIgnoreCase) &&
            sharedText.Contains("Analytical Results", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetPrintArea(WorkbookPart workbookPart, WorksheetPart worksheetPart, string areaReference)
    {
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToArray() ?? [];
        var sheetIndex = Array.FindIndex(sheets, sheet => sheet.Id?.Value == workbookPart.GetIdOfPart(worksheetPart));
        if (sheetIndex < 0)
        {
            return;
        }

        var sheetName = sheets[sheetIndex].Name?.Value;
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            return;
        }

        workbookPart.Workbook.DefinedNames ??= new DefinedNames();
        var definedNames = workbookPart.Workbook.DefinedNames;
        var localSheetId = (uint)sheetIndex;
        foreach (var existing in definedNames
            .Elements<DefinedName>()
            .Where(name => name.Name?.Value == "_xlnm.Print_Area" && name.LocalSheetId?.Value == localSheetId)
            .ToArray())
        {
            existing.Remove();
        }

        var escapedSheetName = sheetName.Replace("'", "''", StringComparison.Ordinal);
        definedNames.Append(new DefinedName
        {
            Name = "_xlnm.Print_Area",
            LocalSheetId = localSheetId,
            Text = $"'{escapedSheetName}'!{areaReference}"
        });
    }

    private static void ReplaceHeaderFooterImagesWithPdfHeaderImage(WorksheetPart worksheetPart, string pdfHeaderImagePath)
    {
        var vmlPart = worksheetPart.VmlDrawingParts.FirstOrDefault();
        if (vmlPart is null)
        {
            return;
        }

        var headerImagePart = vmlPart.AddImagePart(ImagePartType.Png);
        using (var sourceStream = File.OpenRead(pdfHeaderImagePath))
        using (var targetStream = headerImagePart.GetStream(FileMode.Create, FileAccess.Write))
        {
            sourceStream.CopyTo(targetStream);
        }

        var headerRelationshipId = vmlPart.GetIdOfPart(headerImagePart);
        XNamespace v = "urn:schemas-microsoft-com:vml";
        XNamespace o = "urn:schemas-microsoft-com:office:office";

        XDocument vmlDocument;
        using (var vmlStream = vmlPart.GetStream(FileMode.Open, FileAccess.Read))
        {
            vmlDocument = XDocument.Load(vmlStream);
        }

        foreach (var shape in vmlDocument.Descendants(v + "shape").ToArray())
        {
            var shapeId = shape.Attribute("id")?.Value;
            if (shapeId == "CH")
            {
                AddVmlShapeImageAsWorksheetImage(worksheetPart, vmlPart, shape, v, o, shapeId);
            }
            if (shapeId == "LH")
            {
                shape.SetAttributeValue(
                    "style",
                    "position:absolute;margin-left:0;margin-top:0;width:550pt;height:110.6pt;z-index:2");
                shape.Element(v + "imagedata")?.SetAttributeValue(o + "relid", headerRelationshipId);
            }
            else if (shapeId == "RH")
            {
                shape.Remove();
            }
        }

        using (var vmlStream = vmlPart.GetStream(FileMode.Create, FileAccess.Write))
        {
            vmlDocument.Save(vmlStream);
        }

        var headerFooter = worksheetPart.Worksheet.Elements<HeaderFooter>().FirstOrDefault();
        if (headerFooter?.OddHeader is not null)
        {
            headerFooter.OddHeader.Text = "&C\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n";
        }
    }

    private static byte[]? TryConvertWithExcel(byte[] workbookContent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var excelType = Type.GetTypeFromProgID("Excel.Application");
        if (excelType is null)
        {
            return null;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"gas-qc-coa-excel-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        dynamic? excel = null;
        dynamic? workbook = null;

        try
        {
            var workbookPath = Path.Combine(tempDirectory, "input.xlsx");
            var pdfPath = Path.Combine(tempDirectory, "input.pdf");
            File.WriteAllBytes(workbookPath, workbookContent);

            excel = Activator.CreateInstance(excelType);
            if (excel is null)
            {
                return null;
            }

            excel.Visible = false;
            excel.DisplayAlerts = false;
            workbook = excel.Workbooks.Open(workbookPath);
            workbook.ExportAsFixedFormat(0, pdfPath);
            workbook.Close(false);
            workbook = null;
            excel.Quit();
            excel = null;

            return File.Exists(pdfPath) ? File.ReadAllBytes(pdfPath) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseExcelComObject(workbook, closeWorkbook: true);
            ReleaseExcelComObject(excel, closeWorkbook: false);
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void ReleaseExcelComObject(dynamic? comObject, bool closeWorkbook)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            if (closeWorkbook)
            {
                comObject.Close(false);
            }
            else
            {
                comObject.Quit();
            }
        }
        catch
        {
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
        }
    }

    private static void AddVmlShapeImageAsWorksheetImage(
        WorksheetPart worksheetPart,
        VmlDrawingPart vmlPart,
        XElement shape,
        XNamespace v,
        XNamespace o,
        string shapeId)
    {
        var relationshipId = shape.Element(v + "imagedata")?.Attribute(o + "relid")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId) ||
            vmlPart.GetPartById(relationshipId) is not ImagePart sourceImagePart)
        {
            return;
        }

        var drawingsPart = worksheetPart.DrawingsPart;
        if (drawingsPart?.WorksheetDrawing is null)
        {
            drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
            drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
            worksheetPart.Worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });
        }

        var imagePart = drawingsPart.AddImagePart(sourceImagePart.ContentType);
        using (var sourceStream = sourceImagePart.GetStream(FileMode.Open, FileAccess.Read))
        using (var targetStream = imagePart.GetStream(FileMode.Create, FileAccess.Write))
        {
            sourceStream.CopyTo(targetStream);
        }

        drawingsPart.WorksheetDrawing.Append(CreateOneCellImageAnchor(
            drawingsPart.WorksheetDrawing,
            drawingsPart.GetIdOfPart(imagePart),
            $"COA PDF {shapeId}",
            ResolvePdfHeaderColumnId(shapeId),
            ResolvePdfHeaderRowId(shapeId),
            ResolvePdfHeaderWidthPoints(shapeId),
            ResolvePdfHeaderHeightPoints(shapeId)));
        drawingsPart.WorksheetDrawing.Save();
    }

    private static string ResolvePdfHeaderColumnId(string shapeId) =>
        shapeId switch
        {
            "RH" => "4",
            "CH" => "2",
            _ => "0"
        };

    private static string ResolvePdfHeaderRowId(string shapeId) =>
        shapeId == "CH" ? "20" : "0";

    private static long ResolvePdfHeaderWidthPoints(string shapeId) =>
        shapeId switch
        {
            "RH" => 181,
            "CH" => 162,
            _ => 348
        };

    private static long ResolvePdfHeaderHeightPoints(string shapeId) =>
        shapeId switch
        {
            "RH" => 102,
            "CH" => 190,
            _ => 78
        };

    private static Xdr.OneCellAnchor CreateOneCellImageAnchor(
        Xdr.WorksheetDrawing worksheetDrawing,
        string relationshipId,
        string name,
        string columnId,
        string rowId,
        long widthPoints,
        long heightPoints)
    {
        const long emusPerPoint = 12700;
        var widthEmus = widthPoints * emusPerPoint;
        var heightEmus = heightPoints * emusPerPoint;
        var drawingId = worksheetDrawing
            .Descendants<Xdr.NonVisualDrawingProperties>()
            .Select(properties => properties.Id?.Value ?? 0U)
            .DefaultIfEmpty(0U)
            .Max() + 1;

        var picture = new Xdr.Picture(
            new Xdr.NonVisualPictureProperties(
                new Xdr.NonVisualDrawingProperties
                {
                    Id = drawingId,
                    Name = name
                },
                new Xdr.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true })),
            new Xdr.BlipFill(
                new A.Blip { Embed = relationshipId },
                new A.Stretch(new A.FillRectangle())),
            new Xdr.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = 0, Y = 0 },
                    new A.Extents { Cx = widthEmus, Cy = heightEmus }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));

        return new Xdr.OneCellAnchor(
            new Xdr.FromMarker(
                new Xdr.ColumnId(columnId),
                new Xdr.ColumnOffset("0"),
                new Xdr.RowId(rowId),
                new Xdr.RowOffset("0")),
            new Xdr.Extent { Cx = widthEmus, Cy = heightEmus },
            picture,
            new Xdr.ClientData());
    }

    private static class SmallCardCompanyNameOverlay
    {
        private const double HeaderWhiteoutLeftPoints = 52D;
        private const double HeaderWhiteoutTopPoints = 55D;
        private const double HeaderImageLeftPoints = 52.5D;
        private const double HeaderImageTopPoints = 64D;
        private const double HeaderImageColumnPitchPoints = 150D;
        private const double HeaderImageRowPitchPoints = 218D;
        private const double HeaderImageWidthPoints = 135D;
        private const double HeaderWhiteoutWidthPoints = 147D;
        private const double HeaderWhiteoutHeightPoints = 31D;

        public static byte[] Add(
            byte[] pdfContent,
            IReadOnlyList<int> cardCounts,
            string headerImagePath)
        {
            if (!File.Exists(headerImagePath) || cardCounts.Count == 0)
            {
                return pdfContent;
            }

            using var pdfStream = new MemoryStream(pdfContent);
            using var document = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Modify);
            if (document.PageCount != cardCounts.Count)
            {
                throw new InvalidOperationException(
                    $"COA small PDF rendered {document.PageCount} pages for {cardCounts.Count} worksheet pages.");
            }

            using var image = XImage.FromFile(headerImagePath);
            if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return pdfContent;
            }

            var headerImageHeight = HeaderImageWidthPoints * image.PixelHeight / image.PixelWidth;

            for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                var page = document.Pages[pageIndex];
                using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                for (var cardIndex = 0; cardIndex < cardCounts[pageIndex]; cardIndex++)
                {
                    var column = cardIndex % SmallCardColumnsPerPage;
                    var row = cardIndex / SmallCardColumnsPerPage;
                    graphics.DrawRectangle(
                        XBrushes.White,
                        HeaderWhiteoutLeftPoints + (column * HeaderImageColumnPitchPoints),
                        HeaderWhiteoutTopPoints + (row * HeaderImageRowPitchPoints),
                        HeaderWhiteoutWidthPoints,
                        HeaderWhiteoutHeightPoints);
                }

                for (var cardIndex = 0; cardIndex < cardCounts[pageIndex]; cardIndex++)
                {
                    var column = cardIndex % SmallCardColumnsPerPage;
                    var row = cardIndex / SmallCardColumnsPerPage;
                    graphics.DrawImage(
                        image,
                        HeaderImageLeftPoints + (column * HeaderImageColumnPitchPoints),
                        HeaderImageTopPoints + (row * HeaderImageRowPitchPoints),
                        HeaderImageWidthPoints,
                        headerImageHeight);
                }
            }

            using var output = new MemoryStream();
            document.Save(output, closeStream: false);
            return output.ToArray();
        }
    }

    private static class PdfHeaderImageOverlay
    {
        public static byte[] Add(byte[] pdfContent, string imagePath)
        {
            return AddWithPdfSharp(pdfContent, imagePath);
        }

        private static byte[] AddWithPdfSharp(byte[] pdfContent, string imagePath)
        {
            using var pdfStream = new MemoryStream(pdfContent);
            using var document = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Modify);
            using var image = XImage.FromFile(imagePath);

            foreach (var page in document.Pages)
            {
                var pageWidth = page.Width.Point;
                var headerWidth = Math.Min(559D, Math.Max(0D, pageWidth - 36D));
                if (headerWidth <= 0D || image.PixelWidth <= 0)
                {
                    continue;
                }

                var headerHeight = headerWidth * image.PixelHeight / image.PixelWidth;
                var headerX = (pageWidth - headerWidth) / 2D;
                const double headerY = 7D;

                using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                graphics.DrawImage(image, headerX, headerY, headerWidth, headerHeight);
            }

            using var output = new MemoryStream();
            document.Save(output, closeStream: false);
            return output.ToArray();
        }

    }

    private byte[] ConvertWithFallback(byte[] workbookContent, string workbookFileName)
    {
        if (!_options.UseBasicPdfFallback)
        {
            throw new InvalidOperationException(
                "LibreOffice PDF converter was not found. Set Scheduler:CoaExport:LibreOfficePath or enable Scheduler:CoaExport:UseBasicPdfFallback.");
        }

        return BasicSpreadsheetPdfExporter.Export(workbookContent, workbookFileName);
    }

    private static bool IsExecutableAvailable(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return paths.Any(path =>
        {
            var candidate = Path.Combine(path, OperatingSystem.IsWindows() ? $"{fileName}.exe" : fileName);
            return File.Exists(candidate);
        });
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var completedTask = await Task.WhenAny(exitTask, Task.Delay(timeout, cancellationToken));
        return completedTask == exitTask;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static class BasicSpreadsheetPdfExporter
    {
        private const int MaxLinesPerPage = 48;

        public static byte[] Export(byte[] workbookContent, string workbookFileName)
        {
            var lines = ExtractLines(workbookContent, workbookFileName);
            var pages = lines
                .Chunk(MaxLinesPerPage)
                .Select(chunk => chunk.ToArray())
                .ToArray();

            return BuildPdf(pages.Length == 0 ? [Array.Empty<string>()] : pages);
        }

        private static IReadOnlyList<string> ExtractLines(byte[] workbookContent, string workbookFileName)
        {
            using var workbook = new XLWorkbook(new MemoryStream(workbookContent));
            var lines = new List<string>
            {
                Path.GetFileNameWithoutExtension(workbookFileName)
            };

            foreach (var worksheet in workbook.Worksheets)
            {
                lines.Add(worksheet.Name);
                var range = worksheet.RangeUsed();
                if (range is null)
                {
                    continue;
                }

                foreach (var row in range.RowsUsed())
                {
                    var values = row.CellsUsed()
                        .Select(cell => FormatCellValue(cell))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToArray();

                    if (values.Length > 0)
                    {
                        lines.Add(string.Join("  ", values));
                    }
                }
            }

            return lines;
        }

        private static string FormatCellValue(IXLCell cell)
        {
            if (cell.Value.IsBlank)
            {
                return string.Empty;
            }

            if (cell.Value.IsNumber)
            {
                return cell.Value.GetNumber().ToString("0.####", CultureInfo.InvariantCulture);
            }

            if (cell.Value.IsDateTime)
            {
                return cell.Value.GetDateTime().ToString("yyyy/M/d", CultureInfo.InvariantCulture);
            }

            return cell.GetFormattedString();
        }

        private static byte[] BuildPdf(IReadOnlyList<IReadOnlyList<string>> pages)
        {
            var objectCount = 3 + pages.Count * 2;
            var objects = new string[objectCount + 1];
            var pageObjectIds = new List<int>();

            objects[1] = "<< /Type /Catalog /Pages 2 0 R >>";
            objects[3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>";

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var pageObjectId = 4 + pageIndex * 2;
                var contentObjectId = pageObjectId + 1;
                pageObjectIds.Add(pageObjectId);

                var content = BuildPageContent(pages[pageIndex]);
                objects[pageObjectId] =
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectId} 0 R >>";
                objects[contentObjectId] =
                    $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream";
            }

            objects[2] = $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>";

            using var stream = new MemoryStream();
            WriteAscii(stream, "%PDF-1.4\n");
            var offsets = new long[objectCount + 1];
            for (var objectId = 1; objectId <= objectCount; objectId++)
            {
                offsets[objectId] = stream.Position;
                WriteAscii(stream, $"{objectId} 0 obj\n{objects[objectId]}\nendobj\n");
            }

            var xrefOffset = stream.Position;
            WriteAscii(stream, $"xref\n0 {objectCount + 1}\n");
            WriteAscii(stream, "0000000000 65535 f \n");
            for (var objectId = 1; objectId <= objectCount; objectId++)
            {
                WriteAscii(stream, $"{offsets[objectId].ToString("0000000000", CultureInfo.InvariantCulture)} 00000 n \n");
            }

            WriteAscii(stream, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
            return stream.ToArray();
        }

        private static string BuildPageContent(IReadOnlyList<string> lines)
        {
            var builder = new StringBuilder();
            builder.AppendLine("BT");
            builder.AppendLine("/F1 9 Tf");
            builder.AppendLine("40 800 Td");

            foreach (var line in lines)
            {
                builder.Append('(')
                    .Append(EscapePdfText(ToPdfAscii(line)))
                    .AppendLine(") Tj");
                builder.AppendLine("0 -15 Td");
            }

            builder.AppendLine("ET");
            return builder.ToString();
        }

        private static string ToPdfAscii(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(character is >= ' ' and <= '~' ? character : '?');
            }

            return builder.ToString();
        }

        private static string EscapePdfText(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
