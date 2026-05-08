using System.Security.Cryptography;
using System.Text.Json;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class ProcessedQuantFileStore(
    IOptions<SchedulerOptions> options,
    ILogger<ProcessedQuantFileStore> logger) : IProcessedQuantFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SchedulerOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<QuantFileCandidate>> FilterUnprocessedAsync(
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (_options.TargetMode != SchedulerTargetMode.AllNewStableFiles || candidates.Count == 0)
        {
            return candidates.ToArray();
        }

        var statePath = ResolveStatePath();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(statePath, cancellationToken);
            var unprocessed = new List<QuantFileCandidate>();

            foreach (var candidate in candidates)
            {
                var fingerprint = await BuildFingerprintAsync(candidate, cancellationToken);
                if (state.Files.TryGetValue(fingerprint.Key, out var existing) &&
                    string.Equals(existing.Sha256, fingerprint.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                unprocessed.Add(candidate);
            }

            return unprocessed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkProcessedAsync(
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (_options.TargetMode != SchedulerTargetMode.AllNewStableFiles ||
            _options.DryRun ||
            candidates.Count == 0)
        {
            return;
        }

        var statePath = ResolveStatePath();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(statePath, cancellationToken);
            foreach (var candidate in candidates)
            {
                var fingerprint = await BuildFingerprintAsync(candidate, cancellationToken);
                state.Files[fingerprint.Key] = new ProcessedQuantFileEntry
                {
                    FullPath = fingerprint.FullPath,
                    Length = fingerprint.Length,
                    LastWriteTimeUtc = fingerprint.LastWriteTimeUtc,
                    Sha256 = fingerprint.Sha256,
                    LogicalBatchDate = candidate.LogicalBatchDate,
                    ProcessedAtUtc = DateTimeOffset.UtcNow
                };
            }

            await SaveStateAsync(statePath, state, cancellationToken);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to update processed Quant state file.");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ResolveStatePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.ProcessedStatePath))
        {
            return Path.GetFullPath(_options.ProcessedStatePath);
        }

        var basePath = string.IsNullOrWhiteSpace(_options.ExportRoot)
            ? _options.WatchRoot
            : _options.ExportRoot;

        return Path.GetFullPath(Path.Combine(basePath, "processed-quant-files.json"));
    }

    private static async Task<ProcessedQuantFileState> LoadStateAsync(string statePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(statePath))
        {
            return new ProcessedQuantFileState();
        }

        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<ProcessedQuantFileState>(stream, JsonOptions, cancellationToken)
            ?? new ProcessedQuantFileState();
    }

    private static async Task SaveStateAsync(
        string statePath,
        ProcessedQuantFileState state,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(statePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
    }

    private static async Task<QuantFileFingerprint> BuildFingerprintAsync(
        QuantFileCandidate candidate,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(candidate.FullPath);
        var info = new FileInfo(fullPath);

        await using var stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        return new QuantFileFingerprint(
            Key: fullPath.ToUpperInvariant(),
            FullPath: fullPath,
            Length: info.Length,
            LastWriteTimeUtc: info.LastWriteTimeUtc,
            Sha256: Convert.ToHexString(hash));
    }

    private sealed record QuantFileFingerprint(
        string Key,
        string FullPath,
        long Length,
        DateTime LastWriteTimeUtc,
        string Sha256);

    private sealed class ProcessedQuantFileState
    {
        public Dictionary<string, ProcessedQuantFileEntry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ProcessedQuantFileEntry
    {
        public string FullPath { get; set; } = string.Empty;

        public long Length { get; set; }

        public DateTime LastWriteTimeUtc { get; set; }

        public string Sha256 { get; set; } = string.Empty;

        public string LogicalBatchDate { get; set; } = string.Empty;

        public DateTimeOffset ProcessedAtUtc { get; set; }
    }
}
