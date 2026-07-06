using System.Globalization;
using System.Text.RegularExpressions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed partial class QcResultEvaluator : IQcResultEvaluator
{
    private const string CalType = "Au";
    private const string QcComplete = "1";
    private const string QcInst = "QC-01";
    private const string PressureFailDesc = "壓力不足";

    public MfgLotQcUpdate? Evaluate(
        QcDataRow ppbRow,
        IReadOnlyList<QcDataRow> portRawRows,
        QcDataRow? rf,
        QcResultSettingsDto settings)
    {
        if (string.IsNullOrWhiteSpace(ppbRow.LotNo))
        {
            return null;
        }

        var containerType = ResolveContainerType(ppbRow.Container);
        var pressureReading = ResolvePressureReading(portRawRows, ppbRow);
        var failedAnalytes = EvaluateConcentrations(ppbRow, containerType, settings);
        var pressureFailed = EvaluatePressure(pressureReading, containerType, settings);
        var failDescriptions = new List<string>();

        if (pressureFailed)
        {
            failDescriptions.Add(PressureFailDesc);
        }

        if (failedAnalytes.Count > 0)
        {
            failDescriptions.Add($"Conc({string.Join(",", failedAnalytes)})");
        }

        var result = failDescriptions.Count == 0 ? QcResultValues.Pass : QcResultValues.Fail;
        ppbRow.QcResult = result;
        ppbRow.FailDesc = failDescriptions.Count == 0 ? null : string.Join("; ", failDescriptions);

        return new MfgLotQcUpdate(
            ppbRow.LotNo.Trim(),
            ppbRow.Si0Id is 0 ? null : ppbRow.Si0Id,
            ResolveProdOrder(ppbRow.LotNo),
            CalType,
            ResolveCalId(ppbRow),
            pressureReading.IniPrsText,
            QcComplete,
            QcInst,
            NullIfWhiteSpace(ppbRow.Port),
            ppbRow.AnlzTime,
            result,
            NullIfWhiteSpace(rf?.Id),
            pressureReading.FnlPrsText,
            ppbRow.FailDesc);
    }

    public IReadOnlyList<MfgLotQcUpdate> EvaluateExportRows(
        IReadOnlyList<Query2ExportRow> rows,
        string? rfId,
        QcResultSettingsDto settings)
    {
        var rawRows = rows
            .Where(row => row.RowType == Query2ExportRowType.Raw && !IsStd(row.Row))
            .Select(row => row.Row)
            .ToArray();
        var updates = new List<MfgLotQcUpdate>();

        foreach (var ppbRow in rows.Where(row => row.RowType == Query2ExportRowType.Ppb).Select(row => row.Row))
        {
            var sourceRawRows = rawRows
                .Where(row =>
                    string.Equals(row.LotNo, ppbRow.LotNo, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Port, ppbRow.Port, StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.AnlzTime)
                .ThenBy(row => row.SampleNo)
                .ThenBy(row => row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.DataFilename, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var rf = string.IsNullOrWhiteSpace(rfId) ? null : new QcDataRow { Id = rfId };
            var update = Evaluate(ppbRow, sourceRawRows, rf, settings);
            if (update is not null)
            {
                updates.Add(update);
            }
        }

        return updates;
    }

    public IReadOnlyList<QcParameterWarningDto> BuildParameterWarnings(
        IReadOnlyList<Query2ExportRow> rows,
        QcResultSettingsDto settings)
    {
        var warnings = new List<QcParameterWarningDto>();
        var usedContainers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownContainerRows = new List<string>();

        foreach (var ppbRow in rows.Where(row => row.RowType == Query2ExportRowType.Ppb).Select(row => row.Row))
        {
            var containerType = ResolveContainerType(ppbRow.Container);
            if (containerType is null)
            {
                unknownContainerRows.Add(FormatPpbRowLabel(ppbRow));
                continue;
            }

            usedContainers.Add(containerType);
        }

        if (unknownContainerRows.Count > 0)
        {
            warnings.Add(new QcParameterWarningDto(
                "UnknownContainer",
                $"有 PPB 資料無法辨識 Container，QC 判定會略過這些資料：{string.Join("、", unknownContainerRows)}"));
        }

        foreach (var containerType in usedContainers)
        {
            AddPressureParameterWarnings(warnings, containerType, settings);
            AddConcentrationParameterWarnings(warnings, containerType, settings);
        }

        return warnings;
    }

    private static IReadOnlyList<string> EvaluateConcentrations(
        QcDataRow ppbRow,
        string? containerType,
        QcResultSettingsDto settings)
    {
        if (containerType is null)
        {
            return [];
        }

        var rules = settings.ConcentrationRules
            .Where(rule => rule.IsActive)
            .ToDictionary(rule => rule.AnalyteKey, StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();

        foreach (var analyte in CompoundMap.Analytes)
        {
            if (!rules.TryGetValue(analyte.Suffix, out var rule))
            {
                continue;
            }

            var (min, max) = string.Equals(containerType, QcResultSettingRules.Container05, StringComparison.OrdinalIgnoreCase)
                ? (rule.Min05, rule.Max05)
                : (rule.Min1L, rule.Max1L);

            if (!min.HasValue && !max.HasValue)
            {
                continue;
            }

            if (!ppbRow.Areas.TryGetValue(analyte.Suffix, out var value) || !value.HasValue)
            {
                failed.Add(rule.AnalyteName);
                continue;
            }

            if ((min.HasValue && value.Value < min.Value) ||
                (max.HasValue && value.Value > max.Value))
            {
                failed.Add(rule.AnalyteName);
            }
        }

        return failed;
    }

    private static bool EvaluatePressure(
        QcPressureReading reading,
        string? containerType,
        QcResultSettingsDto settings)
    {
        if (containerType is null)
        {
            return false;
        }

        var rule = settings.PressureRules.FirstOrDefault(rule =>
            rule.IsActive &&
            string.Equals(rule.ContainerType, containerType, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            return false;
        }

        return IsBelowRequiredMin(reading.IniPrs, rule.IniPrsMin) ||
            IsBelowRequiredMin(reading.FnlPrs, rule.FnlPrsMin);
    }

    private static bool IsBelowRequiredMin(decimal? value, decimal? min) =>
        min.HasValue && (!value.HasValue || value.Value < min.Value);

    private static void AddPressureParameterWarnings(
        List<QcParameterWarningDto> warnings,
        string containerType,
        QcResultSettingsDto settings)
    {
        var rule = settings.PressureRules.FirstOrDefault(rule =>
            rule.IsActive &&
            string.Equals(rule.ContainerType, containerType, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            warnings.Add(new QcParameterWarningDto(
                "PressureRuleMissing",
                $"{containerType} 壓力設定未建立",
                containerType));
            return;
        }

        var missing = new List<string>();
        if (!rule.IniPrsMin.HasValue)
        {
            missing.Add("分析前壓力");
        }

        if (!rule.FnlPrsMin.HasValue)
        {
            missing.Add("分析後壓力");
        }

        if (missing.Count > 0)
        {
            warnings.Add(new QcParameterWarningDto(
                "PressureMinMissing",
                $"{containerType} 壓力下限未完整設定：{string.Join("、", missing)}",
                containerType));
        }
    }

    private static void AddConcentrationParameterWarnings(
        List<QcParameterWarningDto> warnings,
        string containerType,
        QcResultSettingsDto settings)
    {
        var rules = settings.ConcentrationRules
            .Where(rule => rule.IsActive)
            .GroupBy(rule => rule.AnalyteKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var missingMin = new List<string>();
        var missingMax = new List<string>();

        foreach (var analyte in CompoundMap.Analytes)
        {
            if (!rules.TryGetValue(analyte.Suffix, out var rule))
            {
                missingMin.Add(analyte.QuantName);
                missingMax.Add(analyte.QuantName);
                continue;
            }

            var (min, max) = string.Equals(containerType, QcResultSettingRules.Container05, StringComparison.OrdinalIgnoreCase)
                ? (rule.Min05, rule.Max05)
                : (rule.Min1L, rule.Max1L);
            var analyteName = string.IsNullOrWhiteSpace(rule.AnalyteName) ? analyte.QuantName : rule.AnalyteName.Trim();

            if (!min.HasValue)
            {
                missingMin.Add(analyteName);
            }

            if (!max.HasValue)
            {
                missingMax.Add(analyteName);
            }
        }

        if (missingMin.Count > 0)
        {
            warnings.Add(new QcParameterWarningDto(
                "ConcentrationMinMissing",
                $"{containerType} 濃度 MIN 未設定：{string.Join("、", missingMin)}",
                containerType));
        }

        if (missingMax.Count > 0)
        {
            warnings.Add(new QcParameterWarningDto(
                "ConcentrationMaxMissing",
                $"{containerType} 濃度 MAX 未設定：{string.Join("、", missingMax)}",
                containerType));
        }
    }

    private static QcPressureReading ResolvePressureReading(
        IReadOnlyList<QcDataRow> portRawRows,
        QcDataRow ppbRow)
    {
        var firstRaw = portRawRows
            .OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.SampleNo)
            .ThenBy(row => row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DataFilename, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var description = firstRaw?.Description ?? ppbRow.Description;
        if (TryParsePressure(description, out var iniPrs, out var fnlPrs, out var iniPrsText, out var fnlPrsText))
        {
            return new QcPressureReading(iniPrs, fnlPrs, iniPrsText, fnlPrsText);
        }

        return new QcPressureReading(null, null, null, null);
    }

    private static bool TryParsePressure(
        string? value,
        out decimal? iniPrs,
        out decimal? fnlPrs,
        out string? iniPrsText,
        out string? fnlPrsText)
    {
        iniPrs = null;
        fnlPrs = null;
        iniPrsText = null;
        fnlPrsText = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = ArrowBetweenPressureRegex().Match(value);
        if (!match.Success)
        {
            match = ArrowAfterPressureRegex().Match(value);
        }

        if (!match.Success)
        {
            return false;
        }

        iniPrsText = match.Groups["ini"].Value;
        fnlPrsText = match.Groups["fnl"].Value;
        iniPrs = ParseDecimal(iniPrsText);
        fnlPrs = ParseDecimal(fnlPrsText);
        return iniPrs.HasValue || fnlPrs.HasValue;
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string? ResolveContainerType(string? container)
    {
        try
        {
            return QcResultSettingRules.NormalizeContainerType(container);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ResolveProdOrder(string? lotNo)
    {
        var normalized = NullIfWhiteSpace(lotNo);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Length <= 3 ? normalized : normalized[^3..];
    }

    private static string? ResolveCalId(QcDataRow row)
    {
        if (row.Si0Id is { } si0Id and > 0)
        {
            return $"ppb({si0Id.ToString(CultureInfo.InvariantCulture)})";
        }

        return NullIfWhiteSpace(row.Id);
    }

    private static string FormatPpbRowLabel(QcDataRow row)
    {
        var parts = new[]
        {
            NullIfWhiteSpace(row.LotNo),
            NullIfWhiteSpace(row.Port),
            NullIfWhiteSpace(row.SampleName),
            NullIfWhiteSpace(row.Id)
        }.OfType<string>();

        var label = string.Join("/", parts);
        return string.IsNullOrWhiteSpace(label) ? "未命名 PPB" : label;
    }

    private static bool IsStd(QcDataRow row) =>
        string.Equals(row.SourceKind, "STD", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(row.Port, "STD", StringComparison.OrdinalIgnoreCase);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"(?<ini>\d+(?:\.\d+)?)\s*>\s*(?<fnl>\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex ArrowBetweenPressureRegex();

    [GeneratedRegex(@"(?<ini>\d+(?:\.\d+)?)\s+(?<fnl>\d+(?:\.\d+)?)\s*>", RegexOptions.Compiled)]
    private static partial Regex ArrowAfterPressureRegex();
}
