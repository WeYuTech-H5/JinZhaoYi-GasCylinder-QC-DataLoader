namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record ExcelPpbHistorySaveRequest(
    DateTime StartDate,
    DateTime EndDate,
    string RfId,
    IReadOnlyList<string> StdRawIds,
    IReadOnlyList<string> PortRawIds,
    IReadOnlyList<QcDataRow> PpbRows,
    Guid? ExportSessionId,
    DateTime ExportedAt,
    string? ExportUser);
