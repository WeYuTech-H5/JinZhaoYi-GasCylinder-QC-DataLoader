using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class PortPpbCsvExporterTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "PortPpbCsvExporterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportAsync_writes_csv_with_to14c_order_and_escaped_values()
    {
        var exporter = CreateExporter(new SchedulerCsvExportOptions
        {
            Enabled = true,
            Maker = "New-Fast Technology Co., LTD",
            ValveType = "1/4\" VCR Female"
        });
        var row = CreatePpbRow();
        row.Areas["Acetone"] = 97m;
        row.Areas["Benzene"] = 93m;
        row.Areas["1,2,4-TCB"] = 94m;

        var paths = await exporter.ExportAsync([row], [CreateCandidate()], CancellationToken.None);

        paths.Should().ContainSingle();
        var lines = await File.ReadAllLinesAsync(paths[0]);
        lines.Should().Contain("Maker,\"New-Fast Technology Co., LTD\"");
        lines.Should().Contain("ValveType,\"1/4\"\" VCR Female\"");
        lines.Should().Contain("ContainerID,TSMC-024");
        lines.Should().Contain("RawLotId,CC-706988");

        var itemHeaderIndex = Array.IndexOf(lines, "Item,N,MEAN,SD,MAX,MIN,VALUE,DL");
        itemHeaderIndex.Should().BeGreaterThan(0);
        lines[itemHeaderIndex + 1].Should().Be("Acetone,,,,,,97,");
        lines[itemHeaderIndex + 2].Should().Be("Benzene,,,,,,93,");
        lines[itemHeaderIndex + 3].Should().Be("Benzene-1-2-4-trichloro,,,,,,94,");
        lines.Should().Contain("Water,,,,,,0.02,");
        lines.Should().Contain("Oxygen,,,,,,0.01,");
        lines.Should().Contain("Nitrogen,,,,,,99.9995,");
    }

    [Fact]
    public void BuildFileName_uses_manufacturing_date_sample_name_and_lot_no()
    {
        var row = CreatePpbRow();

        PortPpbCsvExporter.BuildFileName(row).Should().Be("2025-11-18_TSMC-024_20260420004_pass.csv");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }

    private PortPpbCsvExporter CreateExporter(SchedulerCsvExportOptions csvOptions) =>
        new(
            Options.Create(new SchedulerOptions { CsvExport = csvOptions }),
            NullLogger<PortPpbCsvExporter>.Instance);

    private QcDataRow CreatePpbRow() =>
        new()
        {
            Id = "ppb(5900)",
            AnlzTime = new DateTime(2025, 11, 18, 14, 30, 0),
            Port = "PORT 2",
            LotNo = "20260420004",
            DataFilename = "Quant.txt",
            SampleName = "TSMC-024"
        };

    private QuantFileCandidate CreateCandidate()
    {
        var dayFolder = Path.Combine(_rootPath, "20251118");
        Directory.CreateDirectory(dayFolder);

        return new QuantFileCandidate(
            FullPath: Path.Combine(dayFolder, "PORT 2", "Quant.txt"),
            DayFolderPath: dayFolder,
            SourceRootPath: dayFolder,
            OutputRootPath: _rootPath,
            LogicalBatchDate: "20251118",
            IsArchivedInput: false,
            TopFolderName: "PORT 2",
            SourceKind: QuantSourceKind.Port,
            Port: "PORT 2",
            DataFilename: "PORT 2\\Quant.txt",
            DataFilepath: Path.Combine(dayFolder, "PORT 2"));
    }
}
