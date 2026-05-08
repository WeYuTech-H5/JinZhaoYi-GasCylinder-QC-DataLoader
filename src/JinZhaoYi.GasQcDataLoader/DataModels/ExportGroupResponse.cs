namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record ExportGroupResponse(
    string StartDate,
    string EndDate,
    IReadOnlyList<ExportGroup> StdGroups,
    IReadOnlyList<ExportGroup> PortGroups);
