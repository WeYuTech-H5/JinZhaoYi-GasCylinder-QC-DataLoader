using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Processing;

public sealed class MfgJsonImportWorker(
    ILogger<MfgJsonImportWorker> logger,
    IMfgJsonImportService importService,
    IOptions<SchedulerOptions> options) : BackgroundService
{
    private readonly SchedulerMfgJsonImportOptions _options = options.Value.MfgJsonImport;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("MFG JSON import worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingFilesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task ProcessPendingFilesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_options.WatchDirectory))
        {
            logger.LogWarning("MFG JSON watch directory does not exist: {WatchDirectory}.", _options.WatchDirectory);
            return;
        }

        logger.LogInformation(
            "MFG JSON scan started. WatchDirectory={WatchDirectory}, FilePattern={FilePattern}.",
            _options.WatchDirectory,
            _options.FilePattern);

        var files = Directory
            .EnumerateFiles(_options.WatchDirectory, _options.FilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var processedCount = 0;
        var skippedUnstableCount = 0;
        var failedCount = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsStable(file))
            {
                skippedUnstableCount++;
                logger.LogInformation("MFG JSON file is not stable yet. File={File}.", file);
                continue;
            }

            try
            {
                await importService.ProcessFileAsync(file, cancellationToken);
                processedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCount++;
                logger.LogError(ex, "MFG JSON import failed. File={File}.", file);
            }
        }

        logger.LogInformation(
            "MFG JSON scan finished. Found={FoundCount}, Processed={ProcessedCount}, SkippedUnstable={SkippedUnstableCount}, Failed={FailedCount}.",
            files.Length,
            processedCount,
            skippedUnstableCount,
            failedCount);
    }

    private bool IsStable(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists)
        {
            return false;
        }

        return DateTimeOffset.Now - info.LastWriteTime >= TimeSpan.FromSeconds(Math.Max(1, _options.StableFileSeconds));
    }
}
