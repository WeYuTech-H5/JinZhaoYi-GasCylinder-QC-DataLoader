using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class QcDownloadFileResolverTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "QcDownloadFileResolverTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveCylinderQcWorkbook_returns_date_qc_workbook()
    {
        var qcDirectory = Path.Combine(_rootPath, "20260420", "QC");
        Directory.CreateDirectory(qcDirectory);
        var expectedPath = Path.Combine(qcDirectory, "Cylinder_Qc[20260420].xlsx");
        File.WriteAllText(expectedPath, "workbook");
        var resolver = CreateResolver();

        var path = resolver.ResolveCylinderQcWorkbook("20260420");

        path.Should().Be(Path.GetFullPath(expectedPath));
    }

    [Fact]
    public void ResolveCylinderQcWorkbook_supports_watch_root_already_pointing_to_date_folder()
    {
        var dateRoot = Path.Combine(_rootPath, "20260420");
        var qcDirectory = Path.Combine(dateRoot, "QC");
        Directory.CreateDirectory(qcDirectory);
        var expectedPath = Path.Combine(qcDirectory, "Cylinder_Qc[20260420].xlsx");
        File.WriteAllText(expectedPath, "workbook");
        var resolver = new QcDownloadFileResolver(Options.Create(new SchedulerOptions { WatchRoot = dateRoot }));

        var path = resolver.ResolveCylinderQcWorkbook("20260420");

        path.Should().Be(Path.GetFullPath(expectedPath));
    }

    [Fact]
    public void ResolveCylinderQcWorkbook_rejects_invalid_date()
    {
        var resolver = CreateResolver();

        resolver.ResolveCylinderQcWorkbook("2026-04-20").Should().BeNull();
    }

    [Fact]
    public void ResolveCsvBySampleName_returns_latest_matching_csv_under_qc_folder()
    {
        var olderQcDirectory = Path.Combine(_rootPath, "20260420", "QC");
        var newerQcDirectory = Path.Combine(_rootPath, "20260421", "QC");
        Directory.CreateDirectory(olderQcDirectory);
        Directory.CreateDirectory(newerQcDirectory);
        var olderPath = Path.Combine(olderQcDirectory, "2026-04-20_VSMC-009_20260420004_pass.csv");
        var newerPath = Path.Combine(newerQcDirectory, "2026-04-21_VSMC-009_20260420004_pass.csv");
        File.WriteAllText(olderPath, "older");
        File.WriteAllText(newerPath, "newer");
        File.SetLastWriteTimeUtc(olderPath, new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newerPath, new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc));
        var resolver = CreateResolver();

        var path = resolver.ResolveCsvBySampleName("VSMC-009");

        path.Should().Be(Path.GetFullPath(newerPath));
    }

    [Fact]
    public void ResolveCsvBySampleName_rejects_wildcard_input()
    {
        var resolver = CreateResolver();

        resolver.ResolveCsvBySampleName("*").Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }

    private QcDownloadFileResolver CreateResolver() =>
        new(Options.Create(new SchedulerOptions { WatchRoot = _rootPath }));
}
