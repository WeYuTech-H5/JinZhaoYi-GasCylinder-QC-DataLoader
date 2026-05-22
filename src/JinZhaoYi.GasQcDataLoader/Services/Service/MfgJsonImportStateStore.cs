using System.Text.Json;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class MfgJsonImportStateStore(IOptions<SchedulerOptions> options) : IMfgJsonImportStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SchedulerMfgJsonImportOptions _options = options.Value.MfgJsonImport;

    public async Task<MfgJsonImportState> LoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.StateFilePath) || !File.Exists(_options.StateFilePath))
        {
            return new MfgJsonImportState();
        }

        await using var stream = File.OpenRead(_options.StateFilePath);
        var state = await JsonSerializer.DeserializeAsync<MfgJsonImportState>(stream, JsonOptions, cancellationToken);
        return NormalizeState(state);
    }

    public async Task SaveAsync(MfgJsonImportState state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.StateFilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_options.StateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 狀態 JSON 只是稽核與避免同檔同 hash 重跑，不是 MFG LOT 的主資料來源。
        var temporaryPath = _options.StateFilePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _options.StateFilePath, overwrite: true);
    }

    private static MfgJsonImportState NormalizeState(MfgJsonImportState? state)
    {
        if (state is null)
        {
            return new MfgJsonImportState();
        }

        return new MfgJsonImportState
        {
            Files = new Dictionary<string, MfgJsonFileState>(state.Files, StringComparer.OrdinalIgnoreCase),
            Lots = new Dictionary<string, MfgJsonLotState>(state.Lots, StringComparer.OrdinalIgnoreCase)
        };
    }
}
