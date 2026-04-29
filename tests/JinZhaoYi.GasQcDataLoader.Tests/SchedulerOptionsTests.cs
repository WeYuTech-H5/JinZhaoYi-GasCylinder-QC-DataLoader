using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Processing;
using JinZhaoYi.GasQcDataLoader.Services.Service;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class SchedulerOptionsTests
{
    [Fact]
    public void UseAverageSnapshotTables_defaults_to_true()
    {
        var options = new SchedulerOptions();

        options.UseAverageSnapshotTables.Should().BeTrue();
    }

    [Fact]
    public void DoneFolderName_defaults_to_archive()
    {
        var options = new SchedulerOptions();

        options.DoneFolderName.Should().Be("archive");
    }

    [Fact]
    public void StdQc_table_defaults_to_expected_name()
    {
        var options = new SchedulerOptions();

        options.Tables.StdQc.Should().Be("ZZ_NF_GAS_QC_LOT_STD_QC");
    }

    [Fact]
    public void Daily_schedule_options_default_to_yesterday_mode()
    {
        var options = new SchedulerOptions();

        options.UseDailySchedule.Should().BeFalse();
        options.DailyWakeUpTime.Should().Be("02:00");
        options.NormalTargetDayOffset.Should().Be(-1);
        options.BackfillEnabled.Should().BeFalse();
        options.BackfillTargetDate.Should().BeNull();
    }

    [Fact]
    public void CalculateDelayUntilNextDailyRun_returns_same_day_delay_before_wake_time()
    {
        var now = new DateTimeOffset(2026, 4, 16, 1, 30, 0, TimeSpan.FromHours(8));

        var delay = Worker.CalculateDelayUntilNextDailyRun(now, "02:00");

        delay.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void CalculateDelayUntilNextDailyRun_returns_next_day_delay_at_wake_time()
    {
        var now = new DateTimeOffset(2026, 4, 16, 2, 0, 0, TimeSpan.FromHours(8));

        var delay = Worker.CalculateDelayUntilNextDailyRun(now, "02:00");

        delay.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void ResolveTargetDayFolderName_uses_normal_offset_when_backfill_is_disabled()
    {
        var options = new SchedulerOptions
        {
            NormalTargetDayOffset = -1,
            BackfillEnabled = false
        };

        var target = GasQcImportJob.ResolveTargetDayFolderName(new DateTime(2026, 4, 16), options);

        target.Should().Be("20260415");
    }

    [Fact]
    public void ResolveTargetDayFolderName_uses_backfill_date_when_backfill_is_enabled()
    {
        var options = new SchedulerOptions
        {
            BackfillEnabled = true,
            BackfillTargetDate = "20251119"
        };

        var target = GasQcImportJob.ResolveTargetDayFolderName(new DateTime(2026, 4, 16), options);

        target.Should().Be("20251119");
    }

    [Fact]
    public void ResolveCandidateBusinessDate_prefers_day_folder_name()
    {
        var candidate = new QuantFileCandidate(
            FullPath: @"C:\data\20260422\PORT2\PORT 2[20260422 1010]_001.D\Quant.txt",
            DayFolderPath: @"C:\data\20260422",
            SourceRootPath: @"C:\data\20260422\PORT2",
            OutputRootPath: @"C:\data",
            LogicalBatchDate: "20260422",
            IsArchivedInput: false,
            TopFolderName: "PORT2",
            SourceKind: QuantSourceKind.Port,
            Port: "PORT 2",
            DataFilename: @"PORT 2[20260422 1010]_001.D\Quant.txt",
            DataFilepath: @"C:\data\20260422\PORT2\PORT 2[20260422 1010]_001.D");

        var businessDate = GasFolderScanner.ResolveCandidateBusinessDate(candidate);

        businessDate.Should().Be("20260422");
    }

    [Fact]
    public void ResolveCandidateBusinessDate_uses_data_folder_timestamp_for_root_layout()
    {
        var candidate = new QuantFileCandidate(
            FullPath: @"C:\data\QC01\PORT2\PORT 2[20260422 1010]_001.D\Quant.txt",
            DayFolderPath: @"C:\data\QC01",
            SourceRootPath: @"C:\data\QC01\PORT2",
            OutputRootPath: @"C:\data\QC01",
            LogicalBatchDate: "20260422",
            IsArchivedInput: false,
            TopFolderName: "PORT2",
            SourceKind: QuantSourceKind.Port,
            Port: "PORT 2",
            DataFilename: @"PORT 2[20260422 1010]_001.D\Quant.txt",
            DataFilepath: @"C:\data\QC01\PORT2\PORT 2[20260422 1010]_001.D");

        var businessDate = GasFolderScanner.ResolveCandidateBusinessDate(candidate);

        businessDate.Should().Be("20260422");
    }
}
