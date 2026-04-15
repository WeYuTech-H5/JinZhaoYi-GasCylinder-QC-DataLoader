namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class ParsedQuantFile
{
    public required QuantFileCandidate Source { get; init; }

    public required DateTime AcquiredAt { get; init; }

    public required string DataFile { get; init; }

    public required string Sample { get; init; }

    public required string Misc { get; init; }

    public required string LotNo { get; init; }

    public required int SampleNo { get; init; }

    public IReadOnlyDictionary<string, QuantCompound> Compounds { get; init; } =
        new Dictionary<string, QuantCompound>(StringComparer.OrdinalIgnoreCase);
}
