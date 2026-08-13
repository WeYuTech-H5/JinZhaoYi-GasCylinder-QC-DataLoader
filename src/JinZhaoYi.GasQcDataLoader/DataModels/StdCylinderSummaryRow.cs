namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class StdCylinderSummaryRow
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal?> Areas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Guid? ExcelExportSessionId { get; set; }
}
