namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class MfgJsonImportState
{
    public Dictionary<string, MfgJsonFileState> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, MfgJsonLotState> Lots { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MfgJsonFileState
{
    public string FileName { get; init; } = string.Empty;

    public string Hash { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; init; }

    public int InsertedCount { get; init; }

    public int UpdatedCount { get; init; }

    public int SkippedCount { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed class MfgJsonLotState
{
    public string LotNo { get; init; } = string.Empty;

    public string Si0Id { get; init; } = string.Empty;

    public string SourceFileName { get; init; } = string.Empty;

    public string SourceFileHash { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; init; }

    public string? ErrorMessage { get; init; }
}
