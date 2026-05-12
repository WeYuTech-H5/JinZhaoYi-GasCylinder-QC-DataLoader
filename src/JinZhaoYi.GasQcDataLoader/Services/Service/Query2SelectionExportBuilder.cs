using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using System.Globalization;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class Query2SelectionExportBuilder(
    ICalculationService calculationService,
    ILogger<Query2SelectionExportBuilder> logger) : IQuery2SelectionExportBuilder
{
    public IReadOnlyList<Query2ExportRow> BuildRows(
        QcDataRow rf,
        IReadOnlyCollection<QcDataRow> stdRawRows,
        IReadOnlyCollection<QcDataRow> portRawRows)
    {
        var displayStdRawRows = BuildDisplayRawRows(stdRawRows);
        var displayPortRawRows = BuildDisplayRawRows(portRawRows);
        var exportRows = new List<Query2ExportRow>
        {
            new(Query2ExportRowType.Rf, rf.DeepClone())
        };

        var segments = BuildTimeSegments(displayStdRawRows, displayPortRawRows);
        var stdAverages = BuildStdAverages(segments);
        QcDataRow? previousStdAverage = null;

        foreach (var segment in segments)
        {
            var orderedRows = segment.Rows;

            if (segment.IsStd)
            {
                exportRows.AddRange(orderedRows.Select(row => new Query2ExportRow(Query2ExportRowType.Raw, row.DeepClone())));

                if (orderedRows.Count < 2)
                {
                    logger.LogInformation("Skipping STD calculation because selected segment has fewer than two rows. LotNo={LotNo}, Port={Port}.", orderedRows[0].LotNo, orderedRows[0].Port);
                    continue;
                }

                var (stdFirst, stdSecond) = LastTwo(orderedRows);
                var stdAverage = calculationService.CreateAverageRow($"AVG({stdFirst.Id}:{stdSecond.Id})", stdFirst, stdSecond);
                var stdRpd = calculationService.CreateRpdRow($"RPD({stdFirst.Id}:{stdSecond.Id})", stdFirst, stdSecond);

                exportRows.Add(new Query2ExportRow(Query2ExportRowType.Avg, stdAverage));

                if (previousStdAverage is not null)
                {
                    var qc = calculationService.CreateStdQcRow($"QC({previousStdAverage.Id},{stdAverage.Id})", previousStdAverage, stdAverage);
                    exportRows.Add(new Query2ExportRow(Query2ExportRowType.Qc, qc));
                }

                exportRows.Add(new Query2ExportRow(Query2ExportRowType.Rpd, stdRpd));
                previousStdAverage = stdAverage;
                continue;
            }

            var rawExportRows = orderedRows
                .Select(row => BuildPortRawExportRow(row, rf, stdAverages))
                .ToArray();
            exportRows.AddRange(rawExportRows.Select(row => new Query2ExportRow(Query2ExportRowType.Raw, row)));

            if (orderedRows.Count < 2)
            {
                logger.LogInformation("Skipping PORT calculation because selected segment has fewer than two rows. LotNo={LotNo}, Port={Port}.", orderedRows[0].LotNo, orderedRows[0].Port);
                continue;
            }

            var (first, second) = LastTwo(orderedRows);
            var average = calculationService.CreateAverageRow($"AVG({first.Id}:{second.Id})", first, second);
            var rpd = calculationService.CreateRpdRow($"RPD({first.Id}:{second.Id})", first, second);

            exportRows.Add(new Query2ExportRow(Query2ExportRowType.Avg, average));

            var portStdAverage = ResolveStdAverageForPort(stdAverages, average);
            if (portStdAverage is not null)
            {
                var ppb = calculationService.CreatePortPpbRow($"ppb({average.Si0Id})", average, rf, portStdAverage);
                exportRows.Add(new Query2ExportRow(Query2ExportRowType.Ppb, ppb));
            }
            else
            {
                logger.LogInformation("Skipping PORT PPB because no selected STD average is available. LotNo={LotNo}, Port={Port}.", orderedRows[0].LotNo, orderedRows[0].Port);
            }

            exportRows.Add(new Query2ExportRow(Query2ExportRowType.Rpd, rpd));
        }

        return exportRows;
    }

    private QcDataRow BuildPortRawExportRow(
        QcDataRow rawRow,
        QcDataRow rf,
        IReadOnlyList<QcDataRow> stdAverages)
    {
        var exportRow = rawRow.DeepClone();
        var activeStdAverage = ResolveStdAverageForPort(stdAverages, exportRow);
        if (activeStdAverage is null)
        {
            logger.LogInformation("Skipping PORT raw ppb because no selected STD average is available. LotNo={LotNo}, Port={Port}, Id={Id}.", rawRow.LotNo, rawRow.Port, rawRow.Id);
            return exportRow;
        }

        calculationService.ApplyPortRawPpb(exportRow, rf, activeStdAverage);
        return exportRow;
    }

    private static QcDataRow? ResolveStdAverageForPort(IReadOnlyList<QcDataRow> stdAverages, QcDataRow portAverage)
    {
        if (stdAverages.Count == 0)
        {
            return null;
        }

        if (!portAverage.AnlzTime.HasValue)
        {
            return stdAverages[^1];
        }

        return stdAverages
            .Where(row => row.AnlzTime <= portAverage.AnlzTime)
            .LastOrDefault() ?? stdAverages[0];
    }

    private static IReadOnlyList<TimeSegment> BuildTimeSegments(
        IEnumerable<QcDataRow> stdRawRows,
        IEnumerable<QcDataRow> portRawRows)
    {
        var segmentBuilders = new List<TimeSegmentBuilder>();
        TimeSegmentBuilder? current = null;

        foreach (var row in OrderRows(stdRawRows.Concat(portRawRows)))
        {
            var key = SegmentKey.From(row);
            if (current is null || !current.Key.Equals(key))
            {
                current = new TimeSegmentBuilder(key);
                segmentBuilders.Add(current);
            }

            current.Rows.Add(row);
        }

        return segmentBuilders
            .Select(segment => new TimeSegment(segment.Key.IsStd, segment.Rows.ToArray()))
            .ToArray();
    }

    private IReadOnlyList<QcDataRow> BuildStdAverages(IEnumerable<TimeSegment> segments)
    {
        var averages = new List<QcDataRow>();

        foreach (var segment in segments.Where(segment => segment.IsStd && segment.Rows.Count >= 2))
        {
            var (first, second) = LastTwo(segment.Rows);
            averages.Add(calculationService.CreateAverageRow($"AVG({first.Id}:{second.Id})", first, second));
        }

        return averages
            .OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.SampleNo)
            .ThenBy(row => row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<QcDataRow> OrderRows(IEnumerable<QcDataRow> rows) =>
        rows.OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.SampleNo)
            .ThenBy(row => row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DataFilename, StringComparer.OrdinalIgnoreCase);

    private static (QcDataRow First, QcDataRow Second) LastTwo(IReadOnlyList<QcDataRow> rows) =>
        (rows[^2], rows[^1]);

    private static IReadOnlyList<QcDataRow> BuildDisplayRawRows(IEnumerable<QcDataRow> rows)
    {
        var clones = OrderRows(rows)
            .Select(row => row.DeepClone())
            .ToArray();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in clones)
        {
            var id = BuildDisplayRawId(row);
            counts.TryGetValue(id, out var count);
            counts[id] = ++count;
            row.Id = count == 1
                ? id
                : $"{id}#{count.ToString(CultureInfo.InvariantCulture)}";
        }

        return clones;
    }

    private static string BuildDisplayRawId(QcDataRow row)
    {
        var prefix = BuildDisplayPortPrefix(row);
        var sampleNo = row.SampleNo?.ToString("000", CultureInfo.InvariantCulture) ?? "000";
        var time = row.AnlzTime?.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) ?? "NO_TIME";
        return $"{prefix}{sampleNo}@{time}";
    }

    private static string BuildDisplayPortPrefix(QcDataRow row)
    {
        var sourceKind = Normalize(row.SourceKind);
        var port = Normalize(row.Port);
        if (sourceKind == "STD" || port == "STD")
        {
            return "STD";
        }

        var digits = new string(port.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var portNo))
        {
            return $"P{portNo:00}-";
        }

        return string.IsNullOrWhiteSpace(port)
            ? "PORT-"
            : port.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase) + "-";
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private sealed record TimeSegment(bool IsStd, IReadOnlyList<QcDataRow> Rows);

    private sealed class TimeSegmentBuilder(SegmentKey key)
    {
        public SegmentKey Key { get; } = key;

        public List<QcDataRow> Rows { get; } = [];
    }

    private sealed record SegmentKey(
        bool IsStd,
        string SourceKind,
        string Port,
        string LotNo,
        string SampleName)
    {
        public static SegmentKey From(QcDataRow row)
        {
            var sourceKind = Normalize(row.SourceKind);
            var port = Normalize(row.Port);
            var isStd = sourceKind == "STD" || port == "STD";

            return new SegmentKey(
                isStd,
                isStd ? "STD" : "PORT",
                port,
                Normalize(row.LotNo),
                Normalize(row.SampleName));
        }
    }
}
