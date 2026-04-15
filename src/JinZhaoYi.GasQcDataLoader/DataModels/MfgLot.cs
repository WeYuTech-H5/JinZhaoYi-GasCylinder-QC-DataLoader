namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record MfgLot(
    decimal Id,
    string LotNo,
    string? SampleName,
    string? SampleNo,
    string? SampleType,
    string? Container,
    string? EMVolts,
    string? RelativeEM);
