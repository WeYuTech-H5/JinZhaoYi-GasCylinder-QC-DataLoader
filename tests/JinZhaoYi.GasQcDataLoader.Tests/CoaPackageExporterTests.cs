using System.IO.Compression;
using System.Text;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class CoaPackageExporterTests
{
    [Fact]
    public async Task ExportLargePackageForDownload_returns_excel_and_pdf_per_selected_row()
    {
        var pdfConverter = new FakePdfConverter();
        var exporter = new CoaPackageExporter(CreateWorkbookExporter(), pdfConverter);
        var rows = new[]
        {
            CreateRow("STD-N002", 2),
            CreateRow("STD-N001", 1)
        };

        var download = await exporter.ExportLargePackageForDownloadAsync(
            rows,
            "20260521",
            CoaLargeTemplateType.Standard,
            CancellationToken.None);

        download.ContentType.Should().Be("application/zip");
        download.FileName.Should().Be("COA(大卡)_20260521.zip");

        using var archive = new ZipArchive(new MemoryStream(download.Content), ZipArchiveMode.Read);
        archive.Entries.Select(entry => entry.FullName).Should().BeEquivalentTo(
            "COA(大卡)_20260521_STD-N001.xlsx",
            "COA(大卡)_20260521_STD-N001.pdf",
            "COA(大卡)_20260521_STD-N002.xlsx",
            "COA(大卡)_20260521_STD-N002.pdf");
        pdfConverter.WorkbookFileNames.Should().Equal(
            "COA(大卡)_20260521_STD-N001.xlsx",
            "COA(大卡)_20260521_STD-N002.xlsx");
    }

    [Fact]
    public async Task ExportSmallPackageForDownload_returns_one_appended_excel_and_pdf()
    {
        var pdfConverter = new FakePdfConverter();
        var exporter = new CoaPackageExporter(CreateWorkbookExporter(), pdfConverter);
        var rows = new[]
        {
            CreateRow("STD-N001", 1),
            CreateRow("STD-N001", 2)
        };

        var download = await exporter.ExportSmallPackageForDownloadAsync(rows, "20260521", 9, CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(download.Content), ZipArchiveMode.Read);
        archive.Entries.Select(entry => entry.FullName).Should().BeEquivalentTo(
            "COA(小卡)_20260521.xlsx",
            "COA(小卡)_20260521.pdf");
        pdfConverter.WorkbookFileNames.Should().Equal("COA(小卡)_20260521.xlsx");
    }

    private static CoaWorkbookExporter CreateWorkbookExporter() =>
        new(Options.Create(new SchedulerOptions
        {
            CsvExport = new SchedulerCsvExportOptions { RawLotId = "CC-706988" },
            CoaExport = new SchedulerCoaExportOptions
            {
                LargeTemplatePath = ResolveTemplatePath("COA(大卡).xlsx"),
                SmallTemplatePath = ResolveTemplatePath("COA(小卡).xlsx")
            }
        }));

    private static QcDataRow CreateRow(string sampleName, int sampleNo) =>
        new()
        {
            AnlzTime = new DateTime(2026, 5, 21, 10, sampleNo, 0),
            LotNo = "20260521001",
            SampleName = sampleName,
            SampleNo = sampleNo,
            Container = "1L_Cylinder"
        };

    private static string ResolveTemplatePath(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "templates", fileName),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "src", "JinZhaoYi.GasQcDataLoader", "templates", fileName)
        };

        return candidates.Select(Path.GetFullPath).First(File.Exists);
    }

    private sealed class FakePdfConverter : ISpreadsheetPdfConverter
    {
        public List<string> WorkbookFileNames { get; } = [];

        public Task<byte[]> ConvertXlsxToPdfAsync(
            byte[] workbookContent,
            string workbookFileName,
            CancellationToken cancellationToken)
        {
            WorkbookFileNames.Add(workbookFileName);
            return Task.FromResult(Encoding.UTF8.GetBytes($"pdf:{workbookFileName}"));
        }
    }
}
