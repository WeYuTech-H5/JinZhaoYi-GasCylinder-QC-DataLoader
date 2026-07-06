using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IQcResultEvaluator
{
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
