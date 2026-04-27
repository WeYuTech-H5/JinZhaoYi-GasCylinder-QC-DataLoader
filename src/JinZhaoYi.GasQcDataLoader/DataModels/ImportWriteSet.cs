namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class ImportWriteSet
{
    public List<Query2ExportRow> Query2Rows { get; } = [];

    public List<QcDataRow> StdRawRows { get; } = [];

    public List<QcDataRow> StdAverageRows { get; } = [];

    public List<QcDataRow> StdRpdRows { get; } = [];

    public List<QcDataRow> PortRawRows { get; } = [];

    public List<QcDataRow> PortAverageRows { get; } = [];

    public List<QcDataRow> PortPpbRows { get; } = [];

    public List<QcDataRow> PortRpdRows { get; } = [];

    public int TotalRows =>
        StdRawRows.Count +
        StdAverageRows.Count +
        StdRpdRows.Count +
        PortRawRows.Count +
        PortAverageRows.Count +
        PortPpbRows.Count +
        PortRpdRows.Count;
}
