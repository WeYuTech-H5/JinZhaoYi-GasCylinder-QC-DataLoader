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
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class SpreadsheetPdfConverter(IOptions<SchedulerOptions> options) : ISpreadsheetPdfConverter
{
    private const string SmallCardCompanyName = "金 兆 益 科 技 股 份 有 限 公 司";
    private const string SmallCardPdfPrintArea = "$B$2:$AA$66";
    private const string DfKaiFontPath = @"C:\Windows\Fonts\kaiu.ttf";
    private const double A4PageWidthPoints = 595.275590551D;
    private const double A4PageHeightPoints = 841.88976378D;
    private const double SmallCardMarginInches = 0.25D;
    private const double SmallCardWidthPoints = 246.6D;
    private const double SmallCardHeightPoints = 360.5D;
    private const double SmallCardHorizontalGapPoints = 2.4D;
    private const double SmallCardVerticalGapPoints = 3.75D;
    private const int SmallCardColumnsPerPage = 3;
    private const int SmallCardRowsPerPage = 3;
    private static readonly string[] SmallCardSampleCells =
    [
        "F6", "O6", "X6",
        "F28", "O28", "X28",
        "F50", "O50", "X50"
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
        var smallCardCounts = isCoaSmallWorkbook
            ? GetSmallCardCounts(pdfWorkbookContent)
            : [];

        if (!isCoaLargeWorkbook)
        {
            var excelPdfContent = TryConvertWithExcel(pdfWorkbookContent);
            if (excelPdfContent is not null)
            {
                return isCoaSmallWorkbook
                    ? SmallCardCompanyNameOverlay.Add(excelPdfContent, smallCardCounts)
                    : excelPdfContent;
            }
        }

        if (string.IsNullOrWhiteSpace(_options.LibreOfficePath) &&
            !IsExecutableAvailable("soffice"))
        {
            if (isCoaLargeWorkbook)
            {
                throw new InvalidOperationException(
                    "COA large PDF export requires LibreOffice to keep rendering consistent across machines.");
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
            return isCoaSmallWorkbook
                ? SmallCardCompanyNameOverlay.Add(pdfContent, smallCardCounts)
                : pdfContent;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (isCoaLargeWorkbook)
            {
                throw new InvalidOperationException(
                    "COA large PDF export requires LibreOffice to keep rendering consistent across machines.",
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

        // Use one explicit scale calculated from A4, margins, card size, and gaps.
        // Each worksheet is one 3 x 3 page; rows through 66 include the bottom signature line.
        pageMargins.Left = SmallCardMarginInches;
        pageMargins.Right = SmallCardMarginInches;
        pageMargins.Top = SmallCardMarginInches;
        pageMargins.Bottom = SmallCardMarginInches;
        pageMargins.Header = 0D;
        pageMargins.Footer = 0D;

        var printOptions = worksheet.GetFirstChild<PrintOptions>();
        if (printOptions is null)
        {
            printOptions = new PrintOptions();
            worksheet.InsertBefore(printOptions, pageMargins);
        }

        printOptions.HorizontalCentered = true;
        printOptions.VerticalCentered = true;

        var pageSetup = worksheet.GetFirstChild<PageSetup>();
        if (pageSetup is null)
        {
            pageSetup = new PageSetup();
            worksheet.Append(pageSetup);
        }

        pageSetup.PaperSize = 9U; // A4
        pageSetup.Orientation = OrientationValues.Portrait;
        pageSetup.Scale = CalculateSmallCardPdfScale();
        pageSetup.FitToWidth = null;
        pageSetup.FitToHeight = null;

        SetPrintArea(workbookPart, worksheetPart, SmallCardPdfPrintArea);
        HideSmallCardCompanyNamesForPdf(worksheetPart);
        worksheet.Save();
    }

    private static uint CalculateSmallCardPdfScale()
    {
        var printableWidth = A4PageWidthPoints - SmallCardMarginInches * 2D * 72D;
        var printableHeight = A4PageHeightPoints - SmallCardMarginInches * 2D * 72D;
        var gridWidth = CalculateSmallCardGridWidthPoints();
        var gridHeight = CalculateSmallCardGridHeightPoints();
        var widthScale = printableWidth / gridWidth * 100D;
        var heightScale = printableHeight / gridHeight * 100D;

        // Reserve one percentage point for LibreOffice printer-unit rounding.
        return (uint)Math.Max(10D, Math.Floor(Math.Min(widthScale, heightScale)) - 1D);
    }

    private static double CalculateSmallCardGridWidthPoints() =>
        SmallCardColumnsPerPage * SmallCardWidthPoints +
        (SmallCardColumnsPerPage - 1) * SmallCardHorizontalGapPoints;

    private static double CalculateSmallCardGridHeightPoints() =>
        SmallCardRowsPerPage * SmallCardHeightPoints +
        (SmallCardRowsPerPage - 1) * SmallCardVerticalGapPoints;

    private static void HideSmallCardCompanyNamesForPdf(WorksheetPart worksheetPart)
    {
        if (!File.Exists(DfKaiFontPath))
        {
            return;
        }

        var drawing = worksheetPart.DrawingsPart?.WorksheetDrawing;
        if (drawing is null)
        {
            return;
        }

        var changed = false;
        foreach (var text in drawing.Descendants<A.Text>()
            .Where(text => string.Equals(text.Text, SmallCardCompanyName, StringComparison.Ordinal)))
        {
            text.Text = string.Empty;
            changed = true;
        }

        if (changed)
        {
            drawing.Save();
        }
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
        private const string FontFaceName = "DFKai-SB";
        private const double CompanyTextBoxWidthPoints = 188.3D;
        private const double CompanyTextCenterOffsetPoints = 153D;
        private const double CompanyTextTopOffsetPoints = 6D;
        private const double CompanyTextHorizontalScale = 0.87D;
        private const double CompanyTextFontSizePoints = 12D;
        private static readonly object FontResolverLock = new();

        public static byte[] Add(byte[] pdfContent, IReadOnlyList<int> cardCounts)
        {
            if (!File.Exists(DfKaiFontPath) || cardCounts.Count == 0)
            {
                return pdfContent;
            }

            EnsureFontResolver();
            using var pdfStream = new MemoryStream(pdfContent);
            using var document = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Modify);
            if (document.PageCount != cardCounts.Count)
            {
                throw new InvalidOperationException(
                    $"COA small PDF rendered {document.PageCount} pages for {cardCounts.Count} worksheet pages.");
            }

            var scale = CalculateSmallCardPdfScale() / 100D;
            var gridWidth = CalculateSmallCardGridWidthPoints() * scale;
            var gridHeight = CalculateSmallCardGridHeightPoints() * scale;
            var columnPitch = (SmallCardWidthPoints + SmallCardHorizontalGapPoints) * scale;
            var rowPitch = (SmallCardHeightPoints + SmallCardVerticalGapPoints) * scale;
            var textBoxWidth = CompanyTextBoxWidthPoints * scale;
            var font = new XFont(
                FontFaceName,
                CompanyTextFontSizePoints * scale,
                XFontStyleEx.Bold,
                new XPdfFontOptions(PdfFontEncoding.Unicode));

            for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                var page = document.Pages[pageIndex];
                var gridLeft = (page.Width.Point - gridWidth) / 2D;
                var gridTop = (page.Height.Point - gridHeight) / 2D;
                using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                for (var cardIndex = 0; cardIndex < cardCounts[pageIndex]; cardIndex++)
                {
                    var column = cardIndex % SmallCardColumnsPerPage;
                    var row = cardIndex / SmallCardColumnsPerPage;
                    var centerX =
                        gridLeft +
                        (CompanyTextCenterOffsetPoints * scale) +
                        column * columnPitch;
                    var top =
                        gridTop +
                        (CompanyTextTopOffsetPoints * scale) +
                        row * rowPitch;
                    var bounds = new XRect(
                        -textBoxWidth / 2D,
                        top,
                        textBoxWidth,
                        14D * scale);

                    var state = graphics.Save();
                    graphics.TranslateTransform(centerX, 0D);
                    graphics.ScaleTransform(CompanyTextHorizontalScale, 1D);
                    graphics.DrawString(
                        SmallCardCompanyName,
                        font,
                        XBrushes.Black,
                        bounds,
                        XStringFormats.Center);
                    graphics.Restore(state);
                }
            }

            using var output = new MemoryStream();
            document.Save(output, closeStream: false);
            return output.ToArray();
        }

        private static void EnsureFontResolver()
        {
            if (GlobalFontSettings.FontResolver is DfKaiFontResolver)
            {
                return;
            }

            lock (FontResolverLock)
            {
                if (GlobalFontSettings.FontResolver is null)
                {
                    GlobalFontSettings.FontResolver = new DfKaiFontResolver();
                }
            }
        }

        private sealed class DfKaiFontResolver : IFontResolver
        {
            private static readonly byte[] FontBytes = File.ReadAllBytes(DfKaiFontPath);

            public byte[]? GetFont(string faceName) =>
                string.Equals(faceName, FontFaceName, StringComparison.OrdinalIgnoreCase)
                    ? FontBytes
                    : null;

            public FontResolverInfo? ResolveTypeface(
                string familyName,
                bool isBold,
                bool isItalic) =>
                string.Equals(familyName, FontFaceName, StringComparison.OrdinalIgnoreCase)
                    ? new FontResolverInfo(FontFaceName, isBold, isItalic)
                    : null;
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
