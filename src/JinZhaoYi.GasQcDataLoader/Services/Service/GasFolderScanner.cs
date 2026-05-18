using System.Globalization;
using System.Text.RegularExpressions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed partial class GasFolderScanner(ILogger<GasFolderScanner>? logger = null) : IGasFolderScanner
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
        if (!DirectoryExists(dayFolderPath, "configured day folder"))
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
        if (!DirectoryExists(batch.SourceRootPath, "Gas QC source root"))
        {
            return [];
        }

        var candidates = new List<QuantFileCandidate>();

        foreach (var topFolder in EnumerateDirectories(batch.SourceRootPath, "Gas QC source root"))
        {
            var topFolderName = Path.GetFileName(topFolder);
            if (!TryClassifyTopFolder(topFolderName, out var sourceKind, out var port))
            {
                continue;
            }

            foreach (var quantPath in EnumerateFiles(topFolder, "Quant.txt", SearchOption.AllDirectories, "Gas QC source folder"))
            {
                if (IsUnderArchiveSubfolder(topFolder, quantPath))
                {
                    continue;
                }

                var dataFolder = Path.GetDirectoryName(quantPath) ?? topFolder;
                if (!IsFormalDataFolder(dataFolder))
                {
                    logger?.LogInformation(
                        "Skipping Quant file because .D folder does not contain an underscore suffix. QuantPath={QuantPath}, DataFolder={DataFolder}.",
                        quantPath,
                        dataFolder);
                    continue;
                }

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

        if (candidates.Count == 0)
        {
            logger?.LogWarning(
                "No formal Quant.txt candidates were found under source root. SourceRootPath={SourceRootPath}. Expected Quant.txt under STD or PORT folders, inside .D folders with an underscore suffix such as PORT 11[yyyyMMdd HHmm]_001.D.",
                batch.SourceRootPath);
        }

        return candidates
            .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<BatchContext> FindStableBatchContexts(string watchRoot, TimeSpan stableAge)
    {
        if (!DirectoryExists(watchRoot, "Scheduler:WatchRoot"))
        {
            logger?.LogWarning(
                "Gas QC watch root is not accessible. WatchRoot={WatchRoot}. If this is a UNC path, verify the IIS app pool or Windows service identity has share and NTFS permissions.",
                watchRoot);
            return [];
        }

        var rootPath = Path.GetFullPath(watchRoot);
        if (ResolveBatchContext(rootPath) is { } rootBatch)
        {
            return [rootBatch];
        }

        var cutoff = DateTime.Now.Subtract(stableAge);

        var batches = EnumerateDirectories(rootPath, "Scheduler:WatchRoot")
            .Where(path => DayFolderRegex().IsMatch(Path.GetFileName(path)))
            .Where(path => IsDirectoryStable(path, cutoff))
            .Select(path => ResolveBatchContext(Path.GetFullPath(path)))
            .Where(batch => batch is not null)
            .Cast<BatchContext>()
            .OrderBy(batch => batch.DayFolderPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (batches.Length == 0)
        {
            logger?.LogWarning(
                "No Gas QC batch contexts were found under WatchRoot={WatchRoot}. Expected either source folders like STD/PORT 1 directly under the root, or yyyyMMdd day folders containing STD/PORT folders with Quant.txt files.",
                rootPath);
        }

        return batches;
    }

    private bool IsStable(QuantFileCandidate candidate, DateTime cutoff)
    {
        if (!File.Exists(candidate.FullPath))
        {
            logger?.LogWarning("Skipping Quant candidate because the file is not accessible. QuantPath={QuantPath}.", candidate.FullPath);
            return false;
        }

        if (!DirectoryExists(candidate.DataFilepath, "Quant data folder"))
        {
            return false;
        }

        try
        {
            return File.GetLastWriteTime(candidate.FullPath) <= cutoff &&
                   Directory.GetLastWriteTime(candidate.DataFilepath) <= cutoff;
        }
        catch (Exception ex) when (IsFileSystemAccessException(ex))
        {
            logger?.LogWarning(
                ex,
                "Skipping Quant candidate because last-write time could not be read. QuantPath={QuantPath}, DataFolder={DataFolder}.",
                candidate.FullPath,
                candidate.DataFilepath);
            return false;
        }
    }

    private BatchContext? ResolveBatchContext(string rootPath)
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

    private bool ContainsSourceFolders(string rootPath) =>
        EnumerateDirectories(rootPath, "Gas QC root")
            .Select(Path.GetFileName)
            .Any(name => name is not null && TryClassifyTopFolder(name, out _, out _));

    private bool ContainsQuantFiles(string rootPath) =>
        EnumerateDirectories(rootPath, "Gas QC root")
            .Where(path => TryClassifyTopFolder(Path.GetFileName(path), out _, out _))
            .Any(path => EnumerateFiles(path, "Quant.txt", SearchOption.AllDirectories, "Gas QC source folder")
                .Any(quantPath =>
                    !IsUnderArchiveSubfolder(path, quantPath) &&
                    IsFormalDataFolder(Path.GetDirectoryName(quantPath) ?? path)));

    private static bool IsFormalDataFolder(string dataFolderPath)
    {
        var folderName = Path.GetFileName(dataFolderPath);
        return !string.IsNullOrWhiteSpace(folderName) && FormalDataFolderRegex().IsMatch(folderName);
    }

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

    private bool DirectoryExists(string path, string description)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return true;
            }

            logger?.LogWarning(
                "Directory is not accessible or does not exist. Description={Description}, Path={Path}. If this is a UNC path, verify the process identity has share and NTFS permissions.",
                description,
                path);
            return false;
        }
        catch (Exception ex) when (IsFileSystemAccessException(ex))
        {
            logger?.LogWarning(
                ex,
                "Directory access check failed. Description={Description}, Path={Path}. If this is a UNC path, verify the process identity has share and NTFS permissions.",
                description,
                path);
            return false;
        }
    }

    private IReadOnlyList<string> EnumerateDirectories(string path, string description)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToArray();
        }
        catch (Exception ex) when (IsFileSystemAccessException(ex))
        {
            logger?.LogWarning(
                ex,
                "Failed to enumerate directories. Description={Description}, Path={Path}. If this is a UNC path, verify the process identity has share and NTFS permissions.",
                description,
                path);
            return [];
        }
    }

    private IReadOnlyList<string> EnumerateFiles(
        string path,
        string searchPattern,
        SearchOption searchOption,
        string description)
    {
        try
        {
            return Directory.EnumerateFiles(path, searchPattern, searchOption).ToArray();
        }
        catch (Exception ex) when (IsFileSystemAccessException(ex))
        {
            logger?.LogWarning(
                ex,
                "Failed to enumerate files. Description={Description}, Path={Path}, SearchPattern={SearchPattern}, SearchOption={SearchOption}. If this is a UNC path, verify the process identity has share and NTFS permissions.",
                description,
                path,
                searchPattern,
                searchOption);
            return [];
        }
    }

    private bool IsDirectoryStable(string path, DateTime cutoff)
    {
        try
        {
            return Directory.GetLastWriteTime(path) <= cutoff;
        }
        catch (Exception ex) when (IsFileSystemAccessException(ex))
        {
            logger?.LogWarning(ex, "Skipping directory because last-write time could not be read. Path={Path}.", path);
            return false;
        }
    }

    private static bool IsFileSystemAccessException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException;

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

    [GeneratedRegex(@"_.+\.D$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FormalDataFolderRegex();
}
