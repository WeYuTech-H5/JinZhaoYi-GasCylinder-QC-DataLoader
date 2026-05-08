namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record RfOption(
    string? Id,
    DateTime? AnlzTime,
    int? Si0Id,
    string? SampleName,
    int? SampleNo,
    string? Description);
