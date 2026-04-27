using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class ImportWriteSetBuilder(
    IRawRowFactory rawRowFactory,
    ICalculationService calculationService,
    ILogger<ImportWriteSetBuilder> logger) : IImportWriteSetBuilder
{
    public ImportWriteSet BuildSingleFileWriteSet(ParsedQuantFile parsed, MfgLot lot)
    {
        var writeSet = new ImportWriteSet();
        var rawRow = rawRowFactory.Create(parsed, lot, CreateRawId(parsed));
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Raw, rawRow));

        if (parsed.Source.SourceKind == QuantSourceKind.Std)
        {
            writeSet.StdRawRows.Add(rawRow);
        }
        else
        {
            writeSet.PortRawRows.Add(rawRow);
        }

        return writeSet;
    }

    public ImportWriteSet BuildWriteSet(
        IReadOnlyCollection<ParsedQuantFile> parsedFiles,
        IReadOnlyDictionary<string, MfgLot> lots,
        QcDataRow rf)
    {
        var writeSet = new ImportWriteSet();
        writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Rf, rf.DeepClone()));

        var orderedFiles = parsedFiles
            .OrderBy(file => file.AcquiredAt)
            .ThenBy(file => file.Source.Port, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Source.DataFilename, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        QcDataRow? activeStdAverage = null;

        foreach (var group in BuildContiguousGroups(orderedFiles))
        {
            var rawRows = group
                .Select(file => rawRowFactory.Create(file, lots[file.LotNo], CreateRawId(file)))
                .ToList();

            foreach (var rawRow in rawRows)
            {
                writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Raw, rawRow));
            }

            if (group[0].Source.SourceKind == QuantSourceKind.Std)
            {
                writeSet.StdRawRows.AddRange(rawRows);

                if (rawRows.Count < 2)
                {
                    logger.LogWarning("STD 群組資料不足兩筆，略過 AVG/RPD 計算：時間={Time}。", group[0].AcquiredAt);
                    continue;
                }

                var (first, second) = LastTwo(rawRows);
                var stdAverage = calculationService.CreateAverageRow($"AVG({first.Id}:{second.Id})", first, second);
                var stdRpd = calculationService.CreateRpdRow($"RPD({first.Id}:{second.Id})", first, second);

                writeSet.StdAverageRows.Add(stdAverage);
                writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Avg, stdAverage));

                if (activeStdAverage is not null)
                {
                    var stdQc = calculationService.CreateStdQcRow($"QC({activeStdAverage.Id},{stdAverage.Id})", activeStdAverage, stdAverage);
                    writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Qc, stdQc));
                }

                writeSet.StdRpdRows.Add(stdRpd);
                writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Rpd, stdRpd));

                activeStdAverage = stdAverage;
                continue;
            }

            if (activeStdAverage is not null)
            {
                foreach (var rawRow in rawRows)
                {
                    calculationService.ApplyPortRawPpb(rawRow, rf, activeStdAverage);
                }
            }

            writeSet.PortRawRows.AddRange(rawRows);

            if (rawRows.Count < 2)
            {
                logger.LogWarning("PORT 群組資料不足兩筆，略過 AVG/PPB/RPD 計算：Port={Port}，時間={Time}。", group[0].Source.Port, group[0].AcquiredAt);
                continue;
            }

            var (portFirst, portSecond) = LastTwo(rawRows);
            var portAverage = calculationService.CreateAverageRow($"AVG({portFirst.Id}:{portSecond.Id})", portFirst, portSecond);
            var portRpd = calculationService.CreateRpdRow($"RPD({portFirst.Id}:{portSecond.Id})", portFirst, portSecond);

            writeSet.PortAverageRows.Add(portAverage);
            writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Avg, portAverage));

            if (activeStdAverage is not null)
            {
                var portPpb = calculationService.CreatePortPpbRow($"ppb({portAverage.Si0Id})", portAverage, rf, activeStdAverage);
                writeSet.PortPpbRows.Add(portPpb);
                writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Ppb, portPpb));
            }
            else
            {
                logger.LogWarning("PORT 群組在目前上下文中沒有可用 STD AVG，略過 PPB：Port={Port}，時間={Time}。", group[0].Source.Port, group[0].AcquiredAt);
            }

            writeSet.PortRpdRows.Add(portRpd);
            writeSet.Query2Rows.Add(new Query2ExportRow(Query2ExportRowType.Rpd, portRpd));
        }

        return writeSet;
    }

    private static IReadOnlyList<IReadOnlyList<ParsedQuantFile>> BuildContiguousGroups(IReadOnlyList<ParsedQuantFile> orderedFiles)
    {
        var groups = new List<IReadOnlyList<ParsedQuantFile>>();
        var current = new List<ParsedQuantFile>();

        foreach (var file in orderedFiles)
        {
            if (current.Count > 0 && !IsSameGroup(current[^1], file))
            {
                groups.Add(current);
                current = [];
            }

            current.Add(file);
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }

    private static bool IsSameGroup(ParsedQuantFile left, ParsedQuantFile right) =>
        left.Source.SourceKind == right.Source.SourceKind &&
        string.Equals(left.Source.Port, right.Source.Port, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.LotNo, right.LotNo, StringComparison.OrdinalIgnoreCase);

    private static (QcDataRow First, QcDataRow Second) LastTwo(IReadOnlyList<QcDataRow> rows) =>
        (rows[^2], rows[^1]);

    private static string CreateRawId(ParsedQuantFile parsed) =>
        $"{parsed.AcquiredAt:yyyyMMdd}{parsed.SampleNo:000}";
}
