namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IMfgJsonImportService
{
    Task ProcessFileAsync(string path, CancellationToken cancellationToken);
}
