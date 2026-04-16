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
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_options.RunOnce && _options.UseDailySchedule)
            {
                var delay = CalculateDelayUntilNextDailyRun(DateTimeOffset.Now, _options.DailyWakeUpTime);
                logger.LogInformation(
                    "Gas QC worker is waiting for daily wake-up time {DailyWakeUpTime}. DelaySeconds={DelaySeconds:N0}.",
                    _options.DailyWakeUpTime,
                    delay.TotalSeconds);

                await Task.Delay(delay, stoppingToken);
            }

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
                logger.LogError(ex, "Gas QC import cycle failed.");
            }

            if (_options.RunOnce)
            {
                applicationLifetime.StopApplication();
                return;
            }

            if (_options.UseDailySchedule)
            {
                continue;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(_options.MinimumIntervalSeconds, _options.IntervalSeconds)),
                stoppingToken);
        }
    }

    internal static TimeSpan CalculateDelayUntilNextDailyRun(DateTimeOffset now, string dailyWakeUpTime)
    {
        var wakeUpTime = ParseDailyWakeUpTime(dailyWakeUpTime);
        var nextRun = new DateTimeOffset(now.Date.Add(wakeUpTime), now.Offset);

        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun - now;
    }

    private static TimeSpan ParseDailyWakeUpTime(string dailyWakeUpTime)
    {
        if (TimeSpan.TryParseExact(dailyWakeUpTime, @"hh\:mm", null, out var parsed) ||
            TimeSpan.TryParseExact(dailyWakeUpTime, @"h\:mm", null, out parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Scheduler:DailyWakeUpTime is invalid: '{dailyWakeUpTime}'. Use HH:mm, for example 02:00.");
    }
}
