namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record AnalyteDefinition(
    string Suffix,
    string QuantName,
    string AreaColumn,
    string PpbColumn,
    string RtColumn);
