namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record ExportRawOption(
    string Id,
    string? SourceKind,
    string? SourceFolderName,
    string Port,
    string LotNo,
    string? SampleName,
    int? SampleNo,
    DateTime? AnlzTime);
