using System.Globalization;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class QcDownloadFileResolver(IOptions<SchedulerOptions> options) : IQcDownloadFileResolver
{
    private readonly SchedulerOptions _options = options.Value;

    public string? ResolveCylinderQcWorkbook(string batchDate)
    {
        if (!DateTime.TryParseExact(batchDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return null;
        }

        foreach (var qcDirectory in ResolveCandidateQcDirectories(batchDate))
        {
            var path = Path.Combine(qcDirectory, $"Cylinder_Qc[{batchDate}].xlsx");
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return null;
    }

    public string? ResolveCsvBySampleName(string sampleName)
    {
        if (string.IsNullOrWhiteSpace(sampleName) || ContainsInvalidFileNameCharacter(sampleName))
        {
            return null;
        }

        var root = Path.GetFullPath(_options.WatchRoot);
        if (!Directory.Exists(root))
        {
            return null;
        }

        var searchPattern = $"*_{sampleName}_*_pass.csv";
        return Directory
            .EnumerateDirectories(root, "QC", SearchOption.AllDirectories)
            .SelectMany(qcDirectory => Directory.EnumerateFiles(qcDirectory, searchPattern, SearchOption.TopDirectoryOnly))
            .Select(Path.GetFullPath)
            .Where(path => IsUnderRoot(path, root))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool ContainsInvalidFileNameCharacter(string value) =>
        value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
        value.Contains('*') ||
        value.Contains('?');

    private IReadOnlyList<string> ResolveCandidateQcDirectories(string batchDate)
    {
        var root = Path.GetFullPath(_options.WatchRoot);
        var directories = new List<string>();

        if (string.Equals(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), batchDate, StringComparison.OrdinalIgnoreCase))
        {
            directories.Add(Path.Combine(root, "QC"));
        }

        directories.Add(Path.Combine(root, batchDate, "QC"));
        return directories;
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }
}
