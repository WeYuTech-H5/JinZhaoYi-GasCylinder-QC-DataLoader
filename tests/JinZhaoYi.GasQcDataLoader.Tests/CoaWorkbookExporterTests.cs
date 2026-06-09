using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using System.Reflection;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Options;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class CoaWorkbookExporterTests
{
    [Fact]
    public void ExportLargeForDownload_uses_container_sheet_for_standard_template()
    {
        var exporter = CreateExporter();
        var halfLiter = CreateRow("STD-N050", "0.5L_Cylinder");
        halfLiter.ParentExpirationDate = new DateTime(2026, 9, 15);
        halfLiter.Areas["Acetone"] = 101.2m;
        var oneLiter = CreateRow("STD-L100", "1L_Cylinder");
        oneLiter.ParentExpirationDate = new DateTime(2026, 9, 15);
        oneLiter.Areas["Acetone"] = 98.6m;

        var download = exporter.ExportLargeForDownload([halfLiter, oneLiter], "20260521", CoaLargeTemplateType.Standard);

        download.FileName.Should().Be("COA(大卡)_20260521.xlsx");
        using var workbook = Open(download);
        workbook.Worksheets.Select(sheet => sheet.Name).Should().BeEquivalentTo("COA_STD-N050", "COA_STD-L100");
        workbook.Worksheet("COA_STD-N050").Cell("E52").GetDouble().Should().BeApproximately(101.2, 0.0001);
        workbook.Worksheet("COA_STD-L100").Cell("E52").GetDouble().Should().BeApproximately(98.6, 0.0001);
        workbook.Worksheet("COA_STD-N050").Cell("B10").GetString().Should().Be("STD Gas PC for Semiconductor");
        workbook.Worksheet("COA_STD-L100").Cell("B10").GetString().Should().Be("NF-SEMI STD");
        workbook.Worksheet("COA_STD-N050").Cell("B11").GetString().Should().Be("PG000-0006");
        workbook.Worksheet("COA_STD-L100").Cell("B11").GetString().Should().Be("PG000-0100");
        workbook.Worksheet("COA_STD-N050").Cell("B13").GetString().Should().Be("2026/9/15");
        workbook.Worksheet("COA_STD-L100").Cell("B13").GetString().Should().Be("2027/5/20");
        workbook.Worksheet("COA_STD-N050").Cell("B14").GetString().Should().Be("5 cm*35cm");
        workbook.Worksheet("COA_STD-L100").Cell("B14").GetString().Should().Be("8.87 cm*27.7 cm");
        workbook.Worksheet("COA_STD-N050").Cell("B16").GetString().Should().Be("950 psi");
        workbook.Worksheet("COA_STD-L100").Cell("B16").GetString().Should().Be("1000 psi");
        workbook.Worksheet("COA_STD-N050").Cell("E11").GetString().Should().Be("500 mL");
        workbook.Worksheet("COA_STD-L100").Cell("E11").GetString().Should().Be("1000 mL");
        workbook.Worksheet("COA_STD-N050").Cell("E13").GetString().Should().Be("41 L");
        workbook.Worksheet("COA_STD-L100").Cell("E13").GetString().Should().Be("70 L");
        workbook.Worksheet("COA_STD-N050").Cell("E16").GetString().Should().Be("±10%");
        workbook.Worksheet("COA_STD-L100").Cell("E16").GetString().Should().Be("±15%");
        HasWorksheetDrawing(download, "COA_STD-N050").Should().BeTrue();
        HasWorksheetDrawing(download, "COA_STD-L100").Should().BeTrue();
    }

    [Theory]
    [InlineData("STD-N088", "PG000-0006")]
    [InlineData("STD-T003", "PG000-0006")]
    [InlineData("AZ-001", "PG000-0006")]
    [InlineData("TSMC-012", "PG000-0016")]
    [InlineData("VSMC-001", "PG000-0010")]
    [InlineData("STD-L007", "PG000-0100")]
    public void ExportLargeForDownload_uses_sample_name_prefix_for_product_number(string sampleName, string expectedProductNumber)
    {
        var exporter = CreateExporter();
        var row = CreateRow(sampleName, "0.5L_Cylinder");

        var download = exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Standard);

        using var workbook = Open(download);
        workbook.Worksheets.Single().Cell("B11").GetString().Should().Be(expectedProductNumber);
    }

    [Theory]
    [InlineData("STD-N088", "PG000-0006")]
    [InlineData("TSMC-012", "PG000-0016")]
    public void ExportLargeForDownload_renders_corrected_product_numbers_in_default_black(string sampleName, string expectedProductNumber)
    {
        var exporter = CreateExporter();
        var row = CreateRow(sampleName, "0.5L_Cylinder");

        var download = exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Standard);

        using var workbook = Open(download);
        workbook.Worksheets.Single().Cell("B11").GetString().Should().Be(expectedProductNumber);
        GetCellFontRgb(download, $"COA_{sampleName}", "B11").Should().BeNull();
    }

    [Fact]
    public void ExportLargeForDownload_uses_yadong_sheet_when_requested()
    {
        var exporter = CreateExporter();
        var row = CreateRow("STD-YD", "0.5L_Cylinder");
        row.Areas["Acetone"] = 123m;

        var download = exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Yadong);

        using var workbook = Open(download);
        var sheet = workbook.Worksheet("COA_STD-YD");
        sheet.Cell("A20").GetString().Should().Be("Acetone");
        sheet.Cell("E20").GetDouble().Should().Be(123);
        sheet.Cell("B11").GetString().Should().Be("PG000-0010");
    }

    [Fact]
    public void ExportLargeForDownload_ignores_missing_configured_header_image()
    {
        var headerImagePath = Path.Combine(Path.GetTempPath(), $"missing-COA-header-{Guid.NewGuid():N}.png");
        var exporter = CreateExporter(largeHeaderImagePath: headerImagePath);
        var row = CreateRow("STD-IMG", "0.5L_Cylinder");

        var download = exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Standard);

        HasWorksheetDrawing(download, "COA_STD-IMG").Should().BeTrue();
    }

    [Fact]
    public void ExportLargeForDownload_preserves_template_header_image_without_external_image()
    {
        var exporter = CreateExporter();
        var row = CreateRow("STD-REALIMG", "0.5L_Cylinder");

        var download = exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Standard);

        HasWorksheetDrawing(download, "COA_STD-REALIMG").Should().BeTrue();
    }

    [Fact]
    public void PdfConversionWorkbook_uses_static_pdf_header_and_preserves_excel_print_layout()
    {
        var exporter = CreateExporter();
        var row = CreateRow("STD-PDF", "0.5L_Cylinder");
        var download = exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Standard);
        var originalContent = download.Content.ToArray();

        using var headerImage = new TemporaryPdfHeaderImage();
        var preparedContent = PrepareWorkbookForPdfConversion(originalContent);

        HasLegacyHeaderFooterDrawing(download, "COA_STD-PDF").Should().BeTrue();
        originalContent.Should().Equal(download.Content);

        var preparedDownload = new CoaWorkbookDownload(preparedContent, download.ContentType, download.FileName);
        HasLegacyHeaderFooterDrawing(preparedDownload, "COA_STD-PDF").Should().BeTrue();
        HasWorksheetDrawing(preparedDownload, "COA_STD-PDF").Should().BeTrue();
    }

    [Fact]
    public void ExportLargeForDownload_reports_missing_template_path()
    {
        var templatePath = Path.Combine(Path.GetTempPath(), $"missing-COA-large-{Guid.NewGuid():N}.xlsx");
        var exporter = CreateExporter(templatePath);
        var row = CreateRow("STD-MISSING", "0.5L_Cylinder");

        var act = () => exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Standard);

        act.Should().Throw<FileNotFoundException>()
            .WithMessage($"COA template not found: {templatePath}");
    }

    [Fact]
    public void ExportLargeForDownload_uses_cas_number_to_resolve_result_columns()
    {
        var templatePath = Path.Combine(Path.GetTempPath(), $"COA-large-cas-{Guid.NewGuid():N}.xlsx");
        File.Copy(ResolveTemplatePath("COA(大卡).xlsx"), templatePath);

        try
        {
            using (var template = new XLWorkbook(templatePath))
            {
                var sheet = template.Worksheet("COA(1 L)");
                sheet.Cell("A19").Value = "Changed component text";
                sheet.Cell("A40").Value = "Changed xylene text";
                template.Save();
            }

            var exporter = CreateExporter(templatePath);
            var row = CreateRow("STD-CAS", "1L_Cylinder");
            row.Areas["Freon114"] = 321m;
            row.Areas["p-Xylene"] = 654m;

            var download = exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Standard);

            using var workbook = Open(download);
            var output = workbook.Worksheet("COA_STD-CAS");
            output.Cell("A19").GetString().Should().Be("Changed component text");
            output.Cell("E19").GetDouble().Should().Be(321);
            output.Cell("A40").GetString().Should().Be("Changed xylene text");
            output.Cell("E40").GetDouble().Should().Be(654);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public void ExportSmallForDownload_places_each_selected_row_once_on_same_sheet()
    {
        var exporter = CreateExporter();
        var rows = Enumerable.Range(1, 2)
            .Select(index =>
            {
                var row = CreateRow($"STD-N{index:000}", "1L_Cylinder", index);
                row.ProdBomb1LotNo = $"BOMB1-{index:000}";
                row.Areas["Acetone"] = 90 + index;
                row.Areas["IPA"] = 100 + index;
                return row;
            })
            .ToArray();

        var download = exporter.ExportSmallForDownload(rows, "20260521", 9);

        download.FileName.Should().Be("COA(小卡)_20260521.xlsx");
        using var workbook = Open(download);
        workbook.Worksheets.Count.Should().Be(1);
        var firstSheet = workbook.Worksheet("COA小卡1");
        firstSheet.Cell("F6").GetString().Should().Be("STD-N001");
        firstSheet.Cell("O6").GetString().Should().Be("STD-N002");
        firstSheet.Cell("X6").GetString().Should().BeEmpty();
        firstSheet.Cell("F8").GetDouble().Should().Be(91);
        firstSheet.Cell("O8").GetDouble().Should().Be(92);
        firstSheet.Cell("B19").GetString().Should().Be("母瓶 NO.  BOMB1-001");
        firstSheet.Cell("K19").GetString().Should().Be("母瓶 NO.  BOMB1-002");
    }

    [Fact]
    public void ExportSmallForDownload_sets_each_sheet_to_nine_card_a4_print_area()
    {
        var exporter = CreateExporter();
        var rows = Enumerable.Range(1, 10)
            .Select(index => CreateRow($"STD-N{index:000}", "1L_Cylinder", index))
            .ToArray();

        var download = exporter.ExportSmallForDownload(rows, "20260521", 9);
        var sheetNames = GetSheetNames(download);

        GetPrintArea(download, sheetNames[0]).Should().Be($"'{sheetNames[0]}'!$B$2:$AA$66");
        GetPrintArea(download, sheetNames[1]).Should().Be($"'{sheetNames[1]}'!$B$2:$AA$66");
        GetSmallSheetPageSetup(download, sheetNames[0]).Should().Be((true, 9U, 1U, 1U));
    }

    [Fact]
    public void ExportSmallForDownload_rejects_invalid_cards_per_page()
    {
        var exporter = CreateExporter();

        var act = () => exporter.ExportSmallForDownload([CreateRow("STD-001", "1L_Cylinder")], "20260521", 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*cardsPerPage*");
    }

    [Fact]
    public void ExportSmallForDownload_removes_unused_card_slots()
    {
        var exporter = CreateExporter();
        var row = CreateRow("STD-N004", "1L_Cylinder");
        row.Areas["Acetone"] = 94m;

        var download = exporter.ExportSmallForDownload([row], "20260521", 9);

        using var workbook = Open(download);
        var sheet = workbook.Worksheet("COA小卡1");
        sheet.Cell("F6").GetString().Should().Be("STD-N004");
        sheet.Cell("O6").GetString().Should().BeEmpty();
        sheet.Cell("K2").Style.Border.TopBorder.Should().Be(XLBorderStyleValues.None);
        sheet.Cell("O28").GetString().Should().BeEmpty();
        sheet.Cell("K24").Style.Border.TopBorder.Should().Be(XLBorderStyleValues.None);
        sheet.Cell("X50").GetString().Should().BeEmpty();
        sheet.Cell("T46").Style.Border.TopBorder.Should().Be(XLBorderStyleValues.None);
        HasDrawingInRange(download, "COA小卡1", "K2:R20").Should().BeFalse();
        HasDrawingInRange(download, "COA小卡1", "K24:R42").Should().BeFalse();
        HasDrawingInRange(download, "COA小卡1", "T46:AA64").Should().BeFalse();
    }

    [Fact]
    public void ExportSmallForDownload_keeps_only_used_card_signature_rows()
    {
        var exporter = CreateExporter();

        var download = exporter.ExportSmallForDownload([CreateRow("STD-N004", "1L_Cylinder")], "20260521", 9);

        using var workbook = Open(download);
        var sheet = workbook.Worksheet(GetSheetNames(download)[0]);
        sheet.Cell("B22").GetString().Should().NotBeEmpty();
        sheet.Cell("F22").GetString().Should().NotBeEmpty();

        foreach (var cellReference in new[] { "K22", "O22", "T22", "X22", "B44", "F44", "K44", "O44", "T44", "X44", "B66", "F66", "K66", "O66", "T66", "X66" })
        {
            sheet.Cell(cellReference).GetString().Should().BeEmpty();
        }
    }

    [Fact]
    public void ExportSmallForDownload_splits_more_than_nine_selected_rows_to_next_sheet()
    {
        var exporter = CreateExporter();
        var rows = Enumerable.Range(1, 10)
            .Select(index =>
            {
                var row = CreateRow($"STD-N{index:000}", "1L_Cylinder", index);
                row.Areas["Acetone"] = 90 + index;
                return row;
            })
            .ToArray();

        var download = exporter.ExportSmallForDownload(rows, "20260521", 9);

        using var workbook = Open(download);
        workbook.Worksheets.Count.Should().Be(2);
        workbook.Worksheet("COA小卡1").Cell("X50").GetString().Should().Be("STD-N009");

        var secondSheet = workbook.Worksheet("COA小卡2");
        secondSheet.Cell("F6").GetString().Should().Be("STD-N010");
        secondSheet.Cell("O6").GetString().Should().BeEmpty();
        secondSheet.Cell("K2").Style.Border.TopBorder.Should().Be(XLBorderStyleValues.None);
        HasDrawingInRange(download, "COA小卡2", "K2:R20").Should().BeFalse();
    }

    private static CoaWorkbookExporter CreateExporter(string? largeTemplatePath = null, string? largeHeaderImagePath = null) =>
        new(Options.Create(new SchedulerOptions
        {
            CsvExport = new SchedulerCsvExportOptions { RawLotId = "CC-706988" },
            CoaExport = new SchedulerCoaExportOptions
            {
                LargeTemplatePath = largeTemplatePath ?? ResolveTemplatePath("COA(大卡).xlsx"),
                LargeHeaderImagePath = largeHeaderImagePath,
                SmallTemplatePath = ResolveTemplatePath("COA(小卡).xlsx")
            }
        }));

    private static QcDataRow CreateRow(string sampleName, string container, int sampleNo = 1) =>
        new()
        {
            AnlzTime = new DateTime(2026, 5, 21, 10, sampleNo, 0),
            LotNo = "20260521001",
            SampleName = sampleName,
            SampleNo = sampleNo,
            Container = container
        };

    private static XLWorkbook Open(CoaWorkbookDownload download) =>
        new(new MemoryStream(download.Content));

    private static string[] GetSheetNames(CoaWorkbookDownload download)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        return document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>()
            .Select(sheet => sheet.Name?.Value ?? string.Empty)
            .ToArray();
    }

    private static string? GetPrintArea(CoaWorkbookDownload download, string sheetName)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheets = workbookPart.Workbook.Sheets!.Elements<Sheet>().ToArray();
        var sheetIndex = Array.FindIndex(
            sheets,
            sheet => string.Equals(sheet.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        sheetIndex.Should().BeGreaterThanOrEqualTo(0);

        return workbookPart.Workbook.DefinedNames
            ?.Elements<DefinedName>()
            .FirstOrDefault(name =>
                name.Name?.Value == "_xlnm.Print_Area" &&
                name.LocalSheetId?.Value == (uint)sheetIndex)
            ?.Text;
    }

    private static (bool FitToPage, uint? PaperSize, uint? FitToWidth, uint? FitToHeight) GetSmallSheetPageSetup(
        CoaWorkbookDownload download,
        string sheetName)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .First(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        var worksheet = worksheetPart.Worksheet;
        var pageSetup = worksheet.GetFirstChild<PageSetup>();
        return (
            worksheet.GetFirstChild<SheetProperties>()?.PageSetupProperties?.FitToPage?.Value == true,
            pageSetup?.PaperSize?.Value,
            pageSetup?.FitToWidth?.Value,
            pageSetup?.FitToHeight?.Value);
    }

    private static bool HasWorksheetDrawing(CoaWorkbookDownload download, string sheetName)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .First(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        return worksheetPart.DrawingsPart is not null &&
            worksheetPart.Worksheet.Descendants<Drawing>().Any();
    }

    private static bool HasLegacyHeaderFooterDrawing(CoaWorkbookDownload download, string sheetName)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .First(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        return worksheetPart.Worksheet.Descendants<LegacyDrawingHeaderFooter>().Any();
    }

    private static byte[] PrepareWorkbookForPdfConversion(byte[] workbookContent)
    {
        var method = typeof(SpreadsheetPdfConverter).GetMethod(
            "PrepareWorkbookForPdfConversion",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return ((byte[]?)method!.Invoke(null, [workbookContent]))!;
    }

    private static string? GetCellFontRgb(CoaWorkbookDownload download, string sheetName, string cellReference)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .First(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        var cell = worksheetPart.Worksheet.Descendants<Cell>()
            .First(item => string.Equals(item.CellReference?.Value, cellReference, StringComparison.OrdinalIgnoreCase));
        var styleIndex = (int)(cell.StyleIndex?.Value ?? 0U);
        var styles = workbookPart.WorkbookStylesPart!.Stylesheet;
        var fontIndex = (int)(styles.CellFormats!.Elements<CellFormat>().ElementAt(styleIndex).FontId?.Value ?? 0U);
        return styles.Fonts!.Elements<Font>().ElementAt(fontIndex).Color?.Rgb?.Value;
    }

    private static bool HasDrawingInRange(CoaWorkbookDownload download, string sheetName, string rangeReference)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .First(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        var drawingPart = worksheetPart.DrawingsPart;
        if (drawingPart?.WorksheetDrawing is null)
        {
            return false;
        }

        var targetRange = ParseCellRange(rangeReference);
        return drawingPart.WorksheetDrawing.ChildElements
            .Any(anchor => TryGetDrawingAnchorRange(anchor, out var anchorRange) && RangesIntersect(anchorRange, targetRange));
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
        if (!int.TryParse(marker.GetFirstChild<Xdr.ColumnId>()?.Text, out var zeroBasedColumn) ||
            !uint.TryParse(marker.GetFirstChild<Xdr.RowId>()?.Text, out var zeroBasedRow))
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
        return (column, uint.Parse(row));
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

    private sealed class TemporaryPdfHeaderImage : IDisposable
    {
        private readonly string _path;

        public TemporaryPdfHeaderImage()
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "image");
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "000-test-pdf-header.png");
            File.WriteAllBytes(_path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lJqGygAAAABJRU5ErkJggg=="));
        }

        public void Dispose()
        {
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string ResolveTemplatePath(string fileName)
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "templates", fileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "JinZhaoYi.GasQcDataLoader", "templates", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Template fixture not found: {fileName}");
    }
}
