using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class RawRowFactory(IOptions<SchedulerOptions> options) : IRawRowFactory
{
    private readonly SchedulerOptions _options = options.Value;

    public QcDataRow Create(ParsedQuantFile parsed, MfgLot? lot, string id)
    {
        // raw row 的固定欄位優先取 Quant；SampleName、Container 等製造資訊由 MFG_LOT 補齊。
        var row = new QcDataRow
        {
            Id = id,
            AnlzTime = parsed.AcquiredAt,
            Inst = _options.InstrumentName,
            Port = parsed.Source.Port,
            Si0Id = lot?.Id.ToString("0"),
            SampleNo = parsed.SampleNo,
            LotNo = parsed.LotNo,
            DataFilename = parsed.Source.DataFilename,
            DataFilepath = parsed.Source.DataFilepath,
            PcName = Environment.MachineName,
            Container = lot?.Container,
            Description = parsed.Misc,
            EmVolts = lot?.EMVolts,
            RelativeEm = lot?.RelativeEM,
            SampleName = lot?.SampleName,
            SampleType = lot?.SampleType ?? _options.SampleType,
            CreateUser = _options.CreateUser,
            CreateTime = DateTime.Now
        };

        // Quant Response 對應 Area_*，R.T. 對應 RT_*；Conc ppb 不在 raw 建立階段寫入。
        foreach (var compound in parsed.Compounds.Values)
        {
            row.Areas[compound.Analyte.Suffix] = compound.Response;
            row.RetentionTimes[compound.Analyte.Suffix] = compound.RetentionTime;
        }

        return row;
    }
}
