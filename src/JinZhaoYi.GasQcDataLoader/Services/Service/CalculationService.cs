using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class CalculationService : ICalculationService
{
    public void ApplyPortRawPpb(QcDataRow portRaw, QcDataRow rf, QcDataRow activeStdAvg)
    {
        // PORT raw 的 ppb 不直接採用 Quant Conc，而是用 RF 與當下有效 STD AVG 換算。
        foreach (var analyte in CompoundMap.Analytes)
        {
            portRaw.Ppbs[analyte.Suffix] = CalculatePpb(
                rf.Areas.GetValueOrDefault(analyte.Suffix),
                portRaw.Areas.GetValueOrDefault(analyte.Suffix),
                activeStdAvg.Areas.GetValueOrDefault(analyte.Suffix));
        }
    }

    public QcDataRow CreateAverageRow(string id, QcDataRow first, QcDataRow second)
    {
        var row = second.CloneMetadata();
        row.Id = id;
        row.Id1 = first.Id;
        row.Id2 = second.Id;
        row.RetentionTimes.Clear();
        row.Ppbs.Clear();

        // AVG 表只放 Area 平均值，RT 與 ppb 不參與本列輸出。
        foreach (var analyte in CompoundMap.Analytes)
        {
            row.Areas[analyte.Suffix] = Average(first.Areas.GetValueOrDefault(analyte.Suffix), second.Areas.GetValueOrDefault(analyte.Suffix));
        }

        return row;
    }

    public QcDataRow CreateRpdRow(string id, QcDataRow first, QcDataRow second)
    {
        var row = second.CloneMetadata();
        row.Id = id;
        row.Id1 = first.Id;
        row.Id2 = second.Id;
        row.RetentionTimes.Clear();
        row.Ppbs.Clear();

        // RPD = (MAX - MIN) / AVERAGE(MAX, MIN)，逐一 compound 計算。
        foreach (var analyte in CompoundMap.Analytes)
        {
            row.Areas[analyte.Suffix] = Rpd(first.Areas.GetValueOrDefault(analyte.Suffix), second.Areas.GetValueOrDefault(analyte.Suffix));
        }

        return row;
    }

    public QcDataRow CreatePortPpbRow(string id, QcDataRow portAverage, QcDataRow rf, QcDataRow activeStdAvg)
    {
        var row = portAverage.CloneMetadata();
        row.Id = id;
        row.Id1 = portAverage.Id1;
        row.Id2 = portAverage.Id2;
        row.RetentionTimes.Clear();
        row.Ppbs.Clear();

        // PORT_PPB 表的設計是把 ppb 計算結果寫在 Area_* 欄位。
        foreach (var analyte in CompoundMap.Analytes)
        {
            row.Areas[analyte.Suffix] = CalculatePpb(
                rf.Areas.GetValueOrDefault(analyte.Suffix),
                portAverage.Areas.GetValueOrDefault(analyte.Suffix),
                activeStdAvg.Areas.GetValueOrDefault(analyte.Suffix));
        }

        return row;
    }

    public QcDataRow CreateStdQcRow(string id, QcDataRow previousStdAverage, QcDataRow currentStdAverage)
    {
        var row = currentStdAverage.CloneMetadata();
        row.Id = id;
        row.Id1 = previousStdAverage.Id;
        row.Id2 = currentStdAverage.Id;
        row.RetentionTimes.Clear();
        row.Ppbs.Clear();

        // STD_QC 目前只保留公式實作，尚未寫 DB，等待確認對應表。
        foreach (var analyte in CompoundMap.Analytes)
        {
            var previous = previousStdAverage.Areas.GetValueOrDefault(analyte.Suffix);
            var current = currentStdAverage.Areas.GetValueOrDefault(analyte.Suffix);
            row.Areas[analyte.Suffix] = DifferenceOverAverage(current, previous);
        }

        return row;
    }

    private static decimal? CalculatePpb(decimal? rfValue, decimal? sampleArea, decimal? stdAverageArea)
    {
        if (!rfValue.HasValue ||
            !sampleArea.HasValue ||
            !stdAverageArea.HasValue ||
            rfValue.Value < 0 ||
            sampleArea.Value < 0 ||
            stdAverageArea.Value <= 0)
        {
            return null;
        }

        return rfValue.Value * sampleArea.Value / stdAverageArea.Value;
    }

    private static decimal? Average(decimal? first, decimal? second)
    {
        if (!first.HasValue || !second.HasValue || first.Value < 0 || second.Value < 0)
        {
            return null;
        }

        return (first.Value + second.Value) / 2m;
    }

    private static decimal? Rpd(decimal? first, decimal? second)
    {
        if (!first.HasValue || !second.HasValue || first.Value < 0 || second.Value < 0)
        {
            return null;
        }

        var max = Math.Max(first.Value, second.Value);
        var min = Math.Min(first.Value, second.Value);
        var denominator = (max + min) / 2m;
        return denominator <= 0 ? null : (max - min) / denominator;
    }

    private static decimal? DifferenceOverAverage(decimal? current, decimal? previous)
    {
        if (!current.HasValue || !previous.HasValue || current.Value < 0 || previous.Value < 0)
        {
            return null;
        }

        var denominator = (current.Value + previous.Value) / 2m;
        return denominator <= 0 ? null : (current.Value - previous.Value) / denominator;
    }
}
