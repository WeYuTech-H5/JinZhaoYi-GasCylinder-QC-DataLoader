namespace JinZhaoYi.GasQcDataLoader.DataModels;

public static class Query2ColumnLayout
{
    public const int HeaderRowNumber = 3;

    public const int DataStartRowNumber = 4;

    public static IReadOnlyList<string> Headers { get; } = BuildHeaders();

    public static IReadOnlyList<object?> BuildValues(Query2ExportRow exportRow)
    {
        var row = exportRow.Row;
        var values = new List<object?>(Headers.Count)
        {
            ResolveDisplayId(exportRow),
            row.AnlzTime,
            row.Inst,
            row.Port,
            ResolveDisplaySi0Id(row),
            row.SampleNo,
            ParseLong(row.LotNo) is { } lotNo ? lotNo : row.LotNo,
            row.DataFilename,
            row.DataFilepath,
            row.PcName,
            row.Container,
            row.Description,
            ParseDecimal(row.EmVolts) is { } emVolts ? emVolts : row.EmVolts,
            ParseDecimal(row.RelativeEm) is { } relativeEm ? relativeEm : row.RelativeEm,
            row.SampleName,
            row.SampleType
        };

        foreach (var analyte in CompoundMap.Analytes)
        {
            values.Add(row.Areas.GetValueOrDefault(analyte.Suffix));
        }

        foreach (var analyte in CompoundMap.Analytes)
        {
            values.Add(row.Ppbs.GetValueOrDefault(analyte.Suffix));
        }

        foreach (var analyte in CompoundMap.Analytes)
        {
            values.Add(row.RetentionTimes.GetValueOrDefault(analyte.Suffix));
        }

        return values;
    }

    private static IReadOnlyList<string> BuildHeaders()
    {
        var headers = new List<string>
        {
            "id",
            "AnlzTime",
            "Inst",
            "Port",
            "si0_id",
            "SampleNo",
            "LotNo",
            "DataFilename",
            "DataFilepath",
            "PCName",
            "Container",
            "Description",
            "EMVolts",
            "RelativeEM",
            "SampleName",
            "SampleType"
        };

        foreach (var analyte in CompoundMap.Analytes)
        {
            headers.Add(analyte.Suffix);
        }

        foreach (var analyte in CompoundMap.Analytes)
        {
            headers.Add(analyte.Suffix);
        }

        foreach (var analyte in CompoundMap.Analytes)
        {
            headers.Add(analyte.Suffix);
        }

        return headers;
    }

    private static long? ParseLong(string? value) =>
        long.TryParse(value, out var parsed) ? parsed : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, out var parsed) ? parsed : null;

    private static object? ResolveDisplaySi0Id(QcDataRow row)
    {
        if (ParseLong(row.Si0Id) is { } si0Id && si0Id != 0)
        {
            return si0Id;
        }

        return IsMissingDisplaySi0Id(row.Si0Id)
            ? row.SampleNo
            : row.Si0Id;
    }

    private static string? ResolveDisplayId(Query2ExportRow exportRow)
    {
        if (exportRow.RowType != Query2ExportRowType.Ppb ||
            !IsMissingDisplaySi0Id(exportRow.Row.Si0Id) ||
            !exportRow.Row.SampleNo.HasValue)
        {
            return exportRow.Row.Id;
        }

        return $"ppb(S{exportRow.Row.SampleNo.Value})";
    }

    private static bool IsMissingDisplaySi0Id(string? si0Id) =>
        string.IsNullOrWhiteSpace(si0Id) ||
        string.Equals(si0Id.Trim(), "0", StringComparison.OrdinalIgnoreCase);
}
