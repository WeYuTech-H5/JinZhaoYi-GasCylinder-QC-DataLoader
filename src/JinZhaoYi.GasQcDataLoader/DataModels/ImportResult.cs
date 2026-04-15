namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record ImportResult(
    string DayFolderPath,
    int QuantFileCount,
    int PlannedRowCount,
    bool DryRun,
    bool Succeeded,
    IReadOnlyList<string> Messages)
{
    public static ImportResult Failed(string dayFolderPath, int quantFileCount, params string[] messages) =>
        new(dayFolderPath, quantFileCount, 0, false, false, messages);
}
