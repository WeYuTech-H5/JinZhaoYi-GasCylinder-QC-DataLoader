using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IQuery2PreviewService
{
    Query2PreviewState CreatePreview(
        DateTime startDate,
        DateTime endDate,
        string rfId,
        IReadOnlyList<string> stdRawIds,
        IReadOnlyList<string> portRawIds,
        IReadOnlyList<Query2ExportRow> rows,
        IReadOnlyList<Query2DynamicAreaField> dynamicAreaFields,
        IReadOnlyList<Query2DynamicAreaPortValue> dynamicAreaPortValues,
        QcResultSettingsDto? qcSettings = null);

    Query2PreviewState Recalculate(
        Query2PreviewState preview,
        QcResultSettingsDto? qcSettings = null);

    Query2PreviewState RecalculateFromCanonical(
        Query2PreviewState canonicalPreview,
        Query2PreviewState submittedPreview,
        QcResultSettingsDto? qcSettings = null);

    IReadOnlyList<Query2ExportRow> ToExportRows(
        Query2PreviewState preview,
        QcResultSettingsDto? qcSettings = null);

    IReadOnlyList<Query2PreviewEditLogRow> BuildEditLogs(
        Query2PreviewState preview,
        string excelExportKey,
        Guid exportSessionId,
        DateTime exportedAt,
        string exportUser);
}
