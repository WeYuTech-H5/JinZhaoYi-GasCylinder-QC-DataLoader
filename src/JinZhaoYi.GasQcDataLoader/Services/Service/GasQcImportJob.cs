using System.Globalization;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

/// <summary>
/// Runs one Gas QC import cycle.
/// </summary>
public sealed class GasQcImportJob(
    ILogger<GasQcImportJob> logger,
    IGasFolderScanner scanner,
    IImportOrchestrator orchestrator,
    IOptions<SchedulerOptions> options) : Job
{
    private readonly SchedulerOptions _options = options.Value;

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var cycleStartedAt = DateTimeOffset.Now;
        var cycleStatus = "running";
        var totalCandidates = 0;
        var processedCount = 0;
        var successCount = 0;
        var failedCount = 0;
        var plannedRowCount = 0;
        var movedCount = 0;
        var moveFailedCount = 0;

        var stableAge = TimeSpan.FromMinutes(Math.Max(0, _options.StableFolderMinutes));

        try
        {
            var targetDayFolderName = ResolveTargetDayFolderName(DateTime.Today, _options);
            var allStableCandidates = scanner.FindStableQuantFiles(_options.WatchRoot, stableAge).ToArray();
            var candidates = allStableCandidates
                .Where(candidate => string.Equals(
                    GasFolderScanner.ResolveCandidateBusinessDate(candidate),
                    targetDayFolderName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            totalCandidates = candidates.Length;

            logger.LogInformation(
                "Starting Gas QC import. WatchRoot={WatchRoot}, TargetDayFolder={TargetDayFolder}, BackfillEnabled={BackfillEnabled}, AllStableQuantFiles={AllStableCount}, TargetQuantFiles={Count}, RunOnce={RunOnce}, DryRun={DryRun}.",
                _options.WatchRoot,
                targetDayFolderName,
                _options.BackfillEnabled,
                allStableCandidates.Length,
                candidates.Length,
                _options.RunOnce,
                _options.DryRun);

            var dayGroups = candidates
                .GroupBy(candidate => candidate.DayFolderPath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var dayGroup in dayGroups)
            {
                var groupCandidates = dayGroup
                    .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                processedCount += groupCandidates.Length;

                var result = await orchestrator.ImportCandidatesAsync(groupCandidates, cancellationToken);
                plannedRowCount += result.PlannedRowCount;

                foreach (var message in result.Messages)
                {
                    logger.LogInformation("Import message for {DayFolder}: {Message}", dayGroup.Key, message);
                }

                if (!result.Succeeded)
                {
                    failedCount++;
                    logger.LogWarning("Import failed for {DayFolder}; remaining groups are skipped.", dayGroup.Key);
                    break;
                }

                successCount += groupCandidates.Length;

                if (!_options.DryRun && _options.MoveProcessedFilesToDone)
                {
                    foreach (var candidate in groupCandidates)
                    {
                        if (TryMoveProcessedCandidate(candidate))
                        {
                            movedCount++;
                        }
                        else
                        {
                            moveFailedCount++;
                        }
                    }
                }
            }

            cycleStatus = failedCount > 0 ? "failed" : "succeeded";
        }
        catch
        {
            cycleStatus = "failed";
            throw;
        }
        finally
        {
            var skippedCount = Math.Max(0, totalCandidates - processedCount);
            var elapsedSeconds = (DateTimeOffset.Now - cycleStartedAt).TotalSeconds;

            logger.LogInformation(
                "Finished Gas QC import. Status={Status}, TotalCandidates={TotalCandidates}, ProcessedCount={ProcessedCount}, SuccessCount={SuccessCount}, FailedCount={FailedCount}, SkippedCount={SkippedCount}, PlannedRowCount={PlannedRowCount}, MovedCount={MovedCount}, MoveFailedCount={MoveFailedCount}, DryRun={DryRun}, RunOnce={RunOnce}, ElapsedSeconds={ElapsedSeconds:N2}.",
                cycleStatus,
                totalCandidates,
                processedCount,
                successCount,
                failedCount,
                skippedCount,
                plannedRowCount,
                movedCount,
                moveFailedCount,
                _options.DryRun,
                _options.RunOnce,
                elapsedSeconds);
        }
    }

    internal static string ResolveTargetDayFolderName(DateTime today, SchedulerOptions options)
    {
        if (!options.BackfillEnabled)
        {
            return today
                .AddDays(options.NormalTargetDayOffset)
                .ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(options.BackfillTargetDate))
        {
            throw new InvalidOperationException("Scheduler:BackfillTargetDate is required when Scheduler:BackfillEnabled is true. Use yyyyMMdd, for example 20260415.");
        }

        if (!DateTime.TryParseExact(
            options.BackfillTargetDate,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _))
        {
            throw new InvalidOperationException($"Scheduler:BackfillTargetDate is invalid: '{options.BackfillTargetDate}'. Use yyyyMMdd, for example 20260415.");
        }

        return options.BackfillTargetDate;
    }

    private bool TryMoveProcessedCandidate(QuantFileCandidate candidate)
    {
        try
        {
            MoveProcessedCandidate(candidate);
            return true;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to move processed candidate {Path}", candidate.DataFilepath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Failed to move processed candidate {Path}", candidate.DataFilepath);
            return false;
        }
    }

    private void MoveProcessedCandidate(QuantFileCandidate candidate)
    {
        var destinationRoot = Path.Combine(
            candidate.DayFolderPath,
            _options.DoneFolderName,
            candidate.TopFolderName);

        Directory.CreateDirectory(destinationRoot);

        if (Directory.Exists(candidate.DataFilepath) &&
            Path.GetFileName(candidate.DataFilepath).EndsWith(".D", StringComparison.OrdinalIgnoreCase))
        {
            var destination = ResolveUniquePath(
                Path.Combine(destinationRoot, Path.GetFileName(candidate.DataFilepath)));

            Directory.Move(candidate.DataFilepath, destination);

            logger.LogInformation(
                "Moved processed data folder from {Source} to {Destination}.",
                candidate.DataFilepath,
                destination);

            return;
        }

        var fileDestination = ResolveUniquePath(
            Path.Combine(destinationRoot, Path.GetFileName(candidate.FullPath)));

        File.Move(candidate.FullPath, fileDestination);

        logger.LogInformation(
            "Moved processed Quant file from {Source} to {Destination}.",
            candidate.FullPath,
            fileDestination);
    }

    private static string ResolveUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

        for (var index = 1; ; index++)
        {
            var suffix = index == 1
                ? timestamp
                : $"{timestamp}_{index}";

            var candidate = Path.Combine(
                directory,
                $"{fileName}_{suffix}{extension}");

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
