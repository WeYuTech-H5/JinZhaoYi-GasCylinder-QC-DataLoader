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
        update.FailDesc.Should().Be("濃度高於 MAX：Acetone");
        update.RfId.Should().Be("RF-001");
        update.ProdOrder.Should().Be("1");
        update.CalId.Should().Be("ppb(5900)");
        row.QcResult.Should().Be(QcResultValues.Fail);
    }

    [Theory]
    [InlineData("20260701001", "1")]
    [InlineData("20260701005", "5")]
    [InlineData("20260701010", "10")]
    [InlineData("20260701000", "0")]
    [InlineData("005", "5")]
    [InlineData("LOT-ABC", "ABC")]
    public void Evaluate_normalizes_numeric_prod_order_without_leading_zeroes(
        string lotNo,
        string expectedProdOrder)
    {
        var row = CreatePpbRow();
        row.LotNo = lotNo;
        row.Areas["Acetone"] = 100m;

        var update = _evaluator.Evaluate(
            row,
            [CreateRawRow("port 1 1000>950 #20260615001")],
            new QcDataRow { Id = "RF-001" },
            CreateSettings());

        update.Should().NotBeNull();
        update!.ProdOrder.Should().Be(expectedProdOrder);
    }

    [Fact]
    public void Evaluate_returns_fail_when_pressure_is_below_configured_min()
    {
        var row = CreatePpbRow();
        row.Areas["Acetone"] = 100m;

        var update = _evaluator.Evaluate(row, [CreateRawRow("port 1 903 872> #20260615001")], new QcDataRow { Id = "RF-001" }, CreateSettings());

        update.Should().NotBeNull();
        update!.Result.Should().Be(QcResultValues.Fail);
        update.FailDesc.Should().Be(
            "分析前壓力不足：872 < MIN 900; 分析後壓力缺失：MIN 900");
        update.IniPrs.Should().Be("872");
        update.FnlPrs.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "濃度資料缺失：Acetone")]
    [InlineData(89.0, "濃度低於 MIN：Acetone")]
    public void Evaluate_describes_missing_and_below_min_concentration_clearly(
        double? acetone,
        string expectedFailDesc)
    {
        var row = CreatePpbRow();
        if (acetone.HasValue)
        {
            row.Areas["Acetone"] = (decimal)acetone.Value;
        }

        var update = _evaluator.Evaluate(
            row,
            [CreateRawRow("port 1 1000>950 #20260615001")],
            new QcDataRow { Id = "RF-001" },
            CreateSettings());

        update.Should().NotBeNull();
        update!.Result.Should().Be(QcResultValues.Fail);
        update.FailDesc.Should().Be(expectedFailDesc);
    }

    [Fact]
    public void Evaluate_lists_multiple_missing_concentrations_in_compound_order()
    {
        var row = CreatePpbRow();
        var settings = new QcResultSettingsDto
        {
            PressureRules =
            [
                new QcPressureRuleDto(QcResultSettingRules.Container05, 900m, 900m)
            ],
            ConcentrationRules =
            [
                new QcConcentrationRuleDto(
                    "Chlorobenzene-D5",
                    "Chlorobenzene-D5",
                    1,
                    0m,
                    100m,
                    0m,
                    100m),
                new QcConcentrationRuleDto("HCBD", "HCBD", 2, 0m, 100m, 0m, 100m)
            ]
        };

        var update = _evaluator.Evaluate(
            row,
            [CreateRawRow("port 1 1000>950 #20260615001")],
            new QcDataRow { Id = "RF-001" },
            settings);

        update.Should().NotBeNull();
        update!.FailDesc.Should().Be(
            "濃度資料缺失：Chlorobenzene-D5、HCBD");
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

        var snapshot = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow("port 5 205 1088>  #20260629006")],
            settings);

        snapshot.IniPrs.Should().Be(1088m);
        snapshot.FnlPrs.Should().BeNull();
        snapshot.PressureResult.Should().Be(QcPressureResultValues.NotEvaluated);
        snapshot.Result.Should().Be(QcResultValues.Unknown);
        _evaluator.Evaluate(
                row,
                [CreateRawRow("port 5 205 1088>  #20260629006")],
                new QcDataRow { Id = "RF-001" },
                settings)
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData("0.5L_Cylinder", "1050>950", 1050, 950)]
    [InlineData("1L_Cylinder", "1050>1000", 1050, 1000)]
    public void EvaluateSnapshot_treats_values_equal_to_container_thresholds_as_pass(
        string container,
        string pressureText,
        double expectedIniMin,
        double expectedFnlMin)
    {
        var row = CreatePpbRow();
        row.Container = container;

        var snapshot = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow($"port 1 {pressureText} #20260615001")],
            CreatePressureSettings());

        snapshot.IniPrs.Should().Be((decimal)expectedIniMin);
        snapshot.IniPrsMin.Should().Be((decimal)expectedIniMin);
        snapshot.FnlPrs.Should().Be((decimal)expectedFnlMin);
        snapshot.FnlPrsMin.Should().Be((decimal)expectedFnlMin);
        snapshot.IniPrsFailed.Should().BeFalse();
        snapshot.FnlPrsFailed.Should().BeFalse();
        snapshot.PressureResult.Should().Be(QcPressureResultValues.Pass);
        snapshot.Result.Should().Be(QcResultValues.Pass);
        snapshot.FailDesc.Should().BeNull();
    }

    [Theory]
    [InlineData(
        "1049.9999>950",
        true,
        false,
        "分析前壓力不足：1049.9999 < MIN 1050")]
    [InlineData(
        "1050>949.9999",
        false,
        true,
        "分析後壓力不足：949.9999 < MIN 950")]
    [InlineData(
        "1050>",
        false,
        true,
        "分析後壓力缺失：MIN 950")]
    [InlineData(
        "",
        true,
        true,
        "分析前壓力缺失：MIN 1050; 分析後壓力缺失：MIN 950")]
    public void EvaluateSnapshot_marks_below_or_missing_required_pressure_as_fail(
        string pressureText,
        bool expectedIniFailed,
        bool expectedFnlFailed,
        string expectedFailDesc)
    {
        var row = CreatePpbRow();
        var rawRows = string.IsNullOrWhiteSpace(pressureText)
            ? Array.Empty<QcDataRow>()
            : [CreateRawRow($"port 1 {pressureText} #20260615001")];

        var snapshot = _evaluator.EvaluateSnapshot(row, rawRows, CreatePressureSettings());

        snapshot.IniPrsFailed.Should().Be(expectedIniFailed);
        snapshot.FnlPrsFailed.Should().Be(expectedFnlFailed);
        snapshot.PressureResult.Should().Be(QcPressureResultValues.Fail);
        snapshot.Result.Should().Be(QcResultValues.Fail);
        snapshot.FailDesc.Should().Be(expectedFailDesc);
    }

    [Fact]
    public void EvaluateSnapshot_uses_earliest_raw_when_later_raw_is_missing_final_pressure()
    {
        var row = CreatePpbRow();
        row.LotNo = "20260629008";
        row.Port = "PORT 12";
        var earliest = CreateRawRow("port 12  008  1089>1032  #20260629008");
        earliest.LotNo = row.LotNo;
        earliest.Port = row.Port;
        earliest.AnlzTime = new DateTime(2026, 7, 1, 2, 25, 0);
        var later = CreateRawRow("port 12  008  1089>  #20260629008");
        later.LotNo = row.LotNo;
        later.Port = row.Port;
        later.AnlzTime = new DateTime(2026, 7, 1, 2, 40, 0);

        var snapshot = _evaluator.EvaluateSnapshot(row, [later, earliest], CreatePressureSettings());

        snapshot.IniPrs.Should().Be(1089m);
        snapshot.FnlPrs.Should().Be(1032m);
        snapshot.IniPrsMin.Should().Be(1050m);
        snapshot.FnlPrsMin.Should().Be(950m);
        snapshot.PressureResult.Should().Be(QcPressureResultValues.Pass);
        snapshot.FailDesc.Should().BeNull();
    }

    [Theory]
    [InlineData(
        "1049>1000",
        true,
        false,
        "分析前壓力不足：1049 < MIN 1050")]
    [InlineData(
        "1050>999",
        false,
        true,
        "分析後壓力不足：999 < MIN 1000")]
    [InlineData(
        "1050>",
        false,
        true,
        "分析後壓力缺失：MIN 1000")]
    public void EvaluateSnapshot_applies_1l_pressure_thresholds(
        string pressureText,
        bool expectedIniFailed,
        bool expectedFnlFailed,
        string expectedFailDesc)
    {
        var row = CreatePpbRow();
        row.Container = "1L_Cylinder";

        var snapshot = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow($"port 1 {pressureText} #20260615001")],
            CreatePressureSettings());

        snapshot.IniPrsFailed.Should().Be(expectedIniFailed);
        snapshot.FnlPrsFailed.Should().Be(expectedFnlFailed);
        snapshot.PressureResult.Should().Be(QcPressureResultValues.Fail);
        snapshot.Result.Should().Be(QcResultValues.Fail);
        snapshot.FailDesc.Should().Be(expectedFailDesc);
    }

    [Theory]
    [InlineData("2L_Cylinder")]
    [InlineData("")]
    public void EvaluateSnapshot_reports_not_evaluated_when_container_has_no_applicable_rule(string container)
    {
        var row = CreatePpbRow();
        row.Container = container;

        var snapshot = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow("port 1 100>50 #20260615001")],
            CreatePressureSettings());

        snapshot.IniPrsMin.Should().BeNull();
        snapshot.FnlPrsMin.Should().BeNull();
        snapshot.PressureResult.Should().Be(QcPressureResultValues.NotEvaluated);
        snapshot.Result.Should().Be(QcResultValues.Unknown);
        snapshot.FailDesc.Should().BeNull();
    }

    [Fact]
    public void EvaluateSnapshot_reports_unknown_when_pressure_rule_is_missing_or_inactive()
    {
        var row = CreatePpbRow();
        var noRule = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow("port 1 1050>950 #20260615001")],
            new QcResultSettingsDto());
        var inactiveRule = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow("port 1 1050>950 #20260615001")],
            new QcResultSettingsDto
            {
                PressureRules =
                [
                    new QcPressureRuleDto(QcResultSettingRules.Container05, 1050m, 950m, IsActive: false)
                ]
            });

        noRule.PressureResult.Should().Be(QcPressureResultValues.NotEvaluated);
        noRule.Result.Should().Be(QcResultValues.Unknown);
        inactiveRule.PressureResult.Should().Be(QcPressureResultValues.NotEvaluated);
        inactiveRule.Result.Should().Be(QcResultValues.Unknown);
    }

    [Fact]
    public void EvaluateSnapshot_reports_unknown_for_incomplete_rule_unless_configured_threshold_fails()
    {
        var row = CreatePpbRow();
        var settings = new QcResultSettingsDto
        {
            PressureRules =
            [
                new QcPressureRuleDto(QcResultSettingRules.Container05, 1050m, null)
            ]
        };

        var passingConfiguredThreshold = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow("port 1 1050>950 #20260615001")],
            settings);
        var failingConfiguredThreshold = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow("port 1 1049>950 #20260615001")],
            settings);

        passingConfiguredThreshold.PressureResult.Should().Be(QcPressureResultValues.NotEvaluated);
        passingConfiguredThreshold.Result.Should().Be(QcResultValues.Unknown);
        failingConfiguredThreshold.PressureResult.Should().Be(QcPressureResultValues.Fail);
        failingConfiguredThreshold.Result.Should().Be(QcResultValues.Fail);
        failingConfiguredThreshold.FailDesc.Should().Be(
            "分析前壓力不足：1049 < MIN 1050");
    }

    [Fact]
    public void EvaluateSnapshot_combines_pressure_and_concentration_failures_in_stable_order()
    {
        var row = CreatePpbRow();
        row.Areas["Acetone"] = 120m;

        var snapshot = _evaluator.EvaluateSnapshot(
            row,
            [CreateRawRow("port 1 899>899 #20260615001")],
            CreateSettings());

        snapshot.PressureResult.Should().Be(QcPressureResultValues.Fail);
        snapshot.Result.Should().Be(QcResultValues.Fail);
        snapshot.FailDesc.Should().Be(
            "分析前壓力不足：899 < MIN 900; " +
            "分析後壓力不足：899 < MIN 900; " +
            "濃度高於 MAX：Acetone");
    }

    [Fact]
    public void EvaluateExportRowsDetailed_returns_the_same_snapshot_and_update_result()
    {
        var raw = CreateRawRow("port 1 1050>949 #20260615001");
        raw.SourceKind = "PORT";
        var ppb = CreatePpbRow();

        var batch = _evaluator.EvaluateExportRowsDetailed(
            [
                new Query2ExportRow(Query2ExportRowType.Raw, raw),
                new Query2ExportRow(Query2ExportRowType.Ppb, ppb)
            ],
            "RF-001",
            CreatePressureSettings());

        batch.Snapshots.Should().ContainSingle();
        batch.Updates.Should().ContainSingle();
        batch.Snapshots[0].PressureResult.Should().Be(QcPressureResultValues.Fail);
        batch.Snapshots[0].Result.Should().Be(batch.Updates[0].Result);
        batch.Snapshots[0].FailDesc.Should().Be(batch.Updates[0].FailDesc);
        batch.Updates[0].FnlPrs.Should().Be("949");
    }

    [Fact]
    public void EvaluateExportRowsDetailed_does_not_create_mfg_update_for_unknown_result()
    {
        var raw = CreateRawRow("port 1 1050>950 #20260615001");
        raw.SourceKind = "PORT";
        var ppb = CreatePpbRow();

        var batch = _evaluator.EvaluateExportRowsDetailed(
            [
                new Query2ExportRow(Query2ExportRowType.Raw, raw),
                new Query2ExportRow(Query2ExportRowType.Ppb, ppb)
            ],
            "RF-001",
            new QcResultSettingsDto());

        batch.Snapshots.Should().ContainSingle()
            .Which.Result.Should().Be(QcResultValues.Unknown);
        batch.Updates.Should().BeEmpty();
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

    private static QcResultSettingsDto CreatePressureSettings() =>
        new()
        {
            PressureRules =
            [
                new QcPressureRuleDto(QcResultSettingRules.Container05, 1050m, 950m),
                new QcPressureRuleDto(QcResultSettingRules.Container1L, 1050m, 1000m)
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
