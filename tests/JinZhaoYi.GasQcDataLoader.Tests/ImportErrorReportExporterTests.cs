using ClosedXML.Excel;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class ImportErrorReportExporterTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "ImportErrorReportExporterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportAsync_writes_error_report_to_export_root_qc_folder()
    {
        var exporter = new ImportErrorReportExporter(
            Options.Create(new SchedulerOptions
            {
                WatchRoot = Path.Combine(_rootPath, "watch"),
                ExportRoot = Path.Combine(_rootPath, "out")
            }),
            NullLogger<ImportErrorReportExporter>.Instance);

        var outputPath = await exporter.ExportAsync(
            [
                new ImportErrorReportRow(
                    OccurredAt: new DateTimeOffset(2026, 5, 6, 10, 0, 0, TimeSpan.FromHours(8)),
                    LogicalBatchDate: "20260505",
                    Port: "PORT 12",
                    TopFolderName: "PORT 12",
                    LotNo: "20260505001",
                    QuantPath: @"C:\MES_STD\2026\PORT 12\PORT 12[20260505 1732].D\Quant.txt",
                    DataFolderPath: @"C:\MES_STD\2026\PORT 12\PORT 12[20260505 1732].D",
                    ErrorType: "ParseOrImportException",
                    Message: "Misc value does not contain a LOT marker.",
                    SuggestedAction: "請確認 Quant.txt 的 Misc 欄位是否有 #LOT。")
            ],
            CancellationToken.None);

        outputPath.Should().NotBeNull();
        outputPath.Should().StartWith(Path.Combine(_rootPath, "out", "QC"));
        File.Exists(outputPath!).Should().BeTrue();

        using var workbook = new XLWorkbook(outputPath);
        var worksheet = workbook.Worksheet("ImportErrors");
        worksheet.Cell(1, 1).GetString().Should().Be("發生時間");
        worksheet.Cell(1, 10).GetString().Should().Be("建議處理方式");
        worksheet.Cell(2, 2).GetString().Should().Be("20260505");
        worksheet.Cell(2, 3).GetString().Should().Be("PORT 12");
        worksheet.Cell(2, 5).GetString().Should().Be("20260505001");
        worksheet.Cell(2, 9).GetString().Should().Contain("Misc value");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}
