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
        IReadOnlyList<Query2DynamicAreaPortValue> dynamicAreaPortValues);

    Query2PreviewState Recalculate(Query2PreviewState preview);

    IReadOnlyList<Query2ExportRow> ToExportRows(Query2PreviewState preview);

    IReadOnlyList<Query2PreviewEditLogRow> BuildEditLogs(
        Query2PreviewState preview,
        string excelExportKey,
        Guid exportSessionId,
        DateTime exportedAt,
        string exportUser);
}
