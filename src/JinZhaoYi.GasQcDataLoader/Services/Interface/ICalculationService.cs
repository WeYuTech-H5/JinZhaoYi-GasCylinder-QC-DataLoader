using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface ICalculationService
{
    void ApplyPortRawPpb(QcDataRow portRaw, QcDataRow rf, QcDataRow activeStdAvg);

    QcDataRow CreateAverageRow(string id, QcDataRow first, QcDataRow second);

    QcDataRow CreateRpdRow(string id, QcDataRow first, QcDataRow second);

    QcDataRow CreatePortPpbRow(string id, QcDataRow portAverage, QcDataRow rf, QcDataRow activeStdAvg);

    QcDataRow CreateStdQcRow(string id, QcDataRow previousStdAverage, QcDataRow currentStdAverage);
}
