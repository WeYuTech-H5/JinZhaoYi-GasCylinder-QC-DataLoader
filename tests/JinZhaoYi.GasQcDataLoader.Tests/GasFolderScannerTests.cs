using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Services.Service;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class GasFolderScannerTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "GasFolderScannerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void FindStableDayFolders_returns_watch_root_when_root_contains_source_folders()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "STD"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "PORT2"));

        var scanner = new GasFolderScanner();

        var folders = scanner.FindStableDayFolders(_rootPath, TimeSpan.FromMinutes(5));

        folders.Should().ContainSingle().Which.Should().Be(Path.GetFullPath(_rootPath));
    }

    [Fact]
    public void FindStableQuantFiles_supports_root_layout_without_day_folder()
    {
        var dataFolder = Path.Combine(_rootPath, "PORT2", "PORT 2[20260422 1010]_001.D");
        Directory.CreateDirectory(dataFolder);

        var quantPath = Path.Combine(dataFolder, "Quant.txt");
        File.WriteAllText(quantPath, "test");

        var stableTime = DateTime.Now.AddMinutes(-10);
        File.SetLastWriteTime(quantPath, stableTime);
        Directory.SetLastWriteTime(dataFolder, stableTime);

        var scanner = new GasFolderScanner();

        var candidates = scanner.FindStableQuantFiles(_rootPath, TimeSpan.FromMinutes(1));

        candidates.Should().ContainSingle();
        candidates[0].DayFolderPath.Should().Be(Path.GetFullPath(_rootPath));
        candidates[0].SourceRootPath.Should().Be(Path.GetFullPath(_rootPath));
        candidates[0].OutputRootPath.Should().Be(Path.GetFullPath(_rootPath));
        candidates[0].LogicalBatchDate.Should().Be("20260422");
        candidates[0].TopFolderName.Should().Be("PORT2");
        candidates[0].Port.Should().Be("PORT 2");
        candidates[0].IsArchivedInput.Should().BeFalse();
    }

    [Fact]
    public void FindStableQuantFiles_ignores_archive_subfolder_under_source_folder()
    {
        var liveDataFolder = Path.Combine(_rootPath, "PORT5", "PORT 5[20260422 1010]_001.D");
        var archivedDataFolder = Path.Combine(_rootPath, "PORT5", "archive", "PORT 5[20260421 1010]_001.D");
        Directory.CreateDirectory(liveDataFolder);
        Directory.CreateDirectory(archivedDataFolder);

        var liveQuantPath = Path.Combine(liveDataFolder, "Quant.txt");
        var archivedQuantPath = Path.Combine(archivedDataFolder, "Quant.txt");
        File.WriteAllText(liveQuantPath, "live");
        File.WriteAllText(archivedQuantPath, "archived");

        var stableTime = DateTime.Now.AddMinutes(-10);
        File.SetLastWriteTime(liveQuantPath, stableTime);
        File.SetLastWriteTime(archivedQuantPath, stableTime);
        Directory.SetLastWriteTime(liveDataFolder, stableTime);
        Directory.SetLastWriteTime(archivedDataFolder, stableTime);

        var scanner = new GasFolderScanner();

        var candidates = scanner.FindStableQuantFiles(_rootPath, TimeSpan.FromMinutes(1));

        candidates.Should().ContainSingle();
        candidates[0].DataFilepath.Should().Be(Path.GetFullPath(liveDataFolder));
    }

    [Fact]
    public void FindStableQuantFiles_supports_dated_folder_done_layout()
    {
        var batchFolder = Path.Combine(_rootPath, "20251119");
        var dataFolder = Path.Combine(batchFolder, "Done", "STD", "STD[20251120 0049]_903.D");
        Directory.CreateDirectory(dataFolder);

        var quantPath = Path.Combine(dataFolder, "Quant.txt");
        File.WriteAllText(quantPath, "test");

        var stableTime = DateTime.Now.AddMinutes(-10);
        File.SetLastWriteTime(quantPath, stableTime);
        Directory.SetLastWriteTime(dataFolder, stableTime);
        Directory.SetLastWriteTime(batchFolder, stableTime);

        var scanner = new GasFolderScanner();

        var candidates = scanner.FindStableQuantFiles(_rootPath, TimeSpan.FromMinutes(1));

        candidates.Should().ContainSingle();
        candidates[0].DayFolderPath.Should().Be(Path.GetFullPath(batchFolder));
        candidates[0].LogicalBatchDate.Should().Be("20251119");
        candidates[0].IsArchivedInput.Should().BeTrue();
        candidates[0].OutputRootPath.Should().Be(Path.GetFullPath(_rootPath));
    }

    [Fact]
    public void FindQuantFiles_supports_done_root_replay_mode()
    {
        var dayFolder = Path.Combine(_rootPath, "20251119");
        var doneRoot = Path.Combine(dayFolder, "Done");
        var dataFolder = Path.Combine(doneRoot, "PORT 2", "PORT 2[20251120 0049]_V023.D");
        Directory.CreateDirectory(dataFolder);
        File.WriteAllText(Path.Combine(dataFolder, "Quant.txt"), "test");

        var scanner = new GasFolderScanner();

        var candidates = scanner.FindQuantFiles(doneRoot);

        candidates.Should().ContainSingle();
        candidates[0].DayFolderPath.Should().Be(Path.GetFullPath(dayFolder));
        candidates[0].LogicalBatchDate.Should().Be("20251119");
        candidates[0].IsArchivedInput.Should().BeTrue();
        candidates[0].OutputRootPath.Should().Be(Path.GetFullPath(_rootPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}
