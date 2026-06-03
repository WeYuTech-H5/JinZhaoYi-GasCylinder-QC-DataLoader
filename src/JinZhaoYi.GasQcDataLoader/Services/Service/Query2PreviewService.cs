using System.Globalization;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class Query2PreviewService(ICalculationService calculationService) : IQuery2PreviewService
{
    private const string AreaKind = "area";
    private const string PpbKind = "ppb";
    private const string RtKind = "rt";
    private const string AverageFormula = "average";
    private const string RpdFormula = "rpd";
    private const string QcFormula = "qc";
    private const string PortRawPpbFormula = "portRawPpb";
    private const string PortPpbFormula = "portPpb";
    private const string HiddenSourceKindKey = "_sourceKind";
    private const string HiddenSourceFolderNameKey = "_sourceFolderName";
    private const string HiddenId1Key = "_id1";
    private const string HiddenId2Key = "_id2";
    private const NumberStyles DecimalNumberStyles = NumberStyles.Number | NumberStyles.AllowExponent;

    private static readonly Query2PreviewColumn[] BaseColumns =
    [
        new() { Key = "id", Header = "id", Order = 1, DataType = "text" },
        new() { Key = "anlzTime", Header = "AnlzTime", Order = 2, DataType = "datetime" },
        new() { Key = "inst", Header = "Inst", Order = 3, DataType = "text" },
        new() { Key = "port", Header = "Port", Order = 4, DataType = "text" },
        new() { Key = "si0Id", Header = "si0_id", Order = 5, DataType = "number" },
        new() { Key = "sampleNo", Header = "SampleNo", Order = 6, DataType = "number" },
        new() { Key = "lotNo", Header = "LotNo", Order = 7, DataType = "text" },
        new() { Key = "dataFilename", Header = "DataFilename", Order = 8, DataType = "text" },
        new() { Key = "dataFilepath", Header = "DataFilepath", Order = 9, DataType = "text" },
        new() { Key = "pcName", Header = "PCName", Order = 10, DataType = "text" },
        new() { Key = "container", Header = "Container", Order = 11, DataType = "text" },
        new() { Key = "description", Header = "Description", Order = 12, DataType = "text" },
        new() { Key = "emVolts", Header = "EMVolts", Order = 13, DataType = "number" },
        new() { Key = "relativeEm", Header = "RelativeEM", Order = 14, DataType = "number" },
        new() { Key = "sampleName", Header = "SampleName", Order = 15, DataType = "text" },
        new() { Key = "sampleType", Header = "SampleType", Order = 16, DataType = "text" }
    ];

    public Query2PreviewState CreatePreview(
        DateTime startDate,
        DateTime endDate,
        string rfId,
        IReadOnlyList<string> stdRawIds,
        IReadOnlyList<string> portRawIds,
        IReadOnlyList<Query2ExportRow> rows)
    {
        var previewRows = rows
            .Select((row, index) =>
            {
                var values = BuildValueMap(row);
                return new Query2PreviewRow
                {
                    RowKey = $"r{index + 1:0000}",
                    RowType = row.RowType,
                    DisplayId = values.GetValueOrDefault("id"),
                    OriginalValues = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase),
                    CurrentValues = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase),
                    ManualOverrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                };
            })
            .ToList();

        AssignFormulas(previewRows);

        return new Query2PreviewState
        {
            StartDate = startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            EndDate = endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ExportDateText = startDate.Date == endDate.Date
                ? startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : $"{startDate:yyyyMMdd}-{endDate:yyyyMMdd}",
            RfId = rfId,
            StdRawIds = stdRawIds.ToArray(),
            PortRawIds = portRawIds.ToArray(),
            Columns = BuildColumns(),
            Rows = previewRows
        };
    }

    public Query2PreviewState Recalculate(Query2PreviewState preview)
    {
        var state = CloneState(preview);
        state.Columns = BuildColumns();
        ValidateManualOverrides(state);

        var workingRows = state.Rows
            .Select(row => new WorkingRow(row, ToDataRow(row.RowType, row.CurrentValues)))
            .ToList();

        foreach (var workingRow in workingRows)
        {
            ApplyManualOverrides(workingRow);
        }

        ApplyFormulas(workingRows);

        foreach (var workingRow in workingRows)
        {
            workingRow.Preview.CurrentValues = BuildValueMap(new Query2ExportRow(workingRow.Preview.RowType, workingRow.Row));
            workingRow.Preview.DisplayId = workingRow.Preview.CurrentValues.GetValueOrDefault("id");
        }

        return state;
    }

    public IReadOnlyList<Query2ExportRow> ToExportRows(Query2PreviewState preview)
    {
        var state = Recalculate(preview);
        return state.Rows
            .Select(row => new Query2ExportRow(row.RowType, ToDataRow(row.RowType, row.CurrentValues)))
            .ToArray();
    }

    public IReadOnlyList<Query2PreviewEditLogRow> BuildEditLogs(
        Query2PreviewState preview,
        string excelExportKey,
        Guid exportSessionId,
        DateTime exportedAt,
        string exportUser)
    {
        if (!TryParseBatchDate(preview.StartDate, out var startDate) ||
            !TryParseBatchDate(preview.EndDate, out var endDate))
        {
            throw new InvalidOperationException("Preview startDate/endDate must use yyyyMMdd format.");
        }

        var logs = new List<Query2PreviewEditLogRow>();
        var stdRawIds = FormatSelectedIds(preview.StdRawIds);
        var portRawIds = FormatSelectedIds(preview.PortRawIds);
        var rfId = preview.RfId?.Trim() ?? string.Empty;

        foreach (var row in preview.Rows)
        {
            foreach (var (fieldKey, _) in row.ManualOverrides)
            {
                if (!TryParseEditableFieldKey(fieldKey, out var valueKind, out var analyte))
                {
                    continue;
                }

                row.OriginalValues.TryGetValue(fieldKey, out var originalValue);
                row.CurrentValues.TryGetValue(fieldKey, out var newValue);
                if (ValuesEqual(originalValue, newValue))
                {
                    continue;
                }

                logs.Add(new Query2PreviewEditLogRow(
                    exportSessionId,
                    excelExportKey,
                    startDate,
                    endDate,
                    rfId,
                    stdRawIds,
                    portRawIds,
                    row.RowKey,
                    row.RowType,
                    row.DisplayId,
                    fieldKey,
                    valueKind,
                    analyte,
                    NormalizeBlank(originalValue),
                    NormalizeBlank(newValue),
                    exportedAt,
                    exportUser,
                    DateTime.Now));
            }
        }

        return logs;
    }

    private void ApplyFormulas(IReadOnlyList<WorkingRow> rows)
    {
        var byKey = rows.ToDictionary(row => row.Preview.RowKey, StringComparer.OrdinalIgnoreCase);
        var rf = rows.FirstOrDefault(row => row.Preview.RowType == Query2ExportRowType.Rf)?.Row;

        foreach (var workingRow in rows)
        {
            var formula = workingRow.Preview.Formula;
            if (formula is null || string.IsNullOrWhiteSpace(formula.Kind))
            {
                continue;
            }

            switch (formula.Kind)
            {
                case AverageFormula:
                    if (TryGetTwoSources(formula, byKey, out var avgFirst, out var avgSecond))
                    {
                        var calculated = calculationService.CreateAverageRow(workingRow.Row.Id ?? string.Empty, avgFirst.Row, avgSecond.Row);
                        CopyAreas(workingRow, calculated);
                        ClearNonManual(workingRow, PpbKind);
                        ClearNonManual(workingRow, RtKind);
                    }

                    break;

                case RpdFormula:
                    if (TryGetTwoSources(formula, byKey, out var rpdFirst, out var rpdSecond))
                    {
                        var calculated = calculationService.CreateRpdRow(workingRow.Row.Id ?? string.Empty, rpdFirst.Row, rpdSecond.Row);
                        CopyAreas(workingRow, calculated);
                        ClearNonManual(workingRow, PpbKind);
                        ClearNonManual(workingRow, RtKind);
                    }

                    break;

                case QcFormula:
                    if (TryGetTwoSources(formula, byKey, out var previousStdAverage, out var currentStdAverage))
                    {
                        var calculated = calculationService.CreateStdQcRow(workingRow.Row.Id ?? string.Empty, previousStdAverage.Row, currentStdAverage.Row);
                        CopyAreas(workingRow, calculated);
                        ClearNonManual(workingRow, PpbKind);
                        ClearNonManual(workingRow, RtKind);
                    }

                    break;

                case PortRawPpbFormula:
                    if (rf is not null &&
                        TryGetStdAverage(formula, byKey, out var activeStdAverage))
                    {
                        var calculated = workingRow.Row.DeepClone();
                        calculationService.ApplyPortRawPpb(calculated, rf, activeStdAverage.Row);
                        CopyPpbs(workingRow, calculated);
                    }

                    break;

                case PortPpbFormula:
                    if (rf is not null &&
                        TryGetFirstSource(formula, byKey, out var portAverage) &&
                        TryGetStdAverage(formula, byKey, out var stdAverage))
                    {
                        var calculated = calculationService.CreatePortPpbRow(workingRow.Row.Id ?? string.Empty, portAverage.Row, rf, stdAverage.Row);
                        CopyPortPpbResult(workingRow, calculated);
                        ClearNonManual(workingRow, RtKind);
                    }

                    break;
            }
        }
    }

    private static void CopyAreas(WorkingRow target, QcDataRow calculated)
    {
        foreach (var analyte in CompoundMap.Analytes)
        {
            if (!IsManual(target, AreaKind, analyte.Suffix))
            {
                target.Row.Areas[analyte.Suffix] = calculated.Areas.GetValueOrDefault(analyte.Suffix);
            }
        }
    }

    private static void CopyPpbs(WorkingRow target, QcDataRow calculated)
    {
        foreach (var analyte in CompoundMap.Analytes)
        {
            if (!IsManual(target, PpbKind, analyte.Suffix))
            {
                target.Row.Ppbs[analyte.Suffix] = calculated.Ppbs.GetValueOrDefault(analyte.Suffix);
            }
        }
    }

    private static void CopyPortPpbResult(WorkingRow target, QcDataRow calculated)
    {
        foreach (var analyte in CompoundMap.Analytes)
        {
            var areaManual = IsManual(target, AreaKind, analyte.Suffix);
            var ppbManual = IsManual(target, PpbKind, analyte.Suffix);
            if (areaManual || ppbManual)
            {
                var manualValue = target.Row.Ppbs.GetValueOrDefault(analyte.Suffix) ??
                    target.Row.Areas.GetValueOrDefault(analyte.Suffix);
                target.Row.Areas[analyte.Suffix] = manualValue;
                target.Row.Ppbs[analyte.Suffix] = manualValue;
                continue;
            }

            var value = calculated.Areas.GetValueOrDefault(analyte.Suffix);
            target.Row.Areas[analyte.Suffix] = value;
            target.Row.Ppbs[analyte.Suffix] = value;
        }
    }

    private static void ClearNonManual(WorkingRow target, string valueKind)
    {
        foreach (var analyte in CompoundMap.Analytes)
        {
            if (IsManual(target, valueKind, analyte.Suffix))
            {
                continue;
            }

            if (string.Equals(valueKind, PpbKind, StringComparison.OrdinalIgnoreCase))
            {
                target.Row.Ppbs[analyte.Suffix] = null;
            }
            else if (string.Equals(valueKind, RtKind, StringComparison.OrdinalIgnoreCase))
            {
                target.Row.RetentionTimes[analyte.Suffix] = null;
            }
        }
    }

    private static bool TryGetTwoSources(
        Query2PreviewFormula formula,
        IReadOnlyDictionary<string, WorkingRow> byKey,
        out WorkingRow first,
        out WorkingRow second)
    {
        first = default!;
        second = default!;
        if (formula.SourceRowKeys.Count < 2)
        {
            return false;
        }

        return byKey.TryGetValue(formula.SourceRowKeys[0], out first!) &&
               byKey.TryGetValue(formula.SourceRowKeys[1], out second!);
    }

    private static bool TryGetFirstSource(
        Query2PreviewFormula formula,
        IReadOnlyDictionary<string, WorkingRow> byKey,
        out WorkingRow row)
    {
        row = default!;
        return formula.SourceRowKeys.Count > 0 &&
               byKey.TryGetValue(formula.SourceRowKeys[0], out row!);
    }

    private static bool TryGetStdAverage(
        Query2PreviewFormula formula,
        IReadOnlyDictionary<string, WorkingRow> byKey,
        out WorkingRow row)
    {
        row = default!;
        return !string.IsNullOrWhiteSpace(formula.StdAverageRowKey) &&
               byKey.TryGetValue(formula.StdAverageRowKey, out row!);
    }

    private static void ApplyManualOverrides(WorkingRow workingRow)
    {
        ValidatePpbManualConflict(workingRow.Preview);

        foreach (var (fieldKey, value) in workingRow.Preview.ManualOverrides)
        {
            if (!TryParseEditableFieldKey(fieldKey, out var valueKind, out var analyte))
            {
                throw new InvalidOperationException($"Field '{fieldKey}' is not editable.");
            }

            var parsed = ParseNullableDecimal(value, fieldKey);
            SetAnalyteValue(workingRow.Row, valueKind, analyte, parsed);
        }
    }

    private static void ValidatePpbManualConflict(Query2PreviewRow row)
    {
        if (row.RowType != Query2ExportRowType.Ppb)
        {
            return;
        }

        foreach (var analyte in CompoundMap.Analytes)
        {
            var areaKey = FieldKey(AreaKind, analyte.Suffix);
            var ppbKey = FieldKey(PpbKind, analyte.Suffix);
            if (!row.ManualOverrides.TryGetValue(areaKey, out var areaValue) ||
                !row.ManualOverrides.TryGetValue(ppbKey, out var ppbValue) ||
                ValuesEqual(areaValue, ppbValue))
            {
                continue;
            }

            throw new InvalidOperationException($"PPB row cannot have different manual values for '{areaKey}' and '{ppbKey}'.");
        }
    }

    private static void ValidateManualOverrides(Query2PreviewState state)
    {
        foreach (var row in state.Rows)
        {
            foreach (var (fieldKey, value) in row.ManualOverrides)
            {
                if (!TryParseEditableFieldKey(fieldKey, out _, out _))
                {
                    throw new InvalidOperationException($"Field '{fieldKey}' is not editable.");
                }

                _ = ParseNullableDecimal(value, fieldKey);
            }
        }
    }

    private static void AssignFormulas(IReadOnlyList<Query2PreviewRow> rows)
    {
        var byId = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.CurrentValues.GetValueOrDefault("id")))
            .GroupBy(row => row.CurrentValues.GetValueOrDefault("id")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var stdAverageRows = rows
            .Where(row => row.RowType == Query2ExportRowType.Avg && IsStd(row))
            .OrderBy(row => ParseDateOrNull(row.CurrentValues.GetValueOrDefault("anlzTime")) ?? DateTime.MaxValue)
            .ThenBy(row => row.RowKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var row in rows)
        {
            row.Formula = row.RowType switch
            {
                Query2ExportRowType.Avg => BuildTwoSourceFormula(row, byId, AverageFormula),
                Query2ExportRowType.Rpd => BuildTwoSourceFormula(row, byId, RpdFormula),
                Query2ExportRowType.Qc => BuildTwoSourceFormula(row, byId, QcFormula),
                Query2ExportRowType.Raw when !IsStd(row) => BuildPortRawPpbFormula(row, stdAverageRows),
                Query2ExportRowType.Ppb => BuildPortPpbFormula(row, rows, stdAverageRows),
                _ => null
            };
        }
    }

    private static Query2PreviewFormula? BuildTwoSourceFormula(
        Query2PreviewRow row,
        IReadOnlyDictionary<string, Query2PreviewRow> byId,
        string formulaKind)
    {
        var id1 = row.CurrentValues.GetValueOrDefault(HiddenId1Key);
        var id2 = row.CurrentValues.GetValueOrDefault(HiddenId2Key);
        if (string.IsNullOrWhiteSpace(id1) ||
            string.IsNullOrWhiteSpace(id2) ||
            !byId.TryGetValue(id1, out var first) ||
            !byId.TryGetValue(id2, out var second))
        {
            return null;
        }

        return new Query2PreviewFormula
        {
            Kind = formulaKind,
            SourceRowKeys = [first.RowKey, second.RowKey]
        };
    }

    private static Query2PreviewFormula? BuildPortRawPpbFormula(
        Query2PreviewRow row,
        IReadOnlyList<Query2PreviewRow> stdAverageRows)
    {
        var stdAverage = ResolveStdAverageFor(row, stdAverageRows);
        return stdAverage is null
            ? null
            : new Query2PreviewFormula
            {
                Kind = PortRawPpbFormula,
                StdAverageRowKey = stdAverage.RowKey
            };
    }

    private static Query2PreviewFormula? BuildPortPpbFormula(
        Query2PreviewRow row,
        IReadOnlyList<Query2PreviewRow> rows,
        IReadOnlyList<Query2PreviewRow> stdAverageRows)
    {
        var id1 = row.CurrentValues.GetValueOrDefault(HiddenId1Key);
        var id2 = row.CurrentValues.GetValueOrDefault(HiddenId2Key);
        var portAverage = rows.FirstOrDefault(candidate =>
            candidate.RowType == Query2ExportRowType.Avg &&
            !IsStd(candidate) &&
            string.Equals(candidate.CurrentValues.GetValueOrDefault(HiddenId1Key), id1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.CurrentValues.GetValueOrDefault(HiddenId2Key), id2, StringComparison.OrdinalIgnoreCase));
        if (portAverage is null)
        {
            return null;
        }

        var stdAverage = ResolveStdAverageFor(portAverage, stdAverageRows);
        return stdAverage is null
            ? null
            : new Query2PreviewFormula
            {
                Kind = PortPpbFormula,
                SourceRowKeys = [portAverage.RowKey],
                StdAverageRowKey = stdAverage.RowKey
            };
    }

    private static Query2PreviewRow? ResolveStdAverageFor(
        Query2PreviewRow row,
        IReadOnlyList<Query2PreviewRow> stdAverageRows)
    {
        if (stdAverageRows.Count == 0)
        {
            return null;
        }

        var rowTime = ParseDateOrNull(row.CurrentValues.GetValueOrDefault("anlzTime"));
        if (!rowTime.HasValue)
        {
            return stdAverageRows[^1];
        }

        return stdAverageRows
            .Where(candidate => (ParseDateOrNull(candidate.CurrentValues.GetValueOrDefault("anlzTime")) ?? DateTime.MinValue) <= rowTime.Value)
            .LastOrDefault() ?? stdAverageRows[0];
    }

    private static bool IsStd(Query2PreviewRow row)
    {
        var sourceKind = row.CurrentValues.GetValueOrDefault(HiddenSourceKindKey);
        var port = row.CurrentValues.GetValueOrDefault("port");
        return string.Equals(sourceKind, "STD", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(port, "STD", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Query2PreviewColumn> BuildColumns()
    {
        var columns = new List<Query2PreviewColumn>(BaseColumns.Select(CloneColumn));
        var order = BaseColumns.Length;

        foreach (var valueKind in new[] { AreaKind, PpbKind, RtKind })
        {
            foreach (var analyte in CompoundMap.Analytes)
            {
                columns.Add(new Query2PreviewColumn
                {
                    Key = FieldKey(valueKind, analyte.Suffix),
                    Header = analyte.Suffix,
                    Order = ++order,
                    Editable = true,
                    ValueKind = valueKind,
                    Analyte = analyte.Suffix,
                    DataType = "decimal"
                });
            }
        }

        return columns;
    }

    private static Query2PreviewColumn CloneColumn(Query2PreviewColumn column) =>
        new()
        {
            Key = column.Key,
            Header = column.Header,
            Order = column.Order,
            Editable = column.Editable,
            ValueKind = column.ValueKind,
            Analyte = column.Analyte,
            DataType = column.DataType
        };

    private static Dictionary<string, string?> BuildValueMap(Query2ExportRow exportRow)
    {
        var row = exportRow.Row;
        var layoutValues = Query2ColumnLayout.BuildValues(exportRow);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = FormatValue(layoutValues[0]),
            ["anlzTime"] = FormatValue(row.AnlzTime),
            ["inst"] = row.Inst,
            ["port"] = row.Port,
            ["si0Id"] = FormatValue(layoutValues[4]),
            ["sampleNo"] = FormatValue(row.SampleNo),
            ["lotNo"] = row.LotNo,
            ["dataFilename"] = row.DataFilename,
            ["dataFilepath"] = row.DataFilepath,
            ["pcName"] = row.PcName,
            ["container"] = row.Container,
            ["description"] = row.Description,
            ["emVolts"] = row.EmVolts,
            ["relativeEm"] = row.RelativeEm,
            ["sampleName"] = row.SampleName,
            ["sampleType"] = row.SampleType,
            [HiddenSourceKindKey] = row.SourceKind,
            [HiddenSourceFolderNameKey] = row.SourceFolderName,
            [HiddenId1Key] = row.Id1,
            [HiddenId2Key] = row.Id2
        };

        foreach (var analyte in CompoundMap.Analytes)
        {
            var area = row.Areas.GetValueOrDefault(analyte.Suffix);
            var ppb = row.Ppbs.GetValueOrDefault(analyte.Suffix);
            if (exportRow.RowType == Query2ExportRowType.Ppb)
            {
                ppb ??= area;
            }

            values[FieldKey(AreaKind, analyte.Suffix)] = FormatValue(area);
            values[FieldKey(PpbKind, analyte.Suffix)] = FormatValue(ppb);
            values[FieldKey(RtKind, analyte.Suffix)] = FormatValue(row.RetentionTimes.GetValueOrDefault(analyte.Suffix));
        }

        return values;
    }

    private static QcDataRow ToDataRow(Query2ExportRowType rowType, IReadOnlyDictionary<string, string?> values)
    {
        var row = new QcDataRow
        {
            Id = values.GetValueOrDefault("id"),
            AnlzTime = ParseDateOrNull(values.GetValueOrDefault("anlzTime")),
            Inst = values.GetValueOrDefault("inst"),
            Port = values.GetValueOrDefault("port"),
            SourceKind = values.GetValueOrDefault(HiddenSourceKindKey),
            SourceFolderName = values.GetValueOrDefault(HiddenSourceFolderNameKey),
            Si0Id = ParseIntOrNull(values.GetValueOrDefault("si0Id")),
            SampleNo = ParseIntOrNull(values.GetValueOrDefault("sampleNo")),
            LotNo = values.GetValueOrDefault("lotNo"),
            DataFilename = values.GetValueOrDefault("dataFilename"),
            DataFilepath = values.GetValueOrDefault("dataFilepath"),
            PcName = values.GetValueOrDefault("pcName"),
            Container = values.GetValueOrDefault("container"),
            Description = values.GetValueOrDefault("description"),
            EmVolts = values.GetValueOrDefault("emVolts"),
            RelativeEm = values.GetValueOrDefault("relativeEm"),
            SampleName = values.GetValueOrDefault("sampleName"),
            SampleType = values.GetValueOrDefault("sampleType"),
            Id1 = values.GetValueOrDefault(HiddenId1Key),
            Id2 = values.GetValueOrDefault(HiddenId2Key)
        };

        foreach (var analyte in CompoundMap.Analytes)
        {
            var area = ParseNullableDecimal(values.GetValueOrDefault(FieldKey(AreaKind, analyte.Suffix)), FieldKey(AreaKind, analyte.Suffix));
            var ppb = ParseNullableDecimal(values.GetValueOrDefault(FieldKey(PpbKind, analyte.Suffix)), FieldKey(PpbKind, analyte.Suffix));
            if (rowType == Query2ExportRowType.Ppb)
            {
                area = ppb ?? area;
                ppb = area;
            }

            row.Areas[analyte.Suffix] = area;
            row.Ppbs[analyte.Suffix] = ppb;
            row.RetentionTimes[analyte.Suffix] = ParseNullableDecimal(values.GetValueOrDefault(FieldKey(RtKind, analyte.Suffix)), FieldKey(RtKind, analyte.Suffix));
        }

        return row;
    }

    private static void SetAnalyteValue(QcDataRow row, string valueKind, string analyte, decimal? value)
    {
        if (string.Equals(valueKind, AreaKind, StringComparison.OrdinalIgnoreCase))
        {
            row.Areas[analyte] = value;
            return;
        }

        if (string.Equals(valueKind, PpbKind, StringComparison.OrdinalIgnoreCase))
        {
            row.Ppbs[analyte] = value;
            return;
        }

        row.RetentionTimes[analyte] = value;
    }

    private static Query2PreviewState CloneState(Query2PreviewState state) =>
        new()
        {
            StartDate = state.StartDate,
            EndDate = state.EndDate,
            ExportDateText = state.ExportDateText,
            RfId = state.RfId,
            StdRawIds = state.StdRawIds.ToArray(),
            PortRawIds = state.PortRawIds.ToArray(),
            Columns = state.Columns.Select(CloneColumn).ToArray(),
            Rows = state.Rows.Select(CloneRow).ToArray()
        };

    private static Query2PreviewRow CloneRow(Query2PreviewRow row) =>
        new()
        {
            RowKey = row.RowKey,
            RowType = row.RowType,
            DisplayId = row.DisplayId,
            OriginalValues = new Dictionary<string, string?>(row.OriginalValues, StringComparer.OrdinalIgnoreCase),
            CurrentValues = new Dictionary<string, string?>(row.CurrentValues, StringComparer.OrdinalIgnoreCase),
            ManualOverrides = new Dictionary<string, string?>(row.ManualOverrides, StringComparer.OrdinalIgnoreCase),
            Formula = row.Formula is null
                ? null
                : new Query2PreviewFormula
                {
                    Kind = row.Formula.Kind,
                    SourceRowKeys = row.Formula.SourceRowKeys.ToArray(),
                    StdAverageRowKey = row.Formula.StdAverageRowKey
                }
        };

    private static bool IsManual(WorkingRow row, string valueKind, string analyte) =>
        row.Preview.ManualOverrides.ContainsKey(FieldKey(valueKind, analyte));

    private static bool TryParseEditableFieldKey(string fieldKey, out string valueKind, out string analyte)
    {
        valueKind = string.Empty;
        analyte = string.Empty;
        var separator = fieldKey.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == fieldKey.Length - 1)
        {
            return false;
        }

        valueKind = fieldKey[..separator];
        analyte = fieldKey[(separator + 1)..];
        var analyteValue = analyte;
        if (!string.Equals(valueKind, AreaKind, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(valueKind, PpbKind, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(valueKind, RtKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CompoundMap.Analytes.Any(item => string.Equals(item.Suffix, analyteValue, StringComparison.OrdinalIgnoreCase));
    }

    private static string FieldKey(string valueKind, string analyte) => $"{valueKind}:{analyte}";

    private static string? FormatValue(object? value) =>
        value switch
        {
            null => null,
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString("0.#############################", CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString("G17", CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString("G9", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static decimal? ParseNullableDecimal(string? value, string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, DecimalNumberStyles, CultureInfo.InvariantCulture, out var parsed) ||
            decimal.TryParse(value, DecimalNumberStyles, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Field '{fieldKey}' must be empty or a decimal number.");
    }

    private static int? ParseIntOrNull(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTime? ParseDateOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy/MM/dd HH:mm:ss",
            "yyyyMMdd"
        };
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) ||
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryParseBatchDate(string? value, out DateTime batchDate) =>
        DateTime.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out batchDate);

    private static bool ValuesEqual(string? first, string? second)
    {
        first = NormalizeBlank(first);
        second = NormalizeBlank(second);
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        var firstDecimal = ParseNullableDecimalNoThrow(first);
        var secondDecimal = ParseNullableDecimalNoThrow(second);
        return firstDecimal.HasValue && secondDecimal.HasValue
            ? firstDecimal.Value == secondDecimal.Value
            : string.Equals(first, second, StringComparison.Ordinal);
    }

    private static decimal? ParseNullableDecimalNoThrow(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, DecimalNumberStyles, CultureInfo.InvariantCulture, out var parsed) ||
               decimal.TryParse(value, DecimalNumberStyles, CultureInfo.CurrentCulture, out parsed)
            ? parsed
            : null;
    }

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FormatSelectedIds(IEnumerable<string> ids) =>
        string.Join(
            "\n",
            ids.Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal));

    private sealed record WorkingRow(Query2PreviewRow Preview, QcDataRow Row);
}
