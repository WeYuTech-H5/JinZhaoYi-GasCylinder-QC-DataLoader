namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class CoaLargeExportRequest
{
    public string? BatchDate { get; set; }

    public IReadOnlyList<string> SelectedIds { get; set; } = [];

    public string? TemplateType { get; set; }
}

public sealed class CoaSmallExportRequest
{
    public string? BatchDate { get; set; }

    public IReadOnlyList<string> SelectedIds { get; set; } = [];

    public int? CardsPerPage { get; set; }
}

public enum CoaLargeTemplateType
{
    Standard,
    Yadong
}
