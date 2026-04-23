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
        candidates[0].TopFolderName.Should().Be("PORT2");
        candidates[0].Port.Should().Be("PORT 2");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}
