using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class Query2PreviewServiceTests
{
    private static readonly string AcetoneAreaKey = "area:Acetone";
    private static readonly string AcetonePpbKey = "ppb:Acetone";
    private static readonly string QcIniPrsKey = "qc:iniPrs";
    private static readonly string QcIniPrsMinKey = "qc:iniPrsMin";
    private static readonly string QcFnlPrsKey = "qc:fnlPrs";
    private static readonly string QcFnlPrsMinKey = "qc:fnlPrsMin";
    private static readonly string QcPressureResultKey = "qc:pressureResult";
    private static readonly string QcResultKey = "qc:result";
    private static readonly string QcFailDescKey = "qc:failDesc";

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
            [new Query2ExportRow(Query2ExportRowType.Raw, row)],
            [],
            []);

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
    public void Recalculate_preserves_qc_parameter_warnings()
    {
        var service = CreateService();
        var preview = CreatePreview(service);
        preview.QcParameterWarnings =
        [
            new QcParameterWarningDto("PressureMinMissing", "0.5L 壓力下限未完整設定：分析後壓力", "0.5L")
        ];

        var recalculated = service.Recalculate(preview);

        recalculated.QcParameterWarnings.Should().ContainSingle()
            .Which.Message.Should().Be("0.5L 壓力下限未完整設定：分析後壓力");
    }

    [Fact]
    public void CreatePreview_adds_read_only_qc_columns_and_populates_ppb_rows_only()
    {
        var service = CreateService();
        var preview = CreateQcPreview(service, CreateQcSettings());

        var qcColumns = preview.Columns.Where(column => column.ValueKind == "qc").ToArray();
        qcColumns.Select(column => column.Key).Should().Equal(
            QcIniPrsKey,
            QcIniPrsMinKey,
            QcFnlPrsKey,
            QcFnlPrsMinKey,
            QcPressureResultKey,
            QcResultKey,
            QcFailDescKey);
        qcColumns.Should().OnlyContain(column => !column.Editable);

        var raw = preview.Rows.Single(row => row.RowType == Query2ExportRowType.Raw);
        qcColumns.Select(column => raw.CurrentValues[column.Key]).Should().OnlyContain(value => value == null);

        var ppb = preview.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        ppb.CurrentValues[QcIniPrsKey].Should().Be("1050");
        ppb.CurrentValues[QcIniPrsMinKey].Should().Be("1050");
        ppb.CurrentValues[QcFnlPrsKey].Should().Be("949");
        ppb.CurrentValues[QcFnlPrsMinKey].Should().Be("950");
        ppb.CurrentValues[QcPressureResultKey].Should().Be(QcPressureResultValues.Fail);
        ppb.CurrentValues[QcResultKey].Should().Be(QcResultValues.Fail);
        ppb.CurrentValues[QcFailDescKey].Should().Be(
            "分析後壓力不足：949 < MIN 950");
    }

    [Fact]
    public void Recalculate_overwrites_client_supplied_qc_values_with_server_evaluation()
    {
        var service = CreateService();
        var settings = CreateQcSettings();
        var preview = CreateQcPreview(service, settings);
        var ppb = preview.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        ppb.CurrentValues[QcIniPrsMinKey] = "0";
        ppb.CurrentValues[QcPressureResultKey] = QcPressureResultValues.Pass;
        ppb.CurrentValues[QcResultKey] = QcResultValues.Pass;
        ppb.CurrentValues[QcFailDescKey] = null;

        var recalculated = service.Recalculate(preview, settings);

        var recalculatedPpb = recalculated.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        recalculatedPpb.CurrentValues[QcIniPrsMinKey].Should().Be("1050");
        recalculatedPpb.CurrentValues[QcPressureResultKey].Should().Be(QcPressureResultValues.Fail);
        recalculatedPpb.CurrentValues[QcResultKey].Should().Be(QcResultValues.Fail);
        recalculatedPpb.CurrentValues[QcFailDescKey].Should().Be(
            "分析後壓力不足：949 < MIN 950");
    }

    [Fact]
    public void RecalculateFromCanonical_ignores_tampered_qc_source_values_and_untracked_analyte_values()
    {
        var service = CreateService();
        var settings = CreateQcSettings();
        var canonical = CreateQcPreview(service, settings);
        var submitted = CreateQcPreview(service, settings);
        var raw = submitted.Rows.Single(row => row.RowType == Query2ExportRowType.Raw);
        var ppb = submitted.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);

        raw.CurrentValues["description"] = "port 9 9999>9999 #FAKE";
        raw.CurrentValues["lotNo"] = "FAKE-LOT";
        raw.CurrentValues["port"] = "PORT 9";
        ppb.CurrentValues["container"] = "1L_Cylinder";
        ppb.CurrentValues["lotNo"] = "FAKE-LOT";
        ppb.CurrentValues["port"] = "PORT 9";
        ppb.CurrentValues[AcetoneAreaKey] = "1";
        ppb.CurrentValues[AcetonePpbKey] = "1";

        var recalculated = service.RecalculateFromCanonical(canonical, submitted, settings);
        var recalculatedPpb = recalculated.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        var rows = service.ToExportRows(recalculated, settings);
        var recalculatedRaw = rows.Single(row => row.RowType == Query2ExportRowType.Raw).Row;

        recalculatedRaw.Description.Should().Be("port 2 1050>949 #PORTLOT");
        recalculatedRaw.LotNo.Should().Be("PORTLOT");
        recalculatedRaw.Port.Should().Be("PORT 2");
        recalculatedPpb.CurrentValues["container"].Should().Be("0.5L_Cylinder");
        recalculatedPpb.CurrentValues[AcetoneAreaKey].Should().Be("100");
        recalculatedPpb.CurrentValues[QcPressureResultKey].Should().Be(QcPressureResultValues.Fail);
        recalculatedPpb.CurrentValues[QcResultKey].Should().Be(QcResultValues.Fail);
    }

    [Fact]
    public void RecalculateFromCanonical_applies_only_allowlisted_manual_overrides_to_formula_chain()
    {
        var service = CreateService();
        var canonical = CreatePreview(service);
        var submitted = CreatePreview(service);
        var firstStdRaw = submitted.Rows.First(row =>
            row.RowType == Query2ExportRowType.Raw &&
            row.CurrentValues["port"] == "STD");
        firstStdRaw.CurrentValues[AcetoneAreaKey] = "200";
        firstStdRaw.ManualOverrides[AcetoneAreaKey] = "200";
        submitted.Columns = [];
        submitted.DynamicAreaFields = [];

        var recalculated = service.RecalculateFromCanonical(canonical, submitted);

        recalculated.Columns.Should().NotBeEmpty();
        var stdAverage = recalculated.Rows.Single(row =>
            row.RowType == Query2ExportRowType.Avg &&
            row.CurrentValues["port"] == "STD");
        var portPpb = recalculated.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        stdAverage.CurrentValues[AcetoneAreaKey].Should().Be("250");
        portPpb.CurrentValues[AcetoneAreaKey].Should().Be("4");
    }

    [Fact]
    public void RecalculateFromCanonical_uses_server_original_values_for_edit_log()
    {
        var service = CreateService();
        var canonical = CreatePreview(service);
        var submitted = CreatePreview(service);
        var firstStdRaw = submitted.Rows.First(row =>
            row.RowType == Query2ExportRowType.Raw &&
            row.CurrentValues["port"] == "STD");
        firstStdRaw.OriginalValues[AcetoneAreaKey] = "999";
        firstStdRaw.CurrentValues[AcetoneAreaKey] = "200";
        firstStdRaw.ManualOverrides[AcetoneAreaKey] = "200";

        var recalculated = service.RecalculateFromCanonical(canonical, submitted);
        var log = service.BuildEditLogs(
                recalculated,
                "export-key",
                Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                new DateTime(2026, 6, 3, 12, 0, 0),
                "tester")
            .Should()
            .ContainSingle()
            .Which;

        log.OriginalValue.Should().Be("100");
        log.NewValue.Should().Be("200");
    }

    [Fact]
    public void RecalculateFromCanonical_rejects_tampered_row_type_and_formula()
    {
        var service = CreateService();
        var canonical = CreatePreview(service);
        var submittedWithRowType = CreatePreview(service);
        submittedWithRowType.Rows[0].RowType = Query2ExportRowType.Ppb;

        var rowTypeAct = () => service.RecalculateFromCanonical(canonical, submittedWithRowType);

        rowTypeAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid row type*");

        var submittedWithFormula = CreatePreview(service);
        var formulaRow = submittedWithFormula.Rows.First(row => row.Formula is not null);
        formulaRow.Formula!.Kind = "tampered";

        var formulaAct = () => service.RecalculateFromCanonical(canonical, submittedWithFormula);

        formulaAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid formula*");
    }

    [Fact]
    public void RecalculateFromCanonical_rejects_missing_duplicate_or_unknown_row_keys()
    {
        var service = CreateService();
        var canonical = CreatePreview(service);

        var missing = CreatePreview(service);
        missing.Rows = missing.Rows.Skip(1).ToArray();
        var missingAct = () => service.RecalculateFromCanonical(canonical, missing);
        missingAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*no longer match*");

        var duplicate = CreatePreview(service);
        duplicate.Rows[1].RowKey = duplicate.Rows[0].RowKey;
        var duplicateAct = () => service.RecalculateFromCanonical(canonical, duplicate);
        duplicateAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate rowKey*");

        var unknown = CreatePreview(service);
        unknown.Rows[0].RowKey = "unknown-row";
        var unknownAct = () => service.RecalculateFromCanonical(canonical, unknown);
        unknownAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*no longer match*");
    }

    [Fact]
    public void Recalculate_rejects_qc_manual_overrides()
    {
        var service = CreateService();
        var settings = CreateQcSettings();
        var preview = CreateQcPreview(service, settings);
        var ppb = preview.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        ppb.ManualOverrides[QcResultKey] = QcResultValues.Pass;

        var act = () => service.Recalculate(preview, settings);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Field '{QcResultKey}' is not editable.");
    }

    [Fact]
    public void Recalculate_updates_total_result_after_analyte_manual_edit()
    {
        var service = CreateService();
        var settings = CreateQcSettings(iniPrsMin: 1000m, fnlPrsMin: 900m);
        var preview = CreateQcPreview(service, settings);
        var ppb = preview.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        ppb.ManualOverrides[AcetoneAreaKey] = "120";

        var recalculated = service.Recalculate(preview, settings);

        var recalculatedPpb = recalculated.Rows.Single(row => row.RowType == Query2ExportRowType.Ppb);
        recalculatedPpb.CurrentValues[QcPressureResultKey].Should().Be(QcPressureResultValues.Pass);
        recalculatedPpb.CurrentValues[QcResultKey].Should().Be(QcResultValues.Fail);
        recalculatedPpb.CurrentValues[QcFailDescKey].Should().Be(
            "濃度高於 MAX：Acetone");
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
            rows,
            [],
            []);
    }

    private static Query2PreviewService CreateService() => new(new CalculationService());

    private static Query2PreviewState CreateQcPreview(
        Query2PreviewService service,
        QcResultSettingsDto settings)
    {
        var raw = Row(
            "PORT1",
            "PORT 2",
            "PORTLOT",
            3,
            new DateTime(2026, 6, 1, 9, 0, 0),
            "PORT",
            100m);
        raw.Container = "0.5L_Cylinder";
        raw.Description = "port 2 1050>949 #PORTLOT";

        var ppb = raw.DeepClone();
        ppb.Id = "ppb(5900)";
        ppb.Si0Id = 5900;
        ppb.AnlzTime = new DateTime(2026, 6, 1, 9, 15, 0);
        ppb.Areas["Acetone"] = 100m;
        ppb.Ppbs["Acetone"] = 100m;

        return service.CreatePreview(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 1),
            "RF1",
            [],
            ["PORT1"],
            [
                new Query2ExportRow(Query2ExportRowType.Raw, raw),
                new Query2ExportRow(Query2ExportRowType.Ppb, ppb)
            ],
            [],
            [],
            settings);
    }

    private static QcResultSettingsDto CreateQcSettings(
        decimal iniPrsMin = 1050m,
        decimal fnlPrsMin = 950m) =>
        new()
        {
            PressureRules =
            [
                new QcPressureRuleDto(QcResultSettingRules.Container05, iniPrsMin, fnlPrsMin),
                new QcPressureRuleDto(QcResultSettingRules.Container1L, 1050m, 1000m)
            ],
            ConcentrationRules =
            [
                new QcConcentrationRuleDto("Acetone", "Acetone", 1, 90m, 110m, 90m, 110m)
            ]
        };

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
