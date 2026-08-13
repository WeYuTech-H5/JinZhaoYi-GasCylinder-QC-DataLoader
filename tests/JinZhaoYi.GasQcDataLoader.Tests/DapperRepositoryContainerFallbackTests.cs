using System.Dynamic;
using System.Reflection;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Infrastructure;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class DapperRepositoryContainerFallbackTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Dynamic_mapper_uses_mfg_container_when_row_container_is_blank(string? rowContainer)
    {
        var row = MapRow(new Dictionary<string, object?>
        {
            ["Container"] = rowContainer,
            ["MfgContainer"] = "0.5L_Cylinder"
        });

        row.Container.Should().Be("0.5L_Cylinder");
    }

    [Fact]
    public void Dynamic_mapper_preserves_existing_row_container()
    {
        var row = MapRow(new Dictionary<string, object?>
        {
            ["Container"] = "1L_Cylinder",
            ["MfgContainer"] = "0.5L_Cylinder"
        });

        row.Container.Should().Be("1L_Cylinder");
    }

    [Fact]
    public void Dynamic_mapper_keeps_container_null_when_both_sources_are_blank()
    {
        var row = MapRow(new Dictionary<string, object?>
        {
            ["Container"] = " ",
            ["MfgContainer"] = null
        });

        row.Container.Should().BeNull();
    }

    [Theory]
    [InlineData("RawRowsByDateWithMfgContainerSqlFormat")]
    [InlineData("RawRowsByDateRangeWithMfgContainerSqlFormat")]
    public void Raw_queries_supply_mfg_container_with_lot_no_priority_and_si0_fallback(string fieldName)
    {
        var sql = GetSqlFormat(fieldName);

        sql.Should().Contain("mfg.Container AS MfgContainer");
        sql.Should().Contain("FROM dbo.{1} lot");
        sql.Should().Contain("rows.LotNo IS NOT NULL AND lot.LotNo = rows.LotNo");
        sql.Should().Contain("rows.si0_id IS NOT NULL AND TRY_CONVERT(int, lot.si0_id) = rows.si0_id");
        sql.Should().Contain("CASE WHEN rows.LotNo IS NOT NULL AND lot.LotNo = rows.LotNo THEN 0 ELSE 1 END");
    }

    [Fact]
    public void Excel_ppb_history_query_supplies_mfg_container_for_coa_exports()
    {
        var sql = GetSqlFormat("ExcelPpbRowsForCsvSqlFormat");

        sql.Should().Contain("mfg.Container AS MfgContainer");
        sql.Should().Contain("lot.Container");
        sql.Should().Contain("FROM dbo.{1} lot");
    }

    [Fact]
    public void Query2_preview_propagates_mfg_container_fallback_to_ppb_and_removes_unknown_warning()
    {
        var calculationService = new CalculationService();
        var builder = new Query2SelectionExportBuilder(
            calculationService,
            NullLogger<Query2SelectionExportBuilder>.Instance);
        var previewService = new Query2PreviewService(calculationService);
        var rf = new QcDataRow
        {
            Id = "RF-001",
            AnlzTime = new DateTime(2026, 7, 21, 8, 0, 0)
        };
        rf.Areas["Acetone"] = 10m;

        var stdRows = new[]
        {
            CreateMappedRawRow("STD-1", "STD", "STD", "STD-LOT", 1, new DateTime(2026, 7, 21, 9, 0, 0), 100m),
            CreateMappedRawRow("STD-2", "STD", "STD", "STD-LOT", 2, new DateTime(2026, 7, 21, 9, 5, 0), 100m)
        };
        var portRows = new[]
        {
            CreateMappedRawRow("PORT-1", "PORT", "PORT 1", "CC-526369", 1, new DateTime(2026, 7, 21, 10, 0, 0), 100m),
            CreateMappedRawRow("PORT-2", "PORT", "PORT 1", "CC-526369", 2, new DateTime(2026, 7, 21, 10, 5, 0), 100m)
        };

        var rows = builder.BuildRows(rf, stdRows, portRows);
        var preview = previewService.CreatePreview(
            new DateTime(2026, 7, 21),
            new DateTime(2026, 7, 21),
            "RF-001",
            ["STD-1", "STD-2"],
            ["PORT-1", "PORT-2"],
            rows,
            [],
            []);

        var ppbPreview = preview.Rows.Should()
            .ContainSingle(row => row.RowType == Query2ExportRowType.Ppb)
            .Which;
        ppbPreview.CurrentValues["container"].Should().Be("0.5L_Cylinder");

        var warnings = new QcResultEvaluator().BuildParameterWarnings(
            previewService.ToExportRows(preview),
            new QcResultSettingsDto());
        warnings.Should().NotContain(warning => warning.Code == "UnknownContainer");
    }

    [Fact]
    public void Qc_warning_remains_when_row_and_mfg_container_are_both_missing()
    {
        var row = MapRow(new Dictionary<string, object?>
        {
            ["ID"] = "ppb(526369)",
            ["LotNo"] = "CC-526369",
            ["Container"] = null,
            ["MfgContainer"] = null
        });

        var warnings = new QcResultEvaluator().BuildParameterWarnings(
            [new Query2ExportRow(Query2ExportRowType.Ppb, row)],
            new QcResultSettingsDto());

        warnings.Should().ContainSingle(warning => warning.Code == "UnknownContainer");
    }

    private static QcDataRow CreateMappedRawRow(
        string id,
        string sourceKind,
        string port,
        string lotNo,
        int sampleNo,
        DateTime anlzTime,
        decimal acetoneArea)
    {
        var row = MapRow(new Dictionary<string, object?>
        {
            ["ID"] = id,
            ["SourceKind"] = sourceKind,
            ["SourceFolderName"] = $"{sourceKind}-{sampleNo}",
            ["Port"] = port,
            ["LotNo"] = lotNo,
            ["si0_id"] = 526369,
            ["SampleNo"] = sampleNo,
            ["AnlzTime"] = anlzTime,
            ["SampleName"] = sourceKind == "STD" ? "STD-N261" : "TSMC-005",
            ["Container"] = " ",
            ["MfgContainer"] = "0.5L_Cylinder"
        });
        row.Areas["Acetone"] = acetoneArea;
        return row;
    }

    private static QcDataRow MapRow(IReadOnlyDictionary<string, object?> values)
    {
        dynamic row = new ExpandoObject();
        var dictionary = (IDictionary<string, object?>)row;
        foreach (var (key, value) in values)
        {
            dictionary[key] = value;
        }

        return DapperRepository.DynamicToQcDataRow(row);
    }

    private static string GetSqlFormat(string fieldName)
    {
        var field = typeof(DapperRepository).GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Static);

        return field?.GetRawConstantValue() as string
            ?? throw new InvalidOperationException($"{fieldName} was not found.");
    }
}
