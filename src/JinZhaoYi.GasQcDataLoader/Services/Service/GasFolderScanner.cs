using System.Globalization;
using System.Text.RegularExpressions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed partial class GasFolderScanner : IGasFolderScanner
{
    public IReadOnlyList<string> FindStableDayFolders(string watchRoot, TimeSpan stableAge)
    {
        if (!Directory.Exists(watchRoot))
        {
            return [];
        }

        var rootPath = Path.GetFullPath(watchRoot);
        if (ContainsSourceFolders(rootPath))
        {
            return [rootPath];
        }

        var cutoff = DateTime.Now.Subtract(stableAge);

        return Directory.EnumerateDirectories(rootPath)
            .Where(path => DayFolderRegex().IsMatch(Path.GetFileName(path)))
            .Where(path => Directory.GetLastWriteTime(path) <= cutoff)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<QuantFileCandidate> FindStableQuantFiles(string watchRoot, TimeSpan stableAge)
    {
        var cutoff = DateTime.Now.Subtract(stableAge);

        return FindStableDayFolders(watchRoot, TimeSpan.Zero)
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

        var dayRoot = Path.GetFullPath(dayFolderPath);
        var candidates = new List<QuantFileCandidate>();

        foreach (var topFolder in Directory.EnumerateDirectories(dayRoot))
        {
            var topFolderName = Path.GetFileName(topFolder);
            if (!TryClassifyTopFolder(topFolderName, out var sourceKind, out var port))
            {
                continue;
            }

            foreach (var quantPath in Directory.EnumerateFiles(topFolder, "Quant.txt", SearchOption.AllDirectories))
            {
                var dataFolder = Path.GetDirectoryName(quantPath) ?? topFolder;
                var dataFilename = Path.GetRelativePath(topFolder, quantPath);

                candidates.Add(new QuantFileCandidate(
                    FullPath: Path.GetFullPath(quantPath),
                    DayFolderPath: dayRoot,
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

    internal static string ResolveCandidateBusinessDate(QuantFileCandidate candidate)
    {
        var dayFolderName = Path.GetFileName(candidate.DayFolderPath);
        if (DayFolderRegex().IsMatch(dayFolderName))
        {
            return dayFolderName;
        }

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
            return parsed.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        return Directory.GetLastWriteTime(candidate.DataFilepath).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
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

    [GeneratedRegex(@"^\d{8}$", RegexOptions.Compiled)]
    private static partial Regex DayFolderRegex();

    [GeneratedRegex(@"^P(?:OR|RO)T\s*(?<number>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PortFolderRegex();

    [GeneratedRegex(@"\[(?<value>\d{8}\s\d{4})\]", RegexOptions.Compiled)]
    private static partial Regex DataFolderTimeRegex();
}
