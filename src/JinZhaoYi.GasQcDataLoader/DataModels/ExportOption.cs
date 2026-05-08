namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record ExportOption(
    string Id,
    string DisplayName,
    string BatchDate,
    string? SourceKind,
    string? SourceFolderName,
    string Port,
    string LotNo,
    string? SampleName,
    int? SampleNo,
    string? DataFilename,
    string? DataFilepath,
    DateTime? AnlzTime);
