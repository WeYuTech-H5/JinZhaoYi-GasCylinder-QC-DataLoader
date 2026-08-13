using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IImportWriteSetBuilder
{
    ImportWriteSet BuildSingleFileWriteSet(ParsedQuantFile parsed, MfgLot lot);

    ImportWriteSet BuildWriteSet(
        IReadOnlyCollection<ParsedQuantFile> parsedFiles,
        IReadOnlyDictionary<string, MfgLot> lots,
        QcDataRow rf);
}
