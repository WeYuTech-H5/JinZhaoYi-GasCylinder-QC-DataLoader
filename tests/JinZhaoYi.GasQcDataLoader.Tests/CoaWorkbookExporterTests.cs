using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using A = DocumentFormat.OpenXml.Drawing;
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
        workbook.Worksheet("COA_STD-N050").Cell("B10").GetString().Should().Be("NF-SEMI STD");
        workbook.Worksheet("COA_STD-L100").Cell("B10").GetString().Should().Be("STD Gas PC for Semiconductor");
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
    public void PdfConversionWorkbook_uses_large_card_assets_and_a4_print_layout()
    {
        var exporter = CreateExporter();
        var row = CreateRow("STD-PDF", "0.5L_Cylinder");
        var download = exporter.ExportLargeForDownload([row], "20260521", CoaLargeTemplateType.Standard);
        var originalContent = download.Content.ToArray();

        var preparedContent = PrepareWorkbookForPdfConversion(originalContent);

        HasLegacyHeaderFooterDrawing(download, "COA_STD-PDF").Should().BeTrue();
        originalContent.Should().Equal(download.Content);

        var preparedDownload = new CoaWorkbookDownload(preparedContent, download.ContentType, download.FileName);
        HasLegacyHeaderFooterDrawing(preparedDownload, "COA_STD-PDF").Should().BeTrue();
        HasWorksheetDrawing(preparedDownload, "COA_STD-PDF").Should().BeTrue();
        GetLargeSheetPageSetup(preparedDownload, "COA_STD-PDF").Should()
            .Be((true, 9U, 0U, null, 94U, 0.15D, 0.15D, 0.65D, 0.45D, true, false, null));

        foreach (var fileName in new[] { "coa-large-header-composed.png", "coa-large-signature-enhanced.png" })
        {
            var assetPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "image", fileName);
            File.Exists(assetPath).Should().BeTrue();
            new FileInfo(assetPath).Length.Should().BeGreaterThan(0);
        }
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
    public void ExportSmallForDownload_preserves_legacy_small_card_excel_print_layout()
    {
        var exporter = CreateExporter();
        var rows = Enumerable.Range(1, 10)
            .Select(index => CreateRow($"STD-N{index:000}", "1L_Cylinder", index))
            .ToArray();

        var download = exporter.ExportSmallForDownload(rows, "20260521", 9);
        var sheetNames = GetSheetNames(download);

        GetPrintArea(download, sheetNames[0]).Should().BeNull();
        GetPrintArea(download, sheetNames[1]).Should().BeNull();
        GetSmallSheetPageSetup(download, sheetNames[0]).Should().Be((false, 9U, null, null, 70U, 0.25D, 0.75D));
    }

    [Fact]
    public void PdfConversionWorkbook_applies_small_card_pdf_scale_without_changing_excel_download_layout()
    {
        var exporter = CreateExporter();
        var rows = Enumerable.Range(1, 2)
            .Select(index => CreateRow($"STD-N{index:000}", "1L_Cylinder", index))
            .ToArray();
        var download = exporter.ExportSmallForDownload(rows, "20260521", 9);
        var sheetName = GetSheetNames(download)[0];

        var preparedContent = PrepareWorkbookForPdfConversion(download.Content.ToArray());
        var preparedDownload = new CoaWorkbookDownload(preparedContent, download.ContentType, download.FileName);

        GetSmallSheetPageSetup(download, sheetName).Should().Be((false, 9U, null, null, 70U, 0.25D, 0.75D));
        GetSmallSheetPageSetup(preparedDownload, sheetName).Should().Be((false, 1U, null, null, 74U, 0.486D, 0.263D));
        IsHorizontallyCentered(preparedDownload, sheetName).Should().BeTrue();
        IsVerticallyCentered(preparedDownload, sheetName).Should().BeTrue();
        GetPrintArea(preparedDownload, sheetName).Should().Be($"'{sheetName}'!$A$1:$AB$67");

        using var originalWorkbook = Open(download);
        using var preparedWorkbook = Open(preparedDownload);
        originalWorkbook.Worksheet(sheetName).Cell("B8").Style.Font.FontName.Should().Be("Microsoft JhengHei UI");
        preparedWorkbook.Worksheet(sheetName).Cell("B8").Style.Font.FontName.Should().Be("Microsoft JhengHei UI");
        preparedWorkbook.Worksheet(sheetName).Row(8).Height.Should()
            .BeApproximately(originalWorkbook.Worksheet(sheetName).Row(8).Height * 0.96D, 0.01D);
        preparedWorkbook.Worksheet(sheetName).RowHeight.Should().BeApproximately(16.2D * 0.96D, 0.01D);
        preparedWorkbook.Worksheet(sheetName).Row(20).Height.Should().BeApproximately(16.5D * 0.96D, 0.01D);
    }

    [Fact]
    public void PdfConversionWorkbook_keeps_nine_small_cards_in_full_three_by_three_print_area()
    {
        var exporter = CreateExporter();
        var rows = Enumerable.Range(1, 9)
            .Select(index => CreateRow($"STD-N{index:000}", "1L_Cylinder", index))
            .ToArray();
        var download = exporter.ExportSmallForDownload(rows, "20260521", 9);
        var sheetName = GetSheetNames(download)[0];

        var preparedContent = PrepareWorkbookForPdfConversion(download.Content.ToArray());
        var preparedDownload = new CoaWorkbookDownload(preparedContent, download.ContentType, download.FileName);

        GetPrintArea(preparedDownload, sheetName).Should().Be($"'{sheetName}'!$A$1:$AB$67");
        IsHorizontallyCentered(preparedDownload, sheetName).Should().BeTrue();
        IsVerticallyCentered(preparedDownload, sheetName).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(10)]
    public void PdfConversionWorkbook_preserves_small_card_layout_for_supported_batch_sizes(int cardCount)
    {
        var exporter = CreateExporter();
        var rows = Enumerable.Range(1, cardCount)
            .Select(index => CreateRow($"STD-N{index:000}", "1L_Cylinder", index))
            .ToArray();
        var download = exporter.ExportSmallForDownload(rows, "20260521", 9);
        var preparedContent = PrepareWorkbookForPdfConversion(download.Content.ToArray());
        var preparedDownload = new CoaWorkbookDownload(preparedContent, download.ContentType, download.FileName);
        var sheetNames = GetSheetNames(preparedDownload);
        var sampleCells = new[] { "F6", "O6", "X6", "F28", "O28", "X28", "F50", "O50", "X50" };

        sheetNames.Should().HaveCount((cardCount + 8) / 9);
        using var workbook = Open(preparedDownload);
        for (var pageIndex = 0; pageIndex < sheetNames.Length; pageIndex++)
        {
            var sheetName = sheetNames[pageIndex];
            var cardsOnPage = Math.Min(9, cardCount - (pageIndex * 9));
            GetPrintArea(preparedDownload, sheetName).Should().Be($"'{sheetName}'!$A$1:$AB$67");
            IsHorizontallyCentered(preparedDownload, sheetName).Should().BeTrue();
            IsVerticallyCentered(preparedDownload, sheetName).Should().BeTrue();
            workbook.Worksheet(sheetName).Cell("B8").Style.Font.FontName.Should().Be("Microsoft JhengHei UI");

            for (var index = 0; index < sampleCells.Length; index++)
            {
                var value = workbook.Worksheet(sheetName).Cell(sampleCells[index]).GetString();
                if (index < cardsOnPage)
                {
                    value.Should().NotBeEmpty();
                }
                else
                {
                    value.Should().BeEmpty();
                }
            }
        }
    }

    [Fact]
    public void PdfConversionWorkbook_removes_unused_small_card_signature_rows_and_keeps_template_company_text()
    {
        var exporter = CreateExporter();
        var download = exporter.ExportSmallForDownload([CreateRow("STD-N004", "1L_Cylinder")], "20260521", 9);
        var sheetName = GetSheetNames(download)[0];

        var preparedContent = PrepareWorkbookForPdfConversion(download.Content.ToArray());
        var preparedDownload = new CoaWorkbookDownload(preparedContent, download.ContentType, download.FileName);

        using var originalWorkbook = Open(download);
        var originalSheet = originalWorkbook.Worksheet(sheetName);
        originalSheet.Cell("K22").GetString().Should().NotBeEmpty();
        originalSheet.Cell("O22").GetString().Should().NotBeEmpty();

        using var preparedWorkbook = Open(preparedDownload);
        var preparedSheet = preparedWorkbook.Worksheet(sheetName);
        preparedSheet.Cell("B22").GetString().Should().NotBeEmpty();
        preparedSheet.Cell("F22").GetString().Should().NotBeEmpty();
        preparedSheet.Cell("K22").GetString().Should().BeEmpty();
        preparedSheet.Cell("O22").GetString().Should().BeEmpty();
        preparedSheet.Cell("T66").GetString().Should().BeEmpty();
        preparedSheet.Cell("X66").GetString().Should().BeEmpty();
        preparedSheet.Cell("K22").Style.Border.TopBorder.Should().Be(XLBorderStyleValues.None);

        CountDrawingText(preparedDownload, sheetName, "金 兆 益 科 技 股 份 有 限 公 司").Should().Be(1);
        HasDrawingInRange(preparedDownload, sheetName, "K2:R22").Should().BeFalse();
        HasDrawingInRange(preparedDownload, sheetName, "T46:AA66").Should().BeFalse();
    }

    [Fact]
    public void Project_contains_small_card_pdf_header_image_asset()
    {
        var assetPath = Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "image",
            "coa-small-card-header.png");

        File.Exists(assetPath).Should().BeTrue();
        new FileInfo(assetPath).Length.Should().BeGreaterThan(0);
        var (width, height) = ReadPngDimensions(assetPath);
        width.Should().BeGreaterThanOrEqualTo(611, "the Excel-rendered header must provide at least 300 DPI");
        height.Should().BeGreaterThanOrEqualTo(89, "the header should retain sufficient vertical detail at print size");
        (width / (double)height).Should().BeApproximately(1222D / 179D, 0.05D);

        var logoAssetPath = Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "image",
            "coa-small-card-header-logo.png");
        var textAssetPath = Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "image",
            "coa-small-card-header-text.png");
        ReadPngDimensions(logoAssetPath).Should().Be((220, 179));
        ReadPngDimensions(textAssetPath).Should().Be((1002, 179));
    }

    [Fact]
    public void Project_contains_locked_excel_rendered_small_card_reference_page()
    {
        var referencePath = ResolveSmallCardExcelReferencePagePath();

        File.Exists(referencePath).Should().BeTrue();
        ReadPngDimensions(referencePath).Should().Be((2481, 3508));
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(referencePath))).Should()
            .Be("9A4DC3E84EA475B77B58C230BAD68A3736B22451B59A1A4CCB0ABEBA90BF52F2");
    }

    [Theory]
    [Trait("Category", "LibreOfficeIntegration")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(10)]
    public async Task LibreOffice_renders_small_card_pdf_for_supported_batch_sizes(int cardCount)
    {
        var libreOfficePath = Environment.GetEnvironmentVariable("GAS_QC_TEST_LIBREOFFICE_PATH");
        if (string.IsNullOrWhiteSpace(libreOfficePath))
        {
            return;
        }

        File.Exists(libreOfficePath).Should().BeTrue();
        var options = Options.Create(new SchedulerOptions
        {
            CsvExport = new SchedulerCsvExportOptions { RawLotId = "CC-706988" },
            CoaExport = new SchedulerCoaExportOptions
            {
                SmallTemplatePath = ResolveTemplatePath("COA(小卡).xlsx"),
                LibreOfficePath = libreOfficePath,
                PdfConversionTimeoutSeconds = 120,
                UseBasicPdfFallback = false
            }
        });
        var exporter = new CoaWorkbookExporter(options);
        var converter = new SpreadsheetPdfConverter(options);
        var rows = Enumerable.Range(1, cardCount)
            .Select(index => CreateRow($"STD-N{index:000}", "1L_Cylinder", index))
            .ToArray();
        var workbook = exporter.ExportSmallForDownload(rows, "20260624", 9);

        var pdfContent = await converter.ConvertXlsxToPdfAsync(
            workbook.Content.ToArray(),
            workbook.FileName,
            CancellationToken.None);

        pdfContent.Length.Should().BeGreaterThan(20_000);
        using var pdf = PdfReader.Open(new MemoryStream(pdfContent), PdfDocumentOpenMode.Import);
        pdf.PageCount.Should().Be((cardCount + 8) / 9);
        foreach (var page in pdf.Pages.Cast<PdfPage>())
        {
            page.Width.Point.Should().BeApproximately(612D, 2D);
            page.Height.Point.Should().BeApproximately(792D, 2D);
        }

        var outputDirectory = Environment.GetEnvironmentVariable("GAS_QC_TEST_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            var pdfPath = Path.Combine(outputDirectory, $"COA-small-{cardCount:00}.pdf");
            await File.WriteAllBytesAsync(
                Path.Combine(outputDirectory, $"COA-small-{cardCount:00}.xlsx"),
                workbook.Content.ToArray());
            await File.WriteAllBytesAsync(pdfPath, pdfContent);

            if (cardCount is 1 or 9)
            {
                await VerifySmallCardPdfIfConfiguredAsync(pdfPath, outputDirectory, cardCount);
            }
        }
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
    public void ExportSmallForDownload_preserves_legacy_signature_rows_for_unused_slots()
    {
        var exporter = CreateExporter();

        var download = exporter.ExportSmallForDownload([CreateRow("STD-N004", "1L_Cylinder")], "20260521", 9);

        using var workbook = Open(download);
        var sheet = workbook.Worksheet(GetSheetNames(download)[0]);
        sheet.Cell("B22").GetString().Should().NotBeEmpty();
        sheet.Cell("F22").GetString().Should().NotBeEmpty();

        foreach (var cellReference in new[] { "K22", "O22", "T22", "X22", "B44", "F44", "K44", "O44", "T44", "X44", "B66", "F66", "K66", "O66", "T66", "X66" })
        {
            sheet.Cell(cellReference).GetString().Should().NotBeEmpty();
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

    [Fact]
    public void ExportSmallForDownload_keeps_first_page_drawings_when_tenth_card_creates_second_sheet()
    {
        var exporter = CreateExporter();
        var rows = Enumerable.Range(1, 10)
            .Select(index => CreateRow($"STD-N{index:000}", "1L_Cylinder", index))
            .ToArray();

        var download = exporter.ExportSmallForDownload(rows, "20260521", 9);
        var sheetNames = GetSheetNames(download);

        HasDrawingInRange(download, sheetNames[0], "B2:I22").Should().BeTrue();
        HasDrawingInRange(download, sheetNames[0], "K2:R22").Should().BeTrue();
        HasDrawingInRange(download, sheetNames[0], "T46:AA66").Should().BeTrue();
        HasDrawingInRange(download, sheetNames[1], "B2:I22").Should().BeTrue();
        HasDrawingInRange(download, sheetNames[1], "K2:R22").Should().BeFalse();
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

    private static (
        bool FitToPage,
        uint? PaperSize,
        uint? FitToWidth,
        uint? FitToHeight,
        uint? Scale,
        double? LeftMargin,
        double? TopMargin) GetSmallSheetPageSetup(
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
        var pageMargins = worksheet.GetFirstChild<PageMargins>();
        return (
            worksheet.GetFirstChild<SheetProperties>()?.PageSetupProperties?.FitToPage?.Value == true,
            pageSetup?.PaperSize?.Value,
            pageSetup?.FitToWidth?.Value,
            pageSetup?.FitToHeight?.Value,
            pageSetup?.Scale?.Value,
            pageMargins?.Left?.Value,
            pageMargins?.Top?.Value);
    }

    private static (
        bool FitToPage,
        uint? PaperSize,
        uint? FitToWidth,
        uint? FitToHeight,
        uint? Scale,
        double? LeftMargin,
        double? RightMargin,
        double? TopMargin,
        double? BottomMargin,
        bool HorizontalCentered,
        bool VerticalCentered,
        string? PrintArea) GetLargeSheetPageSetup(
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
        var pageMargins = worksheet.GetFirstChild<PageMargins>();
        var printOptions = worksheet.GetFirstChild<PrintOptions>();
        var sheetIndex = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .Select((item, index) => (item, index))
            .First(item => string.Equals(item.item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase))
            .index;
        var printArea = workbookPart.Workbook.DefinedNames?
            .Elements<DefinedName>()
            .FirstOrDefault(name =>
                name.Name?.Value == "_xlnm.Print_Area" &&
                name.LocalSheetId?.Value == (uint)sheetIndex)
            ?.Text;
        return (
            worksheet.GetFirstChild<SheetProperties>()?.PageSetupProperties?.FitToPage?.Value == true,
            pageSetup?.PaperSize?.Value,
            pageSetup?.FitToWidth?.Value,
            pageSetup?.FitToHeight?.Value,
            pageSetup?.Scale?.Value,
            pageMargins?.Left?.Value,
            pageMargins?.Right?.Value,
            pageMargins?.Top?.Value,
            pageMargins?.Bottom?.Value,
            printOptions?.HorizontalCentered?.Value == true,
            printOptions?.VerticalCentered?.Value == true,
            printArea);
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

    private static bool IsHorizontallyCentered(CoaWorkbookDownload download, string sheetName)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .First(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        return worksheetPart.Worksheet.GetFirstChild<PrintOptions>()?.HorizontalCentered?.Value == true;
    }

    private static bool IsVerticallyCentered(CoaWorkbookDownload download, string sheetName)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .First(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        return worksheetPart.Worksheet.GetFirstChild<PrintOptions>()?.VerticalCentered?.Value == true;
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

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        stream.ReadExactly(header);

        header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }).Should().BeTrue();
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return (width, height);
    }

    private static async Task VerifySmallCardPdfIfConfiguredAsync(
        string pdfPath,
        string outputDirectory,
        int cardCount)
    {
        var pythonPath = Environment.GetEnvironmentVariable("GAS_QC_TEST_PYTHON_PATH");
        var pdftoppmPath = Environment.GetEnvironmentVariable("GAS_QC_TEST_PDFTOPPM_PATH");
        if (string.IsNullOrWhiteSpace(pythonPath) ||
            string.IsNullOrWhiteSpace(pdftoppmPath))
        {
            return;
        }

        File.Exists(pythonPath).Should().BeTrue();
        File.Exists(pdftoppmPath).Should().BeTrue();
        var scriptPath = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "verify_coa_small_header.py");
        File.Exists(scriptPath).Should().BeTrue();

        var comparisonDirectory = Path.Combine(
            outputDirectory,
            cardCount == 1 ? "header-comparison" : "crop-mark-comparison");
        var startInfo = new ProcessStartInfo(pythonPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--actual-pdf");
        startInfo.ArgumentList.Add(pdfPath);
        startInfo.ArgumentList.Add("--pdftoppm");
        startInfo.ArgumentList.Add(pdftoppmPath);
        startInfo.ArgumentList.Add("--output-dir");
        startInfo.ArgumentList.Add(comparisonDirectory);
        if (cardCount == 1)
        {
            startInfo.ArgumentList.Add("--reference-page");
            startInfo.ArgumentList.Add(ResolveSmallCardExcelReferencePagePath());
            startInfo.ArgumentList.Add("--verify-fonts");
        }
        else
        {
            startInfo.ArgumentList.Add("--verify-crop-marks");
        }

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        var standardOutput = await process!.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(
            0,
            $"header similarity regression failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
    }

    private static string ResolveSmallCardExcelReferencePagePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "coa-small-excel-reference-page-300dpi.png");

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

    private static int CountDrawingText(CoaWorkbookDownload download, string sheetName, string text)
    {
        using var stream = new MemoryStream(download.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
            .First(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        return worksheetPart.DrawingsPart?.WorksheetDrawing
            .Descendants<A.Text>()
            .Count(item => string.Equals(item.Text, text, StringComparison.Ordinal)) ?? 0;
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
