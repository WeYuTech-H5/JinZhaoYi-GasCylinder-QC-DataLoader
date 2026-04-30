namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IQcDownloadFileResolver
{
    string? ResolveCylinderQcWorkbook(string batchDate);

    string? ResolveCsvBySampleName(string sampleName);
}
