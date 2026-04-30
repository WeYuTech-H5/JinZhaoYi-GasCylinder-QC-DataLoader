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
    private readonly SchedulerCsvExportOptions _options = options.Value.CsvExport;

    public async Task<IReadOnlyList<string>> ExportAsync(
        IReadOnlyCollection<QcDataRow> portPpbRows,
        IReadOnlyCollection<QuantFileCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || portPpbRows.Count == 0 || candidates.Count == 0)
        {
            return [];
        }

        var firstCandidate = candidates.First();
        var outputDirectory = Path.Combine(firstCandidate.DayFolderPath, "QC");
        Directory.CreateDirectory(outputDirectory);

        var outputPaths = new List<string>();
        foreach (var row in portPpbRows.OrderBy(row => row.AnlzTime).ThenBy(row => row.SampleName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = ResolveUniquePath(Path.Combine(outputDirectory, BuildFileName(row)));
            await File.WriteAllTextAsync(outputPath, BuildContent(row), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
            outputPaths.Add(outputPath);
        }

        logger.LogInformation("TO14C PPB CSV exported. Count={Count}, OutputDirectory={OutputDirectory}.", outputPaths.Count, outputDirectory);
        return outputPaths;
    }

    internal static string BuildFileName(QcDataRow row)
    {
        var manufacturingDate = row.AnlzTime?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown-date";
        var sampleName = string.IsNullOrWhiteSpace(row.SampleName) ? "unknown-sample" : row.SampleName;
        var lotNo = string.IsNullOrWhiteSpace(row.LotNo) ? "unknown-lot" : row.LotNo;

        return $"{SanitizeFileName(manufacturingDate)}_{SanitizeFileName(sampleName)}_{SanitizeFileName(lotNo)}_pass.csv";
    }

    internal string BuildContent(QcDataRow row)
    {
        var lines = new List<IReadOnlyList<string?>>
        {
            new string?[] { "SchemaName", _options.SchemaName },
            Array.Empty<string?>(),
            new string?[] { "MaterialNo", _options.MaterialNo },
            new string?[] { "CoACompletionDate", _options.CoACompletionDate },
            new string?[] { "SupplierID", _options.SupplierID },
            new string?[] { "SupplierName", _options.SupplierName },
            new string?[] { "TSMCFab", _options.TSMCFab },
            new string?[] { "FabPhase", _options.FabPhase },
            new string?[] { "ShipQty", _options.ShipQty },
            new string?[] { "Maker", _options.Maker },
            new string?[] { "ManufacturingDate", FormatDate(row.AnlzTime) },
            new string?[] { "DeliverDate", _options.DeliverDate },
            new string?[] { "PONo", _options.PONo },
            new string?[] { "ContainerID", row.SampleName },
            new string?[] { "CylinderMaterial", _options.CylinderMaterial },
            new string?[] { "ValveMaterial", _options.ValveMaterial },
            new string?[] { "ValveType", _options.ValveType },
            new string?[] { "Content", _options.Content },
            new string?[] { "CylinderSize", _options.CylinderSize },
            new string?[] { "SpecNo", _options.SpecNo },
            new string?[] { "SpecVersion", _options.SpecVersion },
            new string?[] { "ShelfLifeTime", _options.ShelfLifeTime.ToString(CultureInfo.InvariantCulture) },
            new string?[] { "RawLotId", _options.RawLotId },
            new string?[] { "MaterialName", _options.MaterialName },
            new string?[] { "SYMBOLIC", "VALUE" },
            Array.Empty<string?>(),
            new string?[] { "Item", "N", "MEAN", "SD", "MAX", "MIN", "VALUE", "DL" }
        };

        foreach (var item in To14cCsvAnalyteMap.Items)
        {
            lines.Add(new string?[] { item.ReptName, null, null, null, null, null, ResolveItemValue(row, item), null });
        }

        lines.Add(new string?[] { "Water", null, null, null, null, null, _options.WaterValue, null });
        lines.Add(new string?[] { "Oxygen", null, null, null, null, null, _options.OxygenValue, null });
        lines.Add(new string?[] { "Nitrogen", null, null, null, null, null, _options.NitrogenValue, null });
        lines.Add(new string?[] { "SYMBOLIC", "VALUE" });

        return string.Join(Environment.NewLine, lines.Select(FormatCsvLine)) + Environment.NewLine;
    }

    private string? ResolveItemValue(QcDataRow row, To14cCsvAnalyte item)
    {
        if (item.CompoundSuffix is null)
        {
            return ResolveRemainLifeTime(row);
        }

        return row.Areas.GetValueOrDefault(item.CompoundSuffix)?.ToString(CultureInfo.InvariantCulture);
    }

    private string? ResolveRemainLifeTime(QcDataRow row)
    {
        if (!row.AnlzTime.HasValue ||
            string.IsNullOrWhiteSpace(_options.DeliverDate) ||
            !DateTime.TryParse(_options.DeliverDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deliverDate))
        {
            return null;
        }

        var expireDate = row.AnlzTime.Value.Date.AddMonths(_options.ShelfLifeTime);
        return Math.Max(0, (expireDate - deliverDate.Date).Days).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTime? value) =>
        value?.ToString("yyyy/M/d", CultureInfo.InvariantCulture) ?? string.Empty;

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
