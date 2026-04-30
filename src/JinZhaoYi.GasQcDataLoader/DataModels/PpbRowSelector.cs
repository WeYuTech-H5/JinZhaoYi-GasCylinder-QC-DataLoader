namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record PpbRowSelector(string? Id, string? LotNo, string? Port, string? DataFilename)
{
    public static PpbRowSelector FromRow(QcDataRow row) =>
        new(row.Id, row.LotNo, row.Port, row.DataFilename);
}
