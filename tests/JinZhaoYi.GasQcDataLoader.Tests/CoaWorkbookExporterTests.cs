using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class CoaWorkbookExporterTests
{
    [Fact]
    public void ExportLargeForDownload_uses_container_sheet_for_standard_template()
    {
        var exporter = CreateExporter();
        var halfLiter = CreateRow("STD-050", "0.5L_Cylinder");
        halfLiter.Areas["Acetone"] = 101.2m;
        var oneLiter = CreateRow("STD-100", "1L_Cylinder");
        oneLiter.Areas["Acetone"] = 98.6m;

        var download = exporter.ExportLargeForDownload([halfLiter, oneLiter], "20260521", CoaLargeTemplateType.Standard);

        download.FileName.Should().Be("COA(大卡)_20260521.xlsx");
        using var workbook = Open(download);
        workbook.Worksheets.Select(sheet => sheet.Name).Should().BeEquivalentTo("COA_STD-050", "COA_STD-100");
        workbook.Worksheet("COA_STD-050").Cell("E52").GetDouble().Should().BeApproximately(101.2, 0.0001);
        workbook.Worksheet("COA_STD-100").Cell("E52").GetDouble().Should().BeApproximately(98.6, 0.0001);
        workbook.Worksheet("COA_STD-050").Cell("B10").GetString().Should().Be("STD Gas PC for Semiconductor");
        workbook.Worksheet("COA_STD-100").Cell("B10").GetString().Should().Be("NF-SEMI STD");
        workbook.Worksheet("COA_STD-050").Cell("B11").GetString().Should().Be("PG000-0006 / PG000-0016");
        workbook.Worksheet("COA_STD-100").Cell("B11").GetString().Should().Be("PG000-0010");
        workbook.Worksheet("COA_STD-050").Cell("B14").GetString().Should().Be("5 cm*35cm");
        workbook.Worksheet("COA_STD-100").Cell("B14").GetString().Should().Be("8.87 cm*27.7 cm");
        workbook.Worksheet("COA_STD-050").Cell("B16").GetString().Should().Be("950 psi");
        workbook.Worksheet("COA_STD-100").Cell("B16").GetString().Should().Be("1000 psi");
        workbook.Worksheet("COA_STD-050").Cell("E11").GetString().Should().Be("500 mL");
        workbook.Worksheet("COA_STD-100").Cell("E11").GetString().Should().Be("1000 mL");
        workbook.Worksheet("COA_STD-050").Cell("E13").GetString().Should().Be("41 L");
        workbook.Worksheet("COA_STD-100").Cell("E13").GetString().Should().Be("70 L");
        workbook.Worksheet("COA_STD-050").Cell("E16").GetString().Should().Be("±10%");
        workbook.Worksheet("COA_STD-100").Cell("E16").GetString().Should().Be("±15%");
        HasWorksheetDrawing(download, "COA_STD-050").Should().BeTrue();
        HasWorksheetDrawing(download, "COA_STD-100").Should().BeTrue();
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
    public void ExportSmallForDownload_repeats_each_selected_row_across_requested_card_count()
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
        workbook.Worksheets.Count.Should().Be(2);
        var firstSheet = workbook.Worksheet("COA小卡_STD-N001");
        firstSheet.Cell("F6").GetString().Should().Be("STD-N001");
        firstSheet.Cell("O6").GetString().Should().Be("STD-N001");
        firstSheet.Cell("X6").GetString().Should().Be("STD-N001");
        firstSheet.Cell("F28").GetString().Should().Be("STD-N001");
        firstSheet.Cell("O28").GetString().Should().Be("STD-N001");
        firstSheet.Cell("X28").GetString().Should().Be("STD-N001");
        firstSheet.Cell("F50").GetString().Should().Be("STD-N001");
        firstSheet.Cell("O50").GetString().Should().Be("STD-N001");
        firstSheet.Cell("X50").GetString().Should().Be("STD-N001");
        firstSheet.Cell("F8").GetDouble().Should().Be(91);
        firstSheet.Cell("O8").GetDouble().Should().Be(91);
        firstSheet.Cell("B19").GetString().Should().Be("母瓶 NO.  BOMB1-001");

        var secondSheet = workbook.Worksheet("COA小卡_STD-N002");
        secondSheet.Cell("F6").GetString().Should().Be("STD-N002");
        secondSheet.Cell("X50").GetString().Should().Be("STD-N002");
        secondSheet.Cell("F8").GetDouble().Should().Be(92);
        secondSheet.Cell("B19").GetString().Should().Be("母瓶 NO.  BOMB1-002");
    }

    [Fact]
    public void ExportSmallForDownload_rejects_invalid_cards_per_page()
    {
        var exporter = CreateExporter();

        var act = () => exporter.ExportSmallForDownload([CreateRow("STD-001", "1L_Cylinder")], "20260521", 10);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*cardsPerPage*");
    }

    private static CoaWorkbookExporter CreateExporter(string? largeTemplatePath = null) =>
        new(Options.Create(new SchedulerOptions
        {
            CsvExport = new SchedulerCsvExportOptions { RawLotId = "CC-706988" },
            CoaExport = new SchedulerCoaExportOptions
            {
                LargeTemplatePath = largeTemplatePath ?? ResolveTemplatePath("COA(大卡).xlsx"),
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
