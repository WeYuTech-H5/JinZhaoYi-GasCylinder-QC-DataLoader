namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record PagedExportGroupResponse(
    string StartDate,
    string EndDate,
    IReadOnlyList<ExportGroup> StdGroups,
    IReadOnlyList<ExportGroup> PortGroups,
    int Page,
    int PageSize,
    int TotalCount);
