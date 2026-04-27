using System.Globalization;
using System.Text.RegularExpressions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed partial class GasFolderScanner : IGasFolderScanner
{
    public IReadOnlyList<string> FindStableDayFolders(string watchRoot, TimeSpan stableAge)
    {
        return FindStableBatchContexts(watchRoot, stableAge)
            .Select(batch => batch.DayFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<QuantFileCandidate> FindStableQuantFiles(string watchRoot, TimeSpan stableAge)
    {
        var cutoff = DateTime.Now.Subtract(stableAge);

        return FindStableBatchContexts(watchRoot, stableAge)
            .SelectMany(FindQuantFiles)
            .Where(candidate => IsStable(candidate, cutoff))
            .OrderBy(GetCandidateSortTime)
            .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<QuantFileCandidate> FindQuantFiles(string dayFolderPath)
    {
        if (!Directory.Exists(dayFolderPath))
        {
            return [];
        }

        return ResolveBatchContext(Path.GetFullPath(dayFolderPath)) is { } batch
            ? FindQuantFiles(batch)
            : [];
    }

    internal static string ResolveCandidateBusinessDate(QuantFileCandidate candidate) => candidate.LogicalBatchDate;

    private IReadOnlyList<QuantFileCandidate> FindQuantFiles(BatchContext batch)
    {
        if (!Directory.Exists(batch.SourceRootPath))
        {
            return [];
        }

        var candidates = new List<QuantFileCandidate>();

        foreach (var topFolder in Directory.EnumerateDirectories(batch.SourceRootPath))
        {
            var topFolderName = Path.GetFileName(topFolder);
            if (!TryClassifyTopFolder(topFolderName, out var sourceKind, out var port))
            {
                continue;
            }

            foreach (var quantPath in Directory.EnumerateFiles(topFolder, "Quant.txt", SearchOption.AllDirectories))
            {
                if (IsUnderArchiveSubfolder(topFolder, quantPath))
                {
                    continue;
                }

                var dataFolder = Path.GetDirectoryName(quantPath) ?? topFolder;
                var dataFilename = Path.GetRelativePath(topFolder, quantPath);

                candidates.Add(new QuantFileCandidate(
                    FullPath: Path.GetFullPath(quantPath),
                    DayFolderPath: batch.DayFolderPath,
                    SourceRootPath: batch.SourceRootPath,
                    OutputRootPath: batch.OutputRootPath,
                    LogicalBatchDate: ResolveLogicalBatchDate(batch, dataFolder),
                    IsArchivedInput: batch.IsArchivedInput,
                    TopFolderName: topFolderName,
                    SourceKind: sourceKind,
                    Port: port,
                    DataFilename: dataFilename,
                    DataFilepath: dataFolder));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<BatchContext> FindStableBatchContexts(string watchRoot, TimeSpan stableAge)
    {
        if (!Directory.Exists(watchRoot))
        {
            return [];
        }

        var rootPath = Path.GetFullPath(watchRoot);
        if (ResolveBatchContext(rootPath) is { } rootBatch)
        {
            return [rootBatch];
        }

        var cutoff = DateTime.Now.Subtract(stableAge);

        return Directory.EnumerateDirectories(rootPath)
            .Where(path => DayFolderRegex().IsMatch(Path.GetFileName(path)))
            .Where(path => Directory.GetLastWriteTime(path) <= cutoff)
            .Select(path => ResolveBatchContext(Path.GetFullPath(path)))
            .Where(batch => batch is not null)
            .Cast<BatchContext>()
            .OrderBy(batch => batch.DayFolderPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsStable(QuantFileCandidate candidate, DateTime cutoff)
    {
        if (!File.Exists(candidate.FullPath) || !Directory.Exists(candidate.DataFilepath))
        {
            return false;
        }

        return File.GetLastWriteTime(candidate.FullPath) <= cutoff &&
               Directory.GetLastWriteTime(candidate.DataFilepath) <= cutoff;
    }

    private static BatchContext? ResolveBatchContext(string rootPath)
    {
        var folderName = Path.GetFileName(rootPath);
        var parentPath = Directory.GetParent(rootPath)?.FullName;
        var donePath = Path.Combine(rootPath, "Done");
        var hasDirectSourceFolders = ContainsSourceFolders(rootPath);
        var hasDirectQuantFiles = hasDirectSourceFolders && ContainsQuantFiles(rootPath);
        var hasDoneSourceFolders = Directory.Exists(donePath) && ContainsSourceFolders(donePath);
        var hasDoneQuantFiles = hasDoneSourceFolders && ContainsQuantFiles(donePath);

        if (hasDirectSourceFolders && hasDirectQuantFiles)
        {
            if (IsDoneFolder(folderName) &&
                parentPath is not null &&
                DayFolderRegex().IsMatch(Path.GetFileName(parentPath)))
            {
                return new BatchContext(
                    DayFolderPath: parentPath,
                    SourceRootPath: rootPath,
                    OutputRootPath: Directory.GetParent(parentPath)?.FullName ?? parentPath,
                    LogicalBatchDate: Path.GetFileName(parentPath),
                    IsArchivedInput: true);
            }

            if (DayFolderRegex().IsMatch(folderName))
            {
                return new BatchContext(
                    DayFolderPath: rootPath,
                    SourceRootPath: rootPath,
                    OutputRootPath: parentPath ?? rootPath,
                    LogicalBatchDate: folderName,
                    IsArchivedInput: false);
            }

            return new BatchContext(
                DayFolderPath: rootPath,
                SourceRootPath: rootPath,
                OutputRootPath: rootPath,
                LogicalBatchDate: string.Empty,
                IsArchivedInput: false);
        }

        if (DayFolderRegex().IsMatch(folderName) && hasDoneQuantFiles)
        {
            return new BatchContext(
                DayFolderPath: rootPath,
                SourceRootPath: donePath,
                OutputRootPath: parentPath ?? rootPath,
                LogicalBatchDate: folderName,
                IsArchivedInput: true);
        }

        if (hasDirectSourceFolders)
        {
            return new BatchContext(
                DayFolderPath: rootPath,
                SourceRootPath: rootPath,
                OutputRootPath: parentPath ?? rootPath,
                LogicalBatchDate: DayFolderRegex().IsMatch(folderName) ? folderName : string.Empty,
                IsArchivedInput: false);
        }

        return null;
    }

    private static DateTime GetCandidateSortTime(QuantFileCandidate candidate)
    {
        var folderName = Path.GetFileName(candidate.DataFilepath);
        var match = DataFolderTimeRegex().Match(folderName);

        if (match.Success &&
            DateTime.TryParseExact(
                match.Groups["value"].Value,
                "yyyyMMdd HHmm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        return Directory.GetLastWriteTime(candidate.DataFilepath);
    }

    private static bool TryClassifyTopFolder(string topFolderName, out QuantSourceKind sourceKind, out string port)
    {
        if (topFolderName.Equals("STD", StringComparison.OrdinalIgnoreCase))
        {
            sourceKind = QuantSourceKind.Std;
            port = "STD";
            return true;
        }

        var match = PortFolderRegex().Match(topFolderName);
        if (match.Success)
        {
            sourceKind = QuantSourceKind.Port;
            port = $"PORT {match.Groups["number"].Value}";
            return true;
        }

        sourceKind = default;
        port = string.Empty;
        return false;
    }

    private static bool ContainsSourceFolders(string rootPath) =>
        Directory.EnumerateDirectories(rootPath)
            .Select(Path.GetFileName)
            .Any(name => name is not null && TryClassifyTopFolder(name, out _, out _));

    private static bool ContainsQuantFiles(string rootPath) =>
        Directory.EnumerateDirectories(rootPath)
            .Where(path => TryClassifyTopFolder(Path.GetFileName(path), out _, out _))
            .Any(path => Directory.EnumerateFiles(path, "Quant.txt", SearchOption.AllDirectories)
                .Any(quantPath => !IsUnderArchiveSubfolder(path, quantPath)));

    private static bool IsDoneFolder(string folderName) =>
        folderName.Equals("Done", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderArchiveSubfolder(string topFolder, string quantPath)
    {
        var relativePath = Path.GetRelativePath(topFolder, quantPath);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments
            .Take(Math.Max(0, segments.Length - 1))
            .Any(segment => segment.Equals("archive", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveLogicalBatchDate(BatchContext batch, string dataFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(batch.LogicalBatchDate))
        {
            return batch.LogicalBatchDate;
        }

        var folderName = Path.GetFileName(dataFolderPath);
        var match = DataFolderTimeRegex().Match(folderName);
        if (match.Success &&
            DateTime.TryParseExact(
                match.Groups["value"].Value,
                "yyyyMMdd HHmm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        return Directory.GetLastWriteTime(dataFolderPath).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    private sealed record BatchContext(
        string DayFolderPath,
        string SourceRootPath,
        string OutputRootPath,
        string LogicalBatchDate,
        bool IsArchivedInput);

    [GeneratedRegex(@"^\d{8}$", RegexOptions.Compiled)]
    private static partial Regex DayFolderRegex();

    [GeneratedRegex(@"^P(?:OR|RO)T\s*(?<number>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PortFolderRegex();

    [GeneratedRegex(@"\[(?<value>\d{8}\s\d{4})\]", RegexOptions.Compiled)]
    private static partial Regex DataFolderTimeRegex();
}
