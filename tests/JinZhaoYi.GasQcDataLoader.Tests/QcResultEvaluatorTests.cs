using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class QcResultEvaluatorTests
{
    private readonly QcResultEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_returns_fail_when_ppb_is_outside_configured_range()
    {
        var row = CreatePpbRow();
        row.Areas["Acetone"] = 120m;

        var update = _evaluator.Evaluate(row, [CreateRawRow("port 1 1000>950 #20260615001")], new QcDataRow { Id = "RF-001" }, CreateSettings());

        update.Should().NotBeNull();
        update!.Result.Should().Be(QcResultValues.Fail);
        update.FailDesc.Should().Be("Conc(Acetone)");
        update.RfId.Should().Be("RF-001");
        update.ProdOrder.Should().Be("001");
        update.CalId.Should().Be("ppb(5900)");
        row.QcResult.Should().Be(QcResultValues.Fail);
    }

    [Fact]
    public void Evaluate_returns_fail_when_pressure_is_below_configured_min()
    {
        var row = CreatePpbRow();
        row.Areas["Acetone"] = 100m;

        var update = _evaluator.Evaluate(row, [CreateRawRow("port 1 903 872> #20260615001")], new QcDataRow { Id = "RF-001" }, CreateSettings());

        update.Should().NotBeNull();
        update!.Result.Should().Be(QcResultValues.Fail);
        update.FailDesc.Should().Be("壓力不足");
        update.IniPrs.Should().Be("872");
        update.FnlPrs.Should().BeNull();
    }

    [Fact]
    public void Evaluate_returns_pass_when_pressure_and_concentration_are_in_range()
    {
        var row = CreatePpbRow();
        row.Areas["Acetone"] = 100m;

        var update = _evaluator.Evaluate(row, [CreateRawRow("port 1 1000>950 #20260615001")], new QcDataRow { Id = "RF-001" }, CreateSettings());

        update.Should().NotBeNull();
        update!.Result.Should().Be(QcResultValues.Pass);
        update.FailDesc.Should().BeNull();
        update.IniPrs.Should().Be("1000");
        update.FnlPrs.Should().Be("950");
    }

    [Fact]
    public void Evaluate_does_not_parse_sample_no_as_pressure_when_final_pressure_is_missing()
    {
        var row = CreatePpbRow();
        row.Areas["Acetone"] = 100m;
        var settings = CreateSettingsWithoutFinalPressureMin();

        var update = _evaluator.Evaluate(row, [CreateRawRow("port 5 205 1088>  #20260629006")], new QcDataRow { Id = "RF-001" }, settings);

        update.Should().NotBeNull();
        update!.Result.Should().Be(QcResultValues.Pass);
        update.FailDesc.Should().BeNull();
        update.IniPrs.Should().Be("1088");
        update.FnlPrs.Should().BeNull();
    }

    [Fact]
    public void BuildParameterWarnings_reports_missing_settings_for_used_container_only()
    {
        var row = CreatePpbRow();
        var settings = CreateCompleteSettingsWithMissing05Values();

        var warnings = _evaluator.BuildParameterWarnings(
            [new Query2ExportRow(Query2ExportRowType.Ppb, row)],
            settings);

        warnings.Select(warning => warning.Message)
            .Should()
            .Contain("0.5L 壓力下限未完整設定：分析後壓力")
            .And.Contain("0.5L 濃度 MIN 未設定：Acetone");
        warnings.Should().OnlyContain(warning => !warning.Message.Contains("1L", StringComparison.OrdinalIgnoreCase));
    }

    private static QcDataRow CreatePpbRow() =>
        new()
        {
            Id = "ppb(5900)",
            Si0Id = 5900,
            LotNo = "20260615001",
            Port = "PORT 1",
            Container = "0.5L_Cylinder",
            AnlzTime = new DateTime(2026, 6, 15, 9, 30, 0)
        };

    private static QcDataRow CreateRawRow(string description) =>
        new()
        {
            LotNo = "20260615001",
            Port = "PORT 1",
            Description = description,
            AnlzTime = new DateTime(2026, 6, 15, 9, 0, 0),
            SampleNo = 1
        };

    private static QcResultSettingsDto CreateSettings() =>
        new()
        {
            PressureRules =
            [
                new QcPressureRuleDto(QcResultSettingRules.Container05, 900m, 900m),
                new QcPressureRuleDto(QcResultSettingRules.Container1L, 900m, 900m)
            ],
            ConcentrationRules =
            [
                new QcConcentrationRuleDto("Acetone", "Acetone", 1, 90m, 110m, 80m, 120m)
            ]
        };

    private static QcResultSettingsDto CreateSettingsWithoutFinalPressureMin() =>
        new()
        {
            PressureRules =
            [
                new QcPressureRuleDto(QcResultSettingRules.Container05, 900m, null),
                new QcPressureRuleDto(QcResultSettingRules.Container1L, 900m, null)
            ],
            ConcentrationRules =
            [
                new QcConcentrationRuleDto("Acetone", "Acetone", 1, 90m, 110m, 80m, 120m)
            ]
        };

    private static QcResultSettingsDto CreateCompleteSettingsWithMissing05Values() =>
        new()
        {
            PressureRules =
            [
                new QcPressureRuleDto(QcResultSettingRules.Container05, 900m, null),
                new QcPressureRuleDto(QcResultSettingRules.Container1L, 900m, 900m)
            ],
            ConcentrationRules = CompoundMap.Analytes
                .Select((analyte, index) => new QcConcentrationRuleDto(
                    analyte.Suffix,
                    analyte.QuantName,
                    index + 1,
                    string.Equals(analyte.Suffix, "Acetone", StringComparison.OrdinalIgnoreCase) ? null : 0m,
                    1000m,
                    0m,
                    1000m))
                .ToArray()
        };
}
