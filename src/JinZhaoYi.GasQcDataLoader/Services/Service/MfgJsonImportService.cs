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
            fileState.Status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase) &&
            fileState.SkippedCount == 0)
        {
            logger.LogDebug("MFG JSON file was already imported. FileName={FileName}, Hash={Hash}.", fileName, hash);
            return;
        }

        try
        {
            var records = parser.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            var upsertResult = records.Count == 0
                ? new MfgJsonImportResult()
                : await repository.UpsertMfgJsonLotsAsync(records, fileName, cancellationToken);
            var result = new MfgJsonImportResult
            {
                InsertedCount = upsertResult.InsertedCount,
                UpdatedCount = upsertResult.UpdatedCount,
                SkippedCount = upsertResult.SkippedCount,
                Lots = upsertResult.Lots,
                SkippedLots = upsertResult.SkippedLots
            };
            state.Files[fileName] = new MfgJsonFileState
            {
                FileName = fileName,
                Hash = hash,
                Status = "Succeeded",
                ProcessedAt = processedAt,
                InsertedCount = result.InsertedCount,
                UpdatedCount = result.UpdatedCount,
                SkippedCount = result.SkippedCount
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
                "MFG JSON import succeeded. FileName={FileName}, Inserted={InsertedCount}, Updated={UpdatedCount}, Skipped={SkippedCount}.",
                fileName,
                result.InsertedCount,
                result.UpdatedCount,
                result.SkippedCount);
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
