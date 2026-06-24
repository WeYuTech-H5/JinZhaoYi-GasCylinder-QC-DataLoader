namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class MfgJsonImportResult
{
    public int InsertedCount { get; init; }

    public int UpdatedCount { get; init; }

    public int SkippedCount { get; init; }

    public IReadOnlyList<MfgJsonImportedLot> Lots { get; init; } = Array.Empty<MfgJsonImportedLot>();

    public IReadOnlyList<MfgJsonSkippedLot> SkippedLots { get; init; } = Array.Empty<MfgJsonSkippedLot>();
}

public sealed class MfgJsonImportedLot
{
    public string LotNo { get; init; } = string.Empty;

    public string Si0Id { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;
}

public sealed class MfgJsonSkippedLot
{
    public string LotNo { get; init; } = string.Empty;

    public string Si0Id { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public IReadOnlyList<string> NullFields { get; init; } = Array.Empty<string>();
}
