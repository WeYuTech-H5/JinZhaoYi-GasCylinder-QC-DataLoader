using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Processing;

public sealed class Worker(
    ILogger<Worker> logger,
    IJob job,
    IHostApplicationLifetime applicationLifetime,
    IOptions<SchedulerOptions> options) : BackgroundService
{
    private readonly SchedulerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 常駐模式會持續輪詢；RunOnce 模式只跑一輪，方便 dry-run 或排程測試。
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await job.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gas QC 本輪輪詢發生未處理例外。");
            }

            if (_options.RunOnce)
            {
                // BackgroundService 結束不會自動讓 Host 停止，所以 RunOnce 需主動通知 Host 關閉。
                applicationLifetime.StopApplication();
                return;
            }

            // 輪詢間隔最少使用設定的 MinimumIntervalSeconds，避免設定錯誤造成忙迴圈。
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(_options.MinimumIntervalSeconds, _options.IntervalSeconds)), stoppingToken);
        }
    }
}
