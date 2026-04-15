using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public abstract class Job : IJob
{
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);
}
