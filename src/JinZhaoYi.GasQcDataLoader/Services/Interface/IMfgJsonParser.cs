using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IMfgJsonParser
{
    IReadOnlyList<MfgJsonLotRecord> Parse(string json);
}
