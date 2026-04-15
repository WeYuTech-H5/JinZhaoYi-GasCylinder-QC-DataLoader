using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

/// <summary>
/// Gas QC 匯入排程 Job。
/// </summary>
/// <remarks>
/// 每一輪會掃描已穩定的 Quant.txt，依日期資料夾分批匯入。
/// 同一天同一輪的檔案必須整批處理，才能正確判斷 STD/PORT 連續群組，
/// 並以群組最後兩筆計算 AVG、RPD、PPB。
/// </remarks>
public sealed class GasQcImportJob(
    ILogger<GasQcImportJob> logger,
    IGasFolderScanner scanner,
    IImportOrchestrator orchestrator,
    IOptions<SchedulerOptions> options) : Job
{
    private readonly SchedulerOptions _options = options.Value;

    /// <summary>
    /// 執行一輪 Gas QC 匯入。
    /// </summary>
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var cycleStartedAt = DateTimeOffset.Now;
        var cycleStatus = "處理中";
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
            var candidates = scanner.FindStableQuantFiles(_options.WatchRoot, stableAge);
            totalCandidates = candidates.Count;

            logger.LogInformation(
                "開始掃描 Gas QC 資料夾：根目錄={WatchRoot}，穩定待處理 Quant 檔案數={Count}，RunOnce={RunOnce}，DryRun={DryRun}。",
                _options.WatchRoot,
                candidates.Count,
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
                    logger.LogInformation("日期資料夾={DayFolder}，處理訊息={Message}", dayGroup.Key, message);
                }

                if (!result.Succeeded)
                {
                    failedCount++;
                    logger.LogWarning("日期資料夾匯入失敗：{DayFolder}，本輪停止處理後續資料夾。", dayGroup.Key);
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

            cycleStatus = failedCount > 0 ? "失敗" : "成功";
        }
        finally
        {
            var skippedCount = Math.Max(0, totalCandidates - processedCount);
            var elapsedSeconds = (DateTimeOffset.Now - cycleStartedAt).TotalSeconds;

            logger.LogInformation(
                "本輪 Gas QC 處理完成：狀態={Status}，掃描待處理={TotalCandidates}，已處理={ProcessedCount}，成功={SuccessCount}，失敗={FailedCount}，未處理={SkippedCount}，規劃資料列={PlannedRowCount}，搬移成功={MovedCount}，搬移失敗={MoveFailedCount}，DryRun={DryRun}，RunOnce={RunOnce}，耗時秒數={ElapsedSeconds:N2}。",
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

    private bool TryMoveProcessedCandidate(QuantFileCandidate candidate)
    {
        try
        {
            MoveProcessedCandidate(candidate);
            return true;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "已處理資料搬移失敗，檔案會留在原路徑供下輪重試：{Path}", candidate.DataFilepath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "已處理資料搬移失敗，檔案會留在原路徑供下輪重試：{Path}", candidate.DataFilepath);
            return false;
        }
    }

    /// <summary>
    /// 將已處理完成的檔案或 .D 資料夾搬到日期資料夾底下的 Done 區。
    /// </summary>
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
                "已搬移處理完成的資料夾：來源={Source}，目的地={Destination}。",
                candidate.DataFilepath,
                destination);

            return;
        }

        var fileDestination = ResolveUniquePath(
            Path.Combine(destinationRoot, Path.GetFileName(candidate.FullPath)));

        File.Move(candidate.FullPath, fileDestination);

        logger.LogInformation(
            "已搬移處理完成的 Quant 檔案：來源={Source}，目的地={Destination}。",
            candidate.FullPath,
            fileDestination);
    }

    /// <summary>
    /// 取得不衝突的目的地路徑。
    /// </summary>
    private static string ResolveUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");

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
