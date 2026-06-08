namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class ExportRequest
{
    public string? BatchDate { get; set; }

    public string? StartDate { get; set; }

    public string? EndDate { get; set; }

    public IReadOnlyList<string> SelectedIds { get; set; } = [];
}
