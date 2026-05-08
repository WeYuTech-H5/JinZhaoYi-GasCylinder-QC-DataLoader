using System.Globalization;
using System.Text;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class PortPpbCsvExporter(
    IOptions<SchedulerOptions> options,
    ILogger<PortPpbCsvExporter> logger) : IPortPpbCsvExporter
{
    private readonly SchedulerOptions _options = options.Value;

    public async Task<IReadOnlyList<string>> ExportAsync(
        IReadOnlyCollection<QcDataRow> portPpbRows,
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (!_options.CsvExport.Enabled || portPpbRows.Count == 0 || candidates.Count == 0)
        {
            return [];
        }

        var firstCandidate = candidates.First();
        var outputDirectory = ResolveOutputDirectory(firstCandidate);
        Directory.CreateDirectory(outputDirectory);

        var outputPaths = new List<string>();
        foreach (var row in portPpbRows.OrderBy(row => row.AnlzTime).ThenBy(row => row.SampleName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = ResolveOutputPath(Path.Combine(outputDirectory, BuildFileName(row, _options.CsvExport.RawLotId)));
            await File.WriteAllTextAsync(outputPath, BuildContent(row), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
            outputPaths.Add(outputPath);
        }

        logger.LogInformation("TO14C PPB CSV exported. Count={Count}, OutputDirectory={OutputDirectory}.", outputPaths.Count, outputDirectory);
        return outputPaths;
    }

    public byte[] ExportToBytes(IReadOnlyCollection<QcDataRow> portPpbRows)
    {
        var content = string.Join(
            Environment.NewLine,
            portPpbRows
                .OrderBy(row => row.AnlzTime)
                .ThenBy(row => row.SampleName, StringComparer.OrdinalIgnoreCase)
                .Select(BuildContent));

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(content);
    }

    internal static string BuildFileName(QcDataRow row, string? rawLotId = null)
    {
        var manufacturingDate = ResolveManufacturingDate(row)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown-date";
        var sampleName = string.IsNullOrWhiteSpace(row.SampleName) ? "unknown-sample" : row.SampleName;
        var lotNo = string.IsNullOrWhiteSpace(rawLotId) ? "unknown-lot" : rawLotId;

        return $"{SanitizeFileName(manufacturingDate)}_{SanitizeFileName(sampleName)}_{SanitizeFileName(lotNo)}_pass.csv";
    }

    internal string BuildContent(QcDataRow row)
    {
        var lines = new List<IReadOnlyList<string?>>
        {
            new string?[] { "SchemaName", _options.CsvExport.SchemaName },
            Array.Empty<string?>(),
            new string?[] { "MaterialNo", _options.CsvExport.MaterialNo },
            new string?[] { "CoACompletionDate", ResolveCoACompletionDate(row) },
            new string?[] { "SupplierID", _options.CsvExport.SupplierID },
            new string?[] { "SupplierName", _options.CsvExport.SupplierName },
            new string?[] { "TSMCFab", _options.CsvExport.TSMCFab },
            new string?[] { "FabPhase", _options.CsvExport.FabPhase },
            new string?[] { "ShipQty", _options.CsvExport.ShipQty },
            new string?[] { "Maker", _options.CsvExport.Maker },
            new string?[] { "ManufacturingDate", FormatDate(ResolveManufacturingDate(row)) },
            new string?[] { "DeliverDate", _options.CsvExport.DeliverDate },
            new string?[] { "PONo", _options.CsvExport.PONo },
            new string?[] { "ContainerID", row.SampleName },
            new string?[] { "CylinderMaterial", _options.CsvExport.CylinderMaterial },
            new string?[] { "ValveMaterial", _options.CsvExport.ValveMaterial },
            new string?[] { "ValveType", _options.CsvExport.ValveType },
            new string?[] { "Content", _options.CsvExport.Content },
            new string?[] { "CylinderSize", _options.CsvExport.CylinderSize },
            new string?[] { "SpecNo", _options.CsvExport.SpecNo },
            new string?[] { "SpecVersion", _options.CsvExport.SpecVersion },
            new string?[] { "ShelfLifeTime", _options.CsvExport.ShelfLifeTime.ToString(CultureInfo.InvariantCulture) },
            new string?[] { "RawLotId", _options.CsvExport.RawLotId },
            new string?[] { "MaterialName", _options.CsvExport.MaterialName },
            new string?[] { "SYMBOLIC", "VALUE" },
            Array.Empty<string?>(),
            new string?[] { "Item", "N", "MEAN", "SD", "MAX", "MIN", "VALUE", "DL" }
        };

        foreach (var item in To14cCsvAnalyteMap.Items)
        {
            lines.Add(new string?[] { item.ReptName, null, null, null, null, null, ResolveItemValue(row, item), null });
        }

        lines.Add(new string?[] { "Water", null, null, null, null, null, _options.CsvExport.WaterValue, null });
        lines.Add(new string?[] { "Oxygen", null, null, null, null, null, _options.CsvExport.OxygenValue, null });
        lines.Add(new string?[] { "Nitrogen", null, null, null, null, null, _options.CsvExport.NitrogenValue, null });
        lines.Add(new string?[] { "SYMBOLIC", "VALUE" });

        return string.Join(Environment.NewLine, lines.Select(FormatCsvLine)) + Environment.NewLine;
    }

    private string? ResolveItemValue(QcDataRow row, To14cCsvAnalyte item)
    {
        if (item.CompoundSuffix is null)
        {
            return ResolveRemainLifeTime(row);
        }

        return FormatAnalyteValue(row.Areas.GetValueOrDefault(item.CompoundSuffix));
    }

    private string? ResolveRemainLifeTime(QcDataRow row)
    {
        if (!row.AnlzTime.HasValue ||
            string.IsNullOrWhiteSpace(_options.CsvExport.DeliverDate) ||
            !DateTime.TryParse(_options.CsvExport.DeliverDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deliverDate))
        {
            return null;
        }

        var expireDate = row.AnlzTime.Value.Date.AddMonths(_options.CsvExport.ShelfLifeTime);
        return Math.Max(0, (expireDate - deliverDate.Date).Days).ToString(CultureInfo.InvariantCulture);
    }

    private string ResolveOutputDirectory(QuantFileCandidate firstCandidate) =>
        string.IsNullOrWhiteSpace(_options.ExportRoot)
            ? Path.Combine(firstCandidate.DayFolderPath, "QC")
            : Path.Combine(_options.ExportRoot, "QC");

    private static string FormatDate(DateTime? value) =>
        value?.ToString("yyyy/M/d", CultureInfo.InvariantCulture) ?? string.Empty;

    private string ResolveCoACompletionDate(QcDataRow row) =>
        !string.IsNullOrWhiteSpace(_options.CsvExport.CoACompletionDate)
            ? _options.CsvExport.CoACompletionDate
            : FormatDate(row.AnlzTime);

    private static DateTime? ResolveManufacturingDate(QcDataRow row)
    {
        if (row.LotNo is { Length: >= 8 } lotNo &&
            DateTime.TryParseExact(
                lotNo[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var manufacturingDate))
        {
            return manufacturingDate;
        }

        return row.AnlzTime;
    }

    private static string? FormatAnalyteValue(decimal? value) =>
        value?.ToString("0", CultureInfo.InvariantCulture);

    private static string FormatCsvLine(IReadOnlyList<string?> fields) =>
        string.Join(",", fields.Select(EscapeCsv));

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        return builder.ToString().Trim();
    }

    private string ResolveOutputPath(string path) =>
        _options.TargetMode == SchedulerTargetMode.AllNewStableFiles
            ? path
            : ResolveUniquePath(path);

    private static string ResolveUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName}_{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
