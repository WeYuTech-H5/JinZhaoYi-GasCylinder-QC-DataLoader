using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class ProcessedQuantFileStoreTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "ProcessedQuantFileStoreTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FilterUnprocessedAsync_skips_candidates_marked_with_same_fingerprint()
    {
        var statePath = Path.Combine(_rootPath, "processed.json");
        var candidate = CreateCandidate("original");
        var store = CreateStore(statePath);

        await store.MarkProcessedAsync([candidate], CancellationToken.None);

        var unprocessed = await store.FilterUnprocessedAsync([candidate], CancellationToken.None);

        unprocessed.Should().BeEmpty();
    }

    [Fact]
    public async Task FilterUnprocessedAsync_returns_candidate_when_file_content_changes()
    {
        var statePath = Path.Combine(_rootPath, "processed.json");
        var candidate = CreateCandidate("original");
        var store = CreateStore(statePath);

        await store.MarkProcessedAsync([candidate], CancellationToken.None);
        await File.WriteAllTextAsync(candidate.FullPath, "changed");

        var unprocessed = await store.FilterUnprocessedAsync([candidate], CancellationToken.None);

        unprocessed.Should().ContainSingle().Which.Should().Be(candidate);
    }

    [Fact]
    public async Task FilterUnprocessedAsync_skips_candidate_when_only_timestamp_changes()
    {
        var statePath = Path.Combine(_rootPath, "processed.json");
        var candidate = CreateCandidate("same-content");
        var store = CreateStore(statePath);

        await store.MarkProcessedAsync([candidate], CancellationToken.None);
        File.SetLastWriteTimeUtc(candidate.FullPath, DateTime.UtcNow.AddMinutes(5));

        var unprocessed = await store.FilterUnprocessedAsync([candidate], CancellationToken.None);

        unprocessed.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }

    private ProcessedQuantFileStore CreateStore(string statePath) =>
        new(
            Options.Create(new SchedulerOptions
            {
                TargetMode = SchedulerTargetMode.AllNewStableFiles,
                ProcessedStatePath = statePath,
                DryRun = false
            }),
            NullLogger<ProcessedQuantFileStore>.Instance);

    private QuantFileCandidate CreateCandidate(string content)
    {
        var dataFolder = Path.Combine(_rootPath, "2026", "PORT 12", "PORT 12[20260505 1732].D");
        Directory.CreateDirectory(dataFolder);
        var quantPath = Path.Combine(dataFolder, "Quant.txt");
        File.WriteAllText(quantPath, content);

        return new QuantFileCandidate(
            FullPath: quantPath,
            DayFolderPath: Path.Combine(_rootPath, "2026"),
            SourceRootPath: Path.Combine(_rootPath, "2026"),
            OutputRootPath: Path.Combine(_rootPath, "2026"),
            LogicalBatchDate: "20260505",
            IsArchivedInput: false,
            TopFolderName: "PORT 12",
            SourceKind: QuantSourceKind.Port,
            Port: "PORT 12",
            DataFilename: @"PORT 12[20260505 1732].D\Quant.txt",
            DataFilepath: dataFolder);
    }
}
