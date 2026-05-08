namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class Query2ExcelExportRequest
{
    public string? StartDate { get; set; }

    public string? EndDate { get; set; }

    public string? RfId { get; set; }

    public IReadOnlyList<string> StdRawIds { get; set; } = [];

    public IReadOnlyList<string> PortRawIds { get; set; } = [];
}
