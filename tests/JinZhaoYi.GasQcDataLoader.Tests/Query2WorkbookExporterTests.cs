using ClosedXML.Excel;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class Query2WorkbookExporterTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Query2WorkbookExporterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportAsync_rewrites_query2_only_and_preserves_template_styles()
    {
        var templateDirectory = Path.Combine(_rootPath, "template");
        var batchDirectory = Path.Combine(_rootPath, "20251119");
        Directory.CreateDirectory(templateDirectory);
        Directory.CreateDirectory(batchDirectory);

        var templatePath = Path.Combine(templateDirectory, "template.xlsx");
        CreateTemplateWorkbook(templatePath);

        var exporter = new Query2WorkbookExporter(
            Options.Create(new SchedulerOptions
            {
                ExcelExport = new SchedulerExcelExportOptions
                {
                    Enabled = true,
                    TemplatePath = templatePath
                }
            }),
            NullLogger<Query2WorkbookExporter>.Instance);

        var writeSet = new ImportWriteSet();
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Rf, Row("RF,ppb(5841)", "STD", "20251030001", acetone: 98.68m)));
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Raw, Row("20251119023", "PORT 2", "20251117006", acetone: 100m, ppbAcetone: 99m, rtAcetone: 2.1m)));
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Avg, Row("AVG(1:2)", "PORT 2", "20251117006", acetone: 101m)));
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Ppb, Row("ppb(5900)", "PORT 2", "20251117006", acetone: 102m)));
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Rpd, Row("RPD(1:2)", "PORT 2", "20251117006", acetone: 0.01m)));
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Qc, Row("QC(AVG1,AVG2)", "STD", "20251030001", acetone: 0.02m)));

        var candidates = new[]
        {
            new QuantFileCandidate(
                FullPath: @"C:\GAS\20251119\Done\STD\STD[20251119 0947]_903.D\Quant.txt",
                DayFolderPath: batchDirectory,
                SourceRootPath: @"C:\GAS\20251119\Done",
                OutputRootPath: _rootPath,
                LogicalBatchDate: "20251119",
                IsArchivedInput: true,
                TopFolderName: "STD",
                SourceKind: QuantSourceKind.Std,
                Port: "STD",
                DataFilename: @"STD[20251119 0947]_903.D\Quant.txt",
                DataFilepath: @"C:\GAS\20251119\Done\STD\STD[20251119 0947]_903.D")
        };

        var outputPath = await exporter.ExportAsync(writeSet, candidates, CancellationToken.None);

        outputPath.Should().NotBeNull();
        outputPath.Should().Be(Path.Combine(batchDirectory, "QC", "Cylinder_Qc[20251119].xlsx"));
        Path.GetFileName(outputPath!).Should().Be("Cylinder_Qc[20251119].xlsx");
        File.Exists(outputPath!).Should().BeTrue();

        using var workbook = new XLWorkbook(outputPath);
        workbook.Worksheets.Select(sheet => sheet.Name).Should().Equal("Query2");

        var worksheet = workbook.Worksheet("Query2");
        worksheet.Row(Query2ColumnLayout.HeaderRowNumber).Cells(1, Query2ColumnLayout.Headers.Count)
            .Select(cell => cell.GetString())
            .Should()
            .Equal(Query2ColumnLayout.Headers);

        worksheet.Cell(4, 1).GetString().Should().Be("RF,ppb(5841)");
        worksheet.Cell(5, 1).GetString().Should().Be("20251119023");
        worksheet.Cell(6, 1).GetString().Should().Be("AVG(1:2)");
        worksheet.Cell(7, 1).GetString().Should().Be("ppb(5900)");
        worksheet.Cell(8, 1).GetString().Should().Be("RPD(1:2)");
        worksheet.Cell(9, 1).GetString().Should().Be("QC(AVG1,AVG2)");
        worksheet.Cell(10, 1).GetString().Should().Be("Crit(MAX)");
        worksheet.Cell(11, 1).GetString().Should().Be("Crit(MIN)");

        worksheet.Cell(4, 1).Style.Fill.BackgroundColor.Color.ToArgb().Should().Be(unchecked((int)0xFFFFC000));
        worksheet.Cell(5, 1).Style.Fill.BackgroundColor.Color.ToArgb().Should().Be(unchecked((int)0xFF9DC3E6));
        worksheet.Cell(6, 1).Style.Fill.BackgroundColor.Color.ToArgb().Should().Be(unchecked((int)0xFFA9D18E));
        worksheet.Cell(7, 1).Style.Fill.BackgroundColor.Color.ToArgb().Should().Be(unchecked((int)0xFFFFE699));
        worksheet.Cell(8, 1).Style.Fill.BackgroundColor.Color.ToArgb().Should().Be(unchecked((int)0xFFF4B084));
        worksheet.Cell(9, 1).Style.Fill.BackgroundColor.Color.ToArgb().Should().Be(unchecked((int)0xFFD9B8E5));
    }

    [Fact]
    public async Task ExportAsync_uses_sample_no_for_excel_si0_display_when_si0_id_is_missing()
    {
        var templateDirectory = Path.Combine(_rootPath, "template-fallback");
        var batchDirectory = Path.Combine(_rootPath, "20260420-fallback");
        Directory.CreateDirectory(templateDirectory);
        Directory.CreateDirectory(batchDirectory);

        var templatePath = Path.Combine(templateDirectory, "template.xlsx");
        CreateTemplateWorkbook(templatePath);

        var exporter = new Query2WorkbookExporter(
            Options.Create(new SchedulerOptions
            {
                ExcelExport = new SchedulerExcelExportOptions
                {
                    Enabled = true,
                    TemplatePath = templatePath
                }
            }),
            NullLogger<Query2WorkbookExporter>.Instance);

        var writeSet = new ImportWriteSet();
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Raw, Row("20260420009", "PORT 5", "20260420004", sampleNo: 9, si0Id: null, acetone: 100m)));
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Ppb, Row("ppb()", "PORT 5", "20260420004", sampleNo: 9, si0Id: null, acetone: 88m)));

        var candidates = new[]
        {
            new QuantFileCandidate(
                FullPath: @"C:\GAS\20260420\PORT 5\PORT 5[20260420 2334]_V009.D\Quant.txt",
                DayFolderPath: batchDirectory,
                SourceRootPath: @"C:\GAS\20260420",
                OutputRootPath: _rootPath,
                LogicalBatchDate: "20260420",
                IsArchivedInput: false,
                TopFolderName: "PORT 5",
                SourceKind: QuantSourceKind.Port,
                Port: "PORT 5",
                DataFilename: @"PORT 5[20260420 2334]_V009.D\Quant.txt",
                DataFilepath: @"C:\GAS\20260420\PORT 5\PORT 5[20260420 2334]_V009.D")
        };

        var outputPath = await exporter.ExportAsync(writeSet, candidates, CancellationToken.None);

        using var workbook = new XLWorkbook(outputPath!);
        var worksheet = workbook.Worksheet("Query2");

        worksheet.Cell(4, 1).GetString().Should().Be("20260420009");
        worksheet.Cell(4, 5).GetValue<int>().Should().Be(9);
        worksheet.Cell(5, 1).GetString().Should().Be("ppb(S9)");
        worksheet.Cell(5, 5).GetValue<int>().Should().Be(9);
    }

    [Fact]
    public async Task ExportAsync_treats_zero_si0_id_as_missing_for_excel_display()
    {
        var templateDirectory = Path.Combine(_rootPath, "template-zero");
        var batchDirectory = Path.Combine(_rootPath, "20260420-zero");
        Directory.CreateDirectory(templateDirectory);
        Directory.CreateDirectory(batchDirectory);

        var templatePath = Path.Combine(templateDirectory, "template.xlsx");
        CreateTemplateWorkbook(templatePath);

        var exporter = new Query2WorkbookExporter(
            Options.Create(new SchedulerOptions
            {
                ExcelExport = new SchedulerExcelExportOptions
                {
                    Enabled = true,
                    TemplatePath = templatePath
                }
            }),
            NullLogger<Query2WorkbookExporter>.Instance);

        var writeSet = new ImportWriteSet();
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Raw, Row("20260420009", "PORT 5", "20260420004", sampleNo: 9, si0Id: 0, acetone: 100m)));
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Ppb, Row("ppb(0)", "PORT 5", "20260420004", sampleNo: 9, si0Id: 0, acetone: 88m)));

        var candidates = new[]
        {
            new QuantFileCandidate(
                FullPath: @"C:\GAS\20260420\PORT 5\PORT 5[20260420 2334]_V009.D\Quant.txt",
                DayFolderPath: batchDirectory,
                SourceRootPath: @"C:\GAS\20260420",
                OutputRootPath: _rootPath,
                LogicalBatchDate: "20260420",
                IsArchivedInput: false,
                TopFolderName: "PORT 5",
                SourceKind: QuantSourceKind.Port,
                Port: "PORT 5",
                DataFilename: @"PORT 5[20260420 2334]_V009.D\Quant.txt",
                DataFilepath: @"C:\GAS\20260420\PORT 5\PORT 5[20260420 2334]_V009.D")
        };

        var outputPath = await exporter.ExportAsync(writeSet, candidates, CancellationToken.None);

        using var workbook = new XLWorkbook(outputPath!);
        var worksheet = workbook.Worksheet("Query2");

        worksheet.Cell(4, 5).GetValue<int>().Should().Be(9);
        worksheet.Cell(5, 1).GetString().Should().Be("ppb(S9)");
        worksheet.Cell(5, 5).GetValue<int>().Should().Be(9);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }

    private static void CreateTemplateWorkbook(string templatePath)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Query2");
        workbook.AddWorksheet("OtherSheet");

        worksheet.Cell(1, 1).Value = "Template";
        worksheet.Cell(2, 1).Value = "Keep headers";

        for (var column = 1; column <= Query2ColumnLayout.Headers.Count; column++)
        {
            worksheet.Cell(Query2ColumnLayout.HeaderRowNumber, column).Value = Query2ColumnLayout.Headers[column - 1];
        }

        SeedTemplateRow(worksheet, 4, "RF,ppb(5841)", XLColor.FromHtml("#FFC000"));
        SeedTemplateRow(worksheet, 5, "RAW_EXAMPLE", XLColor.FromHtml("#9DC3E6"));
        SeedTemplateRow(worksheet, 6, "AVG(1:2)", XLColor.FromHtml("#A9D18E"));
        SeedTemplateRow(worksheet, 7, "ppb(5900)", XLColor.FromHtml("#FFE699"));
        SeedTemplateRow(worksheet, 8, "RPD(1:2)", XLColor.FromHtml("#F4B084"));
        SeedTemplateRow(worksheet, 9, "QC(AVG1,AVG2)", XLColor.FromHtml("#D9B8E5"));
        SeedTemplateRow(worksheet, 10, "Crit(MAX)", XLColor.LightGray);
        SeedTemplateRow(worksheet, 11, "Crit(MIN)", XLColor.Gray);

        workbook.SaveAs(templatePath);
    }

    private static void SeedTemplateRow(IXLWorksheet worksheet, int rowNumber, string id, XLColor fillColor)
    {
        for (var column = 1; column <= Query2ColumnLayout.Headers.Count; column++)
        {
            var cell = worksheet.Cell(rowNumber, column);
            cell.Style.Fill.BackgroundColor = fillColor;
            cell.Style.Font.Bold = true;
        }

        worksheet.Row(rowNumber).Height = 24;
        worksheet.Cell(rowNumber, 1).Value = id;
        worksheet.Cell(rowNumber, 17).Value = rowNumber;
    }

    private static QcDataRow Row(
        string id,
        string port,
        string lotNo,
        int sampleNo = 23,
        int? si0Id = 5900,
        decimal? acetone = null,
        decimal? ppbAcetone = null,
        decimal? rtAcetone = null)
    {
        var row = new QcDataRow
        {
            Id = id,
            Port = port,
            LotNo = lotNo,
            Inst = "QC-01",
            Si0Id = si0Id,
            SampleNo = sampleNo,
            DataFilename = "Quant.txt",
            DataFilepath = @"C:\GAS\file",
            Container = "0.5L_Cylinder",
            Description = $"desc #{lotNo}",
            EmVolts = "1458.82",
            RelativeEm = "-23.529",
            SampleName = "Sample",
            SampleType = "TO14C1",
            AnlzTime = new DateTime(2025, 11, 19, 9, 47, 0)
        };

        row.Areas["Acetone"] = acetone;
        row.Ppbs["Acetone"] = ppbAcetone;
        row.RetentionTimes["Acetone"] = rtAcetone;
        return row;
    }
}
