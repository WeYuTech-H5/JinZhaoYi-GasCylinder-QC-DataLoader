using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class Query2PreviewServiceTests
{
    private static readonly string AcetoneAreaKey = "area:Acetone";
    private static readonly string AcetonePpbKey = "ppb:Acetone";

    [Fact]
    public void CreatePreview_marks_only_analyte_value_columns_as_editable()
    {
        var preview = CreatePreview();

        preview.Columns.Where(column => column.Editable)
            .Should()
            .OnlyContain(column =>
                column.Key.StartsWith("area:", StringComparison.Ordinal) ||
                column.Key.StartsWith("ppb:", StringComparison.Ordinal) ||
                column.Key.StartsWith("rt:", StringComparison.Ordinal));
        preview.Columns.Single(column => column.Key == "id").Editable.Should().BeFalse();
        preview.Columns.Single(column => column.Key == AcetoneAreaKey).Editable.Should().BeTrue();
        preview.Columns.Single(column => column.Key == AcetonePpbKey).Editable.Should().BeTrue();
    }

    [Fact]
    public void Recalculate_uses_editable_manual_values_for_downstream_formulas()
    {
        var service = CreateService();
        var preview = CreatePreview(service);
        var firstStdRaw = preview.Rows.First(row => row.RowType == Query2ExportRowType.Raw && row.CurrentValues["port"] == "STD");
        firstStdRaw.CurrentValues[AcetoneAreaKey] = "200";
        firstStdRaw.ManualOverrides[AcetoneAreaKey] = "200";

        var recalculated = service.Recalculate(preview);

        var stdAverage = recalculated.Rows.Single(row => row.RowType == Query2ExportRowType.Avg && row.CurrentValues["port"] == "STD");
        var portRawRows = recalculated.Rows.Where(row => row.RowType == Query2ExportRowType.Raw && row.CurrentValues["port"] == "PORT 2").ToArray();
        var portPpb = recalculated.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        stdAverage.CurrentValues[AcetoneAreaKey].Should().Be("250");
        portRawRows.Select(row => row.CurrentValues[AcetonePpbKey]).Should().Equal("2", "6");
        portPpb.CurrentValues[AcetoneAreaKey].Should().Be("4");
        portPpb.CurrentValues[AcetonePpbKey].Should().Be("4");
    }

    [Fact]
    public void Recalculate_accepts_scientific_notation_decimal_values()
    {
        var service = CreateService();
        var preview = CreatePreview(service);
        var firstStdRaw = preview.Rows.First(row => row.RowType == Query2ExportRowType.Raw && row.CurrentValues["port"] == "STD");
        firstStdRaw.CurrentValues[AcetoneAreaKey] = "1.25E-05";
        firstStdRaw.ManualOverrides[AcetoneAreaKey] = "1.25E-05";

        var recalculated = service.Recalculate(preview);

        var recalculatedRaw = recalculated.Rows.Single(row => row.RowKey == firstStdRaw.RowKey);
        recalculatedRaw.CurrentValues[AcetoneAreaKey].Should().Be("0.0000125");
    }

    [Fact]
    public void CreatePreview_formats_decimal_values_without_scientific_notation()
    {
        var service = CreateService();
        var row = Row("STD1", "STD", "STDLOT", 1, new DateTime(2026, 6, 1, 8, 0, 0), "STD", 0.0000189022765012299187138573m);

        var preview = service.CreatePreview(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 1),
            "RF1",
            ["STD1"],
            [],
            [new Query2ExportRow(Query2ExportRowType.Raw, row)]);

        preview.Rows.Single().CurrentValues[AcetoneAreaKey].Should().Be("0.0000189022765012299187138573");
    }

    [Fact]
    public void Recalculate_rejects_non_editable_manual_fields()
    {
        var service = CreateService();
        var preview = CreatePreview(service);
        preview.Rows[0].ManualOverrides["id"] = "RF-CHANGED";

        var act = () => service.Recalculate(preview);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Field 'id' is not editable.");
    }

    [Fact]
    public void ToExportRows_uses_ppb_manual_value_as_port_ppb_history_area_value()
    {
        var service = CreateService();
        var preview = CreatePreview(service);
        var portPpb = preview.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        portPpb.CurrentValues[AcetonePpbKey] = "9.5";
        portPpb.ManualOverrides[AcetonePpbKey] = "9.5";

        var rows = service.ToExportRows(preview);

        rows.Single(row => row.RowType == Query2ExportRowType.Ppb).Row.Areas["Acetone"].Should().Be(9.5m);
    }

    [Fact]
    public void BuildEditLogs_records_only_final_manual_differences()
    {
        var service = CreateService();
        var preview = CreatePreview(service);
        var firstStdRaw = preview.Rows.First(row => row.RowType == Query2ExportRowType.Raw && row.CurrentValues["port"] == "STD");
        firstStdRaw.CurrentValues[AcetoneAreaKey] = "200";
        firstStdRaw.ManualOverrides[AcetoneAreaKey] = "200";
        var recalculated = service.Recalculate(preview);

        var logs = service.BuildEditLogs(
            recalculated,
            "export-key",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            new DateTime(2026, 6, 3, 12, 0, 0),
            "tester");

        logs.Should().ContainSingle();
        var log = logs[0];
        log.FieldKey.Should().Be(AcetoneAreaKey);
        log.ValueKind.Should().Be("area");
        log.Analyte.Should().Be("Acetone");
        log.OriginalValue.Should().Be("100");
        log.NewValue.Should().Be("200");
        log.RowType.Should().Be(Query2ExportRowType.Raw);
    }

    private static Query2PreviewState CreatePreview(Query2PreviewService? service = null)
    {
        service ??= CreateService();
        var rows = CreateRows();
        return service.CreatePreview(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 3),
            "RF1",
            ["STD1", "STD2"],
            ["PORT1", "PORT2"],
            rows);
    }

    private static Query2PreviewService CreateService() => new(new CalculationService());

    private static IReadOnlyList<Query2ExportRow> CreateRows()
    {
        var builder = new Query2SelectionExportBuilder(
            new CalculationService(),
            NullLogger<Query2SelectionExportBuilder>.Instance);

        var rf = Row("RF1", "STD", "RFLOT", 900, new DateTime(2026, 5, 31, 8, 0, 0), "RF", 10m);
        var stdRows = new[]
        {
            Row("STD1", "STD", "STDLOT", 1, new DateTime(2026, 6, 1, 8, 0, 0), "STD", 100m),
            Row("STD2", "STD", "STDLOT", 1, new DateTime(2026, 6, 1, 8, 15, 0), "STD", 300m)
        };
        var portRows = new[]
        {
            Row("PORT1", "PORT 2", "PORTLOT", 3, new DateTime(2026, 6, 1, 9, 0, 0), "PORT", 50m),
            Row("PORT2", "PORT 2", "PORTLOT", 3, new DateTime(2026, 6, 1, 9, 15, 0), "PORT", 150m)
        };

        return builder.BuildRows(rf, stdRows, portRows);
    }

    private static QcDataRow Row(
        string id,
        string port,
        string lotNo,
        int sampleNo,
        DateTime anlzTime,
        string sourceKind,
        decimal acetone)
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
            DataFilepath = @"C:\GAS\Quant.txt",
            SampleName = $"{port}-{sampleNo:000}",
            SampleType = "TO14C1",
            Si0Id = sampleNo
        };
        row.Areas["Acetone"] = acetone;
        return row;
    }
}
