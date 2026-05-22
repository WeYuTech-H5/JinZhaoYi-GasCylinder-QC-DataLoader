using System.Security.Cryptography;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class MfgJsonImportService(
    IMfgJsonParser parser,
    IMfgJsonImportStateStore stateStore,
    IDapperRepository repository,
    ILogger<MfgJsonImportService> logger) : IMfgJsonImportService
{
    public async Task ProcessFileAsync(string path, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(path);
        var bytes = await ReadAllBytesAsync(path, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var state = await stateStore.LoadAsync(cancellationToken);
        var processedAt = DateTimeOffset.Now;

        if (state.Files.TryGetValue(fileName, out var fileState) &&
            fileState.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase) &&
            fileState.Status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("MFG JSON file was already imported. FileName={FileName}, Hash={Hash}.", fileName, hash);
            return;
        }

        try
        {
            var records = parser.Parse(System.Text.Encoding.UTF8.GetString(bytes));

            // 這裡只處理成功由舊 DB 產生的 MFG JSON，使用 upsert 避免同檔重送或人工重跑造成 LOT 重複。
            var result = await repository.UpsertMfgJsonLotsAsync(records, fileName, cancellationToken);
            state.Files[fileName] = new MfgJsonFileState
            {
                FileName = fileName,
                Hash = hash,
                Status = "Succeeded",
                ProcessedAt = processedAt,
                InsertedCount = result.InsertedCount,
                UpdatedCount = result.UpdatedCount
            };

            foreach (var lot in result.Lots)
            {
                state.Lots[lot.LotNo] = new MfgJsonLotState
                {
                    LotNo = lot.LotNo,
                    Si0Id = lot.Si0Id,
                    SourceFileName = fileName,
                    SourceFileHash = hash,
                    Status = "Succeeded",
                    Action = lot.Action,
                    ProcessedAt = processedAt
                };
            }

            await stateStore.SaveAsync(state, cancellationToken);
            logger.LogInformation(
                "MFG JSON import succeeded. FileName={FileName}, Inserted={InsertedCount}, Updated={UpdatedCount}.",
                fileName,
                result.InsertedCount,
                result.UpdatedCount);
        }
        catch (Exception ex)
        {
            state.Files[fileName] = new MfgJsonFileState
            {
                FileName = fileName,
                Hash = hash,
                Status = "Failed",
                ProcessedAt = processedAt,
                ErrorMessage = ex.Message
            };

            await stateStore.SaveAsync(state, cancellationToken);
            throw;
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == bytes.Length ? bytes : bytes[..offset];
    }
}
