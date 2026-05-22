using JinZhaoYi.GasQcDataLoader.Services.Service;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IMfgJsonImportStateStore
{
    Task<MfgJsonImportState> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(MfgJsonImportState state, CancellationToken cancellationToken);
}
