using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IRawRowFactory
{
    QcDataRow Create(ParsedQuantFile parsed, MfgLot? lot, string id);
}
