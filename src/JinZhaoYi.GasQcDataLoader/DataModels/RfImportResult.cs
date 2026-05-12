namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record RfImportResult(
    RfOption Rf,
    string SourceJsonPath,
    IReadOnlyList<string> Warnings);
