namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record ExportGroup(
    string GroupId,
    string? SourceKind,
    string Port,
    string LotNo,
    string? SampleName,
    IReadOnlyList<ExportRawOption> Rows);
