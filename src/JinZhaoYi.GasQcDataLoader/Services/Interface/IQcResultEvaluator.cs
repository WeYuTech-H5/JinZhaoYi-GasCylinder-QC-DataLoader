using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IQcResultEvaluator
{
    QcJudgmentSnapshot EvaluateSnapshot(
        QcDataRow ppbRow,
        IReadOnlyList<QcDataRow> portRawRows,
        QcResultSettingsDto settings);

    QcExportEvaluationBatch EvaluateExportRowsDetailed(
        IReadOnlyList<Query2ExportRow> rows,
        string? rfId,
        QcResultSettingsDto settings);

    MfgLotQcUpdate? Evaluate(
        QcDataRow ppbRow,
        IReadOnlyList<QcDataRow> portRawRows,
        QcDataRow? rf,
        QcResultSettingsDto settings);

    IReadOnlyList<MfgLotQcUpdate> EvaluateExportRows(
        IReadOnlyList<Query2ExportRow> rows,
        string? rfId,
        QcResultSettingsDto settings);

    IReadOnlyList<QcParameterWarningDto> BuildParameterWarnings(
        IReadOnlyList<Query2ExportRow> rows,
        QcResultSettingsDto settings);
}
