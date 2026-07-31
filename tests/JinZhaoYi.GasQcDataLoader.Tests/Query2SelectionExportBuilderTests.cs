using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class Query2SelectionExportBuilderTests
{
    [Fact]
    public void BuildRows_calculates_from_selected_rf_std_and_port_rows()
    {
        var builder = new Query2SelectionExportBuilder(
            new CalculationService(),
            NullLogger<Query2SelectionExportBuilder>.Instance);
        var rf = Row("RF,ppb(6233)", "STD", "RFLOT", 903, new DateTime(2026, 3, 20), sourceKind: "Rf", acetone: 10m);
        var stdRows = new[]
        {
            Row("STD1", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 0, 41, 0), sourceKind: "Std", acetone: 100m),
            Row("STD2", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 0, 57, 0), sourceKind: "Std", acetone: 300m),
            Row("STD3", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 12, 16, 0), sourceKind: "Std", acetone: 500m)
        };
        var portRows = new[]
        {
            Row("PORT1", "PORT 4", "20260428013", 33, new DateTime(2026, 4, 30, 16, 19, 0), sourceKind: "Port", acetone: 50m),
            Row("PORT2", "PORT 4", "20260428013", 33, new DateTime(2026, 4, 30, 16, 34, 0), sourceKind: "Port", acetone: 150m)
        };

        var rows = builder.BuildRows(rf, stdRows, portRows);

        rows.Select(row => row.RowType).Should().ContainInOrder(
            Query2ExportRowType.Rf,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Rpd,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Ppb,
            Query2ExportRowType.Rpd);
        rows.Single(row => row.RowType == Query2ExportRowType.Ppb).Row.Areas["Acetone"].Should().Be(2.5m);
        rows.Where(row => row.RowType == Query2ExportRowType.Raw && row.Row.Port == "STD")
            .Select(row => row.Row.Id)
            .Should()
            .Equal("04300041", "04300057", "04301216");
        rows.Single(row => row.RowType == Query2ExportRowType.Avg && row.Row.Port == "STD")
            .Row.Id.Should().Be("AVG(04300057:04301216)");
        rows.Single(row => row.RowType == Query2ExportRowType.Rpd && row.Row.Port == "STD")
            .Row.Id.Should().Be("RPD(04300057:04301216)");
        rows.Where(row => row.RowType == Query2ExportRowType.Raw && row.Row.Port == "PORT 4")
            .Select(row => row.Row.Id)
            .Should()
            .Equal("04301619", "04301634");
        rows.Where(row => row.RowType == Query2ExportRowType.Raw && row.Row.Port == "PORT 4")
            .Select(row => row.Row.Ppbs["Acetone"])
            .Should()
            .Equal(1.25m, 3.75m);
    }

    [Fact]
    public void BuildRows_uses_first_selected_std_average_when_port_is_before_all_std_rows()
    {
        var builder = new Query2SelectionExportBuilder(
            new CalculationService(),
            NullLogger<Query2SelectionExportBuilder>.Instance);
        var rf = Row("RF,ppb(6233)", "STD", "RFLOT", 903, new DateTime(2026, 3, 20), sourceKind: "Rf", acetone: 10m);
        var stdRows = new[]
        {
            Row("STD1", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 0, 41, 0), sourceKind: "Std", acetone: 100m),
            Row("STD2", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 0, 57, 0), sourceKind: "Std", acetone: 300m)
        };
        var portRows = new[]
        {
            Row("PORT1", "PORT 5", "20260428008", 7, new DateTime(2026, 4, 30, 0, 9, 0), sourceKind: "Port", acetone: 50m),
            Row("PORT2", "PORT 5", "20260428008", 7, new DateTime(2026, 4, 30, 0, 25, 0), sourceKind: "Port", acetone: 150m)
        };

        var rows = builder.BuildRows(rf, stdRows, portRows);

        rows.Single(row => row.RowType == Query2ExportRowType.Ppb).Row.Areas["Acetone"].Should().Be(5m);
    }

    [Fact]
    public void BuildRows_keeps_interleaved_std_and_port_time_segments()
    {
        var builder = new Query2SelectionExportBuilder(
            new CalculationService(),
            NullLogger<Query2SelectionExportBuilder>.Instance);
        var rf = Row("RF,ppb(6233)", "STD", "RFLOT", 903, new DateTime(2026, 3, 20), sourceKind: "Rf", acetone: 10m);
        var stdRows = new[]
        {
            Row("STD1", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 10, 0, 0), sourceKind: "Std", acetone: 100m),
            Row("STD2", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 10, 15, 0), sourceKind: "Std", acetone: 300m),
            Row("STD3", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 11, 0, 0), sourceKind: "Std", acetone: 400m),
            Row("STD4", "STD", "20260410008", 904, new DateTime(2026, 4, 30, 11, 15, 0), sourceKind: "Std", acetone: 600m)
        };
        var portRows = new[]
        {
            Row("PORT1", "PORT 2", "20260428013", 33, new DateTime(2026, 4, 30, 10, 30, 0), sourceKind: "Port", acetone: 50m),
            Row("PORT2", "PORT 2", "20260428013", 33, new DateTime(2026, 4, 30, 10, 45, 0), sourceKind: "Port", acetone: 150m),
            Row("PORT3", "PORT 3", "20260428014", 34, new DateTime(2026, 4, 30, 11, 30, 0), sourceKind: "Port", acetone: 900m),
            Row("PORT4", "PORT 3", "20260428014", 34, new DateTime(2026, 4, 30, 11, 45, 0), sourceKind: "Port", acetone: 1100m)
        };

        var rows = builder.BuildRows(rf, stdRows, portRows);

        rows.Select(row => row.RowType).Should().ContainInOrder(
            Query2ExportRowType.Rf,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Rpd,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Ppb,
            Query2ExportRowType.Rpd,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Qc,
            Query2ExportRowType.Rpd,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Ppb,
            Query2ExportRowType.Rpd);
        rows.Where(row => row.RowType == Query2ExportRowType.Ppb)
            .Select(row => row.Row.Areas["Acetone"])
            .Should()
            .Equal(5m, 20m);
    }

    [Fact]
    public void BuildRows_uses_earliest_raw_description_for_ppb_without_changing_raw_descriptions()
    {
        var builder = new Query2SelectionExportBuilder(
            new CalculationService(),
            NullLogger<Query2SelectionExportBuilder>.Instance);
        var rf = Row("RF,ppb(6514)", "STD", "RFLOT", 903, new DateTime(2026, 6, 1), sourceKind: "Rf", acetone: 10m);
        var stdRows = new[]
        {
            Row("STD1", "STD", "20260601001", 904, new DateTime(2026, 7, 1, 1, 0, 0), sourceKind: "Std", acetone: 100m),
            Row("STD2", "STD", "20260601001", 904, new DateTime(2026, 7, 1, 1, 15, 0), sourceKind: "Std", acetone: 100m),
            Row("STD3", "STD", "20260601001", 904, new DateTime(2026, 7, 1, 3, 0, 0), sourceKind: "Std", acetone: 100m),
            Row("STD4", "STD", "20260601001", 904, new DateTime(2026, 7, 1, 3, 15, 0), sourceKind: "Std", acetone: 100m)
        };
        const string completePressure = "port 12  008  1089>1032  #20260629008";
        const string missingFinalPressure = "port 12  008  1089>  #20260629008";
        const string laterSegmentMissingFinalPressure = "port 12  008  1088>  #20260629008";
        var portRows = new[]
        {
            Row(
                "PORT1",
                "PORT 12",
                "20260629008",
                8,
                new DateTime(2026, 7, 1, 2, 25, 0),
                sourceKind: "Port",
                acetone: 100m,
                description: completePressure),
            Row(
                "PORT2",
                "PORT 12",
                "20260629008",
                8,
                new DateTime(2026, 7, 1, 2, 40, 0),
                sourceKind: "Port",
                acetone: 100m,
                description: missingFinalPressure),
            Row(
                "PORT3",
                "PORT 12",
                "20260629008",
                8,
                new DateTime(2026, 7, 1, 4, 0, 0),
                sourceKind: "Port",
                acetone: 100m,
                description: laterSegmentMissingFinalPressure),
            Row(
                "PORT4",
                "PORT 12",
                "20260629008",
                8,
                new DateTime(2026, 7, 1, 4, 15, 0),
                sourceKind: "Port",
                acetone: 100m,
                description: laterSegmentMissingFinalPressure)
        };

        var rows = builder.BuildRows(rf, stdRows, portRows.Reverse().ToArray());

        rows.Where(row => row.RowType == Query2ExportRowType.Raw && row.Row.Port == "PORT 12")
            .Select(row => row.Row.Description)
            .Should()
            .Equal(
                completePressure,
                missingFinalPressure,
                laterSegmentMissingFinalPressure,
                laterSegmentMissingFinalPressure);
        rows.Where(row => row.RowType == Query2ExportRowType.Ppb)
            .Select(row => row.Row.Description)
            .Should()
            .Equal(completePressure, completePressure);
    }

    private static QcDataRow Row(
        string id,
        string port,
        string lotNo,
        int sampleNo,
        DateTime anlzTime,
        string sourceKind,
        decimal acetone,
        string? description = null)
    {
        var row = new QcDataRow
        {
            Id = id,
            Port = port,
            LotNo = lotNo,
            SampleNo = sampleNo,
            AnlzTime = anlzTime,
            SourceKind = sourceKind,
            SourceFolderName = $"{port}[{anlzTime:yyyyMMdd HHmm}]_{sampleNo:000}.D",
            DataFilename = "Quant.txt",
            SampleName = $"{port}-{sampleNo:000}",
            Si0Id = sampleNo,
            Description = description
        };
        row.Areas["Acetone"] = acetone;
        return row;
    }
}
