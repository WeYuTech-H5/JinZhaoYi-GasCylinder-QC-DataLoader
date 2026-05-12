using System.Globalization;
using System.Text.RegularExpressions;
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
    IProcessedQuantFileStore processedQuantFileStore,
    IImportErrorReportExporter importErrorReportExporter,
    IDapperRepository repository,
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
        var errorRows = new List<ImportErrorReportRow>();

        var stableAge = TimeSpan.FromMinutes(Math.Max(0, _options.StableFolderMinutes));

        try
        {
            var allStableCandidates = scanner.FindStableQuantFiles(_options.WatchRoot, stableAge).ToArray();
            var targetDayFolderName = _options.TargetMode == SchedulerTargetMode.AllNewStableFiles
                ? "all-new-stable-files"
                : ResolveTargetDayFolderName(DateTime.Today, _options);
            var candidates = await ResolveCandidatesForCurrentCycleAsync(
                allStableCandidates,
                targetDayFolderName,
                cancellationToken);
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
                .GroupBy(candidate => BuildImportGroupKey(candidate), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var dayGroup in dayGroups)
            {
                var groupCandidates = dayGroup
                    .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                processedCount += groupCandidates.Length;

                ImportResult result;
                try
                {
                    result = await orchestrator.ImportCandidatesAsync(groupCandidates, cancellationToken);
                }
                catch (Exception ex) when (_options.TargetMode == SchedulerTargetMode.AllNewStableFiles && ex is not OperationCanceledException)
                {
                    failedCount++;
                    errorRows.AddRange(BuildErrorRows(groupCandidates, "ParseOrImportException", ex.Message));
                    logger.LogError(ex, "Import failed for {DayFolder}; continuing with remaining new stable groups.", dayGroup.Key);
                    continue;
                }

                plannedRowCount += result.PlannedRowCount;

                foreach (var message in result.Messages)
                {
                    logger.LogInformation("Import message for {DayFolder}: {Message}", dayGroup.Key, message);
                }

                if (!result.Succeeded)
                {
                    failedCount++;
                    errorRows.AddRange(BuildErrorRows(groupCandidates, "ImportValidationFailed", string.Join(Environment.NewLine, result.Messages)));
                    if (_options.TargetMode == SchedulerTargetMode.AllNewStableFiles)
                    {
                        logger.LogWarning("Import failed for {DayFolder}; continuing with remaining new stable groups.", dayGroup.Key);
                        continue;
                    }

                    logger.LogWarning("Import failed for {DayFolder}; remaining groups are skipped.", dayGroup.Key);
                    break;
                }

                successCount += groupCandidates.Length;
                await processedQuantFileStore.MarkProcessedAsync(groupCandidates, cancellationToken);

                if (!_options.DryRun && _options.MoveProcessedFilesToDone)
                {
                    foreach (var candidate in groupCandidates)
                    {
                        if (candidate.IsArchivedInput)
                        {
                            logger.LogInformation(
                                "Skipping move for archived input {Path}.",
                                candidate.DataFilepath);
                            continue;
                        }

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
            if (errorRows.Count > 0)
            {
                try
                {
                    await importErrorReportExporter.ExportAsync(errorRows, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to export import error report.");
                }

                if (!_options.DryRun)
                {
                    try
                    {
                        await repository.UpsertImportErrorLogsAsync(errorRows, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed to write import error logs to database.");
                    }
                }
            }

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

    private async Task<QuantFileCandidate[]> ResolveCandidatesForCurrentCycleAsync(
        IReadOnlyCollection<QuantFileCandidate> allStableCandidates,
        string targetDayFolderName,
        CancellationToken cancellationToken)
    {
        if (_options.TargetMode == SchedulerTargetMode.AllNewStableFiles)
        {
            var candidatesWithLogicalDate = allStableCandidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.LogicalBatchDate))
                .ToArray();

            var unprocessedCandidates = (await processedQuantFileStore.FilterUnprocessedAsync(
                    candidatesWithLogicalDate,
                    cancellationToken))
                .ToArray();

            if (unprocessedCandidates.Length == 0)
            {
                return [];
            }

            var groupsWithNewFiles = unprocessedCandidates
                .Select(BuildImportGroupKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return candidatesWithLogicalDate
                .Where(candidate => groupsWithNewFiles.Contains(BuildImportGroupKey(candidate)))
                .ToArray();
        }

        return allStableCandidates
            .Where(candidate => string.Equals(
                candidate.LogicalBatchDate,
                targetDayFolderName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
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
        var sourceFolder = Directory.GetParent(candidate.DataFilepath)?.FullName
            ?? throw new InvalidOperationException($"Unable to resolve source folder for '{candidate.DataFilepath}'.");
        var destinationRoot = Path.Combine(sourceFolder, _options.DoneFolderName);

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

    private static string BuildImportGroupKey(QuantFileCandidate candidate) =>
        $"{candidate.DayFolderPath} ({candidate.LogicalBatchDate})";

    private static IReadOnlyList<ImportErrorReportRow> BuildErrorRows(
        IReadOnlyCollection<QuantFileCandidate> candidates,
        string errorType,
        string message)
    {
        var occurredAt = DateTimeOffset.Now;
        var missingMfgLotNos = ExtractMissingMfgLotNos(message);
        var rows = new List<ImportErrorReportRow>();

        foreach (var candidate in candidates.OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            var lotNo = TryReadLotNo(candidate.FullPath);
            if (missingMfgLotNos.Count > 0 && !missingMfgLotNos.Contains(lotNo))
            {
                continue;
            }

            var rowMessage = ResolveRowMessage(message, lotNo);
            rows.Add(new ImportErrorReportRow(
                OccurredAt: occurredAt,
                LogicalBatchDate: candidate.LogicalBatchDate,
                Port: candidate.Port,
                TopFolderName: candidate.TopFolderName,
                LotNo: lotNo,
                QuantPath: candidate.FullPath,
                DataFolderPath: candidate.DataFilepath,
                ErrorType: errorType,
                Message: rowMessage,
                SuggestedAction: ResolveSuggestedAction(errorType, rowMessage)));
        }

        return rows;
    }

    private static string ResolveRowMessage(string groupMessage, string lotNo)
    {
        if (!string.IsNullOrWhiteSpace(lotNo) &&
            groupMessage.Contains("ZZ_NF_GAS_MFG_LOT", StringComparison.OrdinalIgnoreCase) &&
            groupMessage.Contains(lotNo, StringComparison.OrdinalIgnoreCase))
        {
            return $"ZZ_NF_GAS_MFG_LOT missing LOT: {lotNo}.";
        }

        return groupMessage;
    }

    private static IReadOnlySet<string> ExtractMissingMfgLotNos(string message)
    {
        if (!message.Contains("ZZ_NF_GAS_MFG_LOT", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (!message.Contains("LOT", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return Regex.Matches(message, @"\b\d{11}\b")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string TryReadLotNo(string quantPath)
    {
        try
        {
            foreach (var line in File.ReadLines(quantPath))
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("Misc", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var markerIndex = trimmed.LastIndexOf('#');
                if (markerIndex < 0 || markerIndex == trimmed.Length - 1)
                {
                    return string.Empty;
                }

                return trimmed[(markerIndex + 1)..].Trim();
            }
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static string ResolveSuggestedAction(string errorType, string message)
    {
        if (message.Contains("ZZ_NF_GAS_MFG_LOT", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("missing LOT", StringComparison.OrdinalIgnoreCase))
        {
            return "Check whether the cylinder LOT exists in MES/ZZ_NF_GAS_MFG_LOT, and verify the LOT in Quant.txt Misc.";
        }

        if (message.Contains("Misc value does not contain a LOT marker", StringComparison.OrdinalIgnoreCase))
        {
            return "Check whether Quant.txt Misc contains #LOT, for example #20260505001.";
        }

        if (message.Contains("sample number", StringComparison.OrdinalIgnoreCase))
        {
            return "Check whether the .D folder name and Quant.txt Data File can be parsed for sample number.";
        }

        if (message.Contains("RF", StringComparison.OrdinalIgnoreCase))
        {
            return "Check whether RF source data has been imported and can match the current import batch time.";
        }

        return "Check source Quant.txt format, folder naming, and related master data settings.";
    }
}
