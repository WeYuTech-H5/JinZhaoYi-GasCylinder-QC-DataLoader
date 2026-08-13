using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IQuery2SelectionExportBuilder
{
    IReadOnlyList<Query2ExportRow> BuildRows(
        QcDataRow rf,
        IReadOnlyCollection<QcDataRow> stdRawRows,
        IReadOnlyCollection<QcDataRow> portRawRows);
}
