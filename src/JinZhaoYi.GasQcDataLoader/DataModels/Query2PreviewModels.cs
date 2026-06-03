namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class Query2ExcelPreviewRequest
{
    public string? StartDate { get; set; }

    public string? EndDate { get; set; }

    public string? RfId { get; set; }

    public IReadOnlyList<string> StdRawIds { get; set; } = [];

    public IReadOnlyList<string> PortRawIds { get; set; } = [];
}

public sealed class Query2PreviewRecalculateRequest
{
    public Query2PreviewState? Preview { get; set; }
}

public sealed class Query2PreviewExportRequest
{
    public Query2PreviewState? Preview { get; set; }
}

public sealed class Query2PreviewState
{
    public string? StartDate { get; set; }

    public string? EndDate { get; set; }

    public string? ExportDateText { get; set; }

    public string? RfId { get; set; }

    public IReadOnlyList<string> StdRawIds { get; set; } = [];

    public IReadOnlyList<string> PortRawIds { get; set; } = [];

    public IReadOnlyList<Query2PreviewColumn> Columns { get; set; } = [];

    public IReadOnlyList<Query2PreviewRow> Rows { get; set; } = [];
}

public sealed class Query2PreviewColumn
{
    public string Key { get; set; } = string.Empty;

    public string Header { get; set; } = string.Empty;

    public int Order { get; set; }

    public bool Editable { get; set; }

    public string? ValueKind { get; set; }

    public string? Analyte { get; set; }

    public string DataType { get; set; } = "text";
}

public sealed class Query2PreviewRow
{
    public string RowKey { get; set; } = string.Empty;

    public Query2ExportRowType RowType { get; set; }

    public string RowTypeName => RowType.ToString();

    public string? DisplayId { get; set; }

    public Dictionary<string, string?> OriginalValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> CurrentValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> ManualOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Query2PreviewFormula? Formula { get; set; }
}

public sealed class Query2PreviewFormula
{
    public string Kind { get; set; } = string.Empty;

    public IReadOnlyList<string> SourceRowKeys { get; set; } = [];

    public string? StdAverageRowKey { get; set; }
}

public sealed record Query2PreviewEditLogRow(
    Guid ExportSessionId,
    string ExcelExportKey,
    DateTime StartDate,
    DateTime EndDate,
    string RfId,
    string StdRawIds,
    string PortRawIds,
    string RowKey,
    Query2ExportRowType RowType,
    string? RowDisplayId,
    string FieldKey,
    string ValueKind,
    string Analyte,
    string? OriginalValue,
    string? NewValue,
    DateTime ExportedAt,
    string ExportUser,
    DateTime CreateTime);
