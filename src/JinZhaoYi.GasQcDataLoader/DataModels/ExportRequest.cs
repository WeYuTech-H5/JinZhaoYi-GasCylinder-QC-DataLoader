namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class ExportRequest
{
    public string? BatchDate { get; set; }

    public IReadOnlyList<string> SelectedIds { get; set; } = [];
}
