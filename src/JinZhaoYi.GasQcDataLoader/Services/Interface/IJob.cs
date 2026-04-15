namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
