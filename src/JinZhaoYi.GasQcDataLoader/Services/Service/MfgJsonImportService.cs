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
    private const string SkippedNullFieldAction = "SkippedNullField";

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
            var skippedLots = records
                .Where(record => record.NullFields.Count > 0)
                .Select(ToSkippedLot)
                .ToArray();
            var validRecords = records
                .Where(record => record.NullFields.Count == 0)
                .ToArray();

            // Only complete JSON records are written; rows with explicit null fields are tracked as skipped.
            var upsertResult = validRecords.Length == 0
                ? new MfgJsonImportResult()
                : await repository.UpsertMfgJsonLotsAsync(validRecords, fileName, cancellationToken);
            var result = new MfgJsonImportResult
            {
                InsertedCount = upsertResult.InsertedCount,
                UpdatedCount = upsertResult.UpdatedCount,
                SkippedCount = skippedLots.Length,
                Lots = upsertResult.Lots,
                SkippedLots = skippedLots
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

            foreach (var lot in result.SkippedLots)
            {
                state.Lots[lot.LotNo] = new MfgJsonLotState
                {
                    LotNo = lot.LotNo,
                    Si0Id = lot.Si0Id,
                    SourceFileName = fileName,
                    SourceFileHash = hash,
                    Status = "Skipped",
                    Action = SkippedNullFieldAction,
                    ProcessedAt = processedAt,
                    ErrorMessage = lot.Reason
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

    private static MfgJsonSkippedLot ToSkippedLot(MfgJsonLotRecord record)
    {
        var nullFields = record.NullFields.ToArray();
        return new MfgJsonSkippedLot
        {
            LotNo = record.LotNo,
            Si0Id = record.Si0Id,
            NullFields = nullFields,
            Reason = $"MFG JSON contains null field(s): {string.Join(", ", nullFields)}."
        };
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
