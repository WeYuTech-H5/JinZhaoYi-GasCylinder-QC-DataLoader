namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record QuantFileCandidate(
    string FullPath,
    string DayFolderPath,
    string SourceRootPath,
    string OutputRootPath,
    string LogicalBatchDate,
    bool IsArchivedInput,
    string TopFolderName,
    QuantSourceKind SourceKind,
    string Port,
    string DataFilename,
    string DataFilepath);
