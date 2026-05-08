namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record ImportErrorReportRow(
    DateTimeOffset OccurredAt,
    string LogicalBatchDate,
    string Port,
    string TopFolderName,
    string LotNo,
    string QuantPath,
    string DataFolderPath,
    string ErrorType,
    string Message,
    string SuggestedAction);
