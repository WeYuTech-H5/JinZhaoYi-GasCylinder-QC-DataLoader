namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record QcPressureRule(
    string ContainerType,
    decimal? IniPrsMin,
    decimal? FnlPrsMin,
    bool IsActive);

public sealed record QcPressureRuleDto(
    string ContainerType,
    decimal? IniPrsMin,
    decimal? FnlPrsMin,
    bool IsActive = true);

public sealed record QcConcentrationRuleValue(
    string ContainerType,
    string AnalyteKey,
    decimal? MinPpb,
    decimal? MaxPpb,
    int SortOrder,
    bool IsActive);

public sealed record QcConcentrationRuleDto(
    string AnalyteKey,
    string AnalyteName,
    int SortOrder,
    decimal? Min05,
    decimal? Max05,
    decimal? Min1L,
    decimal? Max1L,
    bool IsActive = true);

public sealed class QcResultSettingsDto
{
    public IReadOnlyList<QcPressureRuleDto> PressureRules { get; init; } = [];

    public IReadOnlyList<QcConcentrationRuleDto> ConcentrationRules { get; init; } = [];
}

public sealed record QcParameterWarningDto(
    string Code,
    string Message,
    string? ContainerType = null);

public sealed class QcResultSettingsUpsertRequest
{
    public IReadOnlyList<QcPressureRuleDto> PressureRules { get; init; } = [];

    public IReadOnlyList<QcConcentrationRuleDto> ConcentrationRules { get; init; } = [];
}

public static class QcResultSettingRules
{
    public const string Container05 = "0.5L";
    public const string Container1L = "1L";

    public static readonly IReadOnlyList<string> SupportedContainers = [Container05, Container1L];

    public static string NormalizeContainerType(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("containerType is required.");
        }

        if (text.Contains("0.5", StringComparison.OrdinalIgnoreCase))
        {
            return Container05;
        }

        if (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("1L", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("1L", StringComparison.OrdinalIgnoreCase))
        {
            return Container1L;
        }

        throw new InvalidOperationException($"Unsupported containerType '{value}'. Use '0.5L' or '1L'.");
    }

    public static QcPressureRule NormalizePressureRule(QcPressureRuleDto rule)
    {
        var containerType = NormalizeContainerType(rule.ContainerType);
        RequireNonNegative(rule.IniPrsMin, $"{containerType} iniPrsMin");
        RequireNonNegative(rule.FnlPrsMin, $"{containerType} fnlPrsMin");
        return new QcPressureRule(containerType, rule.IniPrsMin, rule.FnlPrsMin, rule.IsActive);
    }

    public static IReadOnlyList<QcConcentrationRuleValue> NormalizeConcentrationRule(
        QcConcentrationRuleDto rule,
        IReadOnlyDictionary<string, AnalyteDefinition> analytesByKey)
    {
        var analyteKey = rule.AnalyteKey?.Trim();
        if (string.IsNullOrWhiteSpace(analyteKey))
        {
            throw new InvalidOperationException("analyteKey is required.");
        }

        if (!analytesByKey.TryGetValue(analyteKey, out var analyte))
        {
            throw new InvalidOperationException($"Unsupported analyteKey '{rule.AnalyteKey}'.");
        }

        ValidateRange(rule.Min05, rule.Max05, $"{analyte.Suffix} 0.5L");
        ValidateRange(rule.Min1L, rule.Max1L, $"{analyte.Suffix} 1L");
        var sortOrder = rule.SortOrder <= 0 ? ResolveDefaultSortOrder(analyte.Suffix) : rule.SortOrder;

        return
        [
            new QcConcentrationRuleValue(Container05, analyte.Suffix, rule.Min05, rule.Max05, sortOrder, rule.IsActive),
            new QcConcentrationRuleValue(Container1L, analyte.Suffix, rule.Min1L, rule.Max1L, sortOrder, rule.IsActive)
        ];
    }

    public static IReadOnlyDictionary<string, AnalyteDefinition> BuildAnalyteLookup() =>
        CompoundMap.Analytes.ToDictionary(analyte => analyte.Suffix, StringComparer.OrdinalIgnoreCase);

    private static void ValidateRange(decimal? min, decimal? max, string label)
    {
        RequireNonNegative(min, $"{label} minPpb");
        RequireNonNegative(max, $"{label} maxPpb");

        if (min.HasValue && max.HasValue && min.Value > max.Value)
        {
            throw new InvalidOperationException($"{label} minPpb must be less than or equal to maxPpb.");
        }
    }

    private static void RequireNonNegative(decimal? value, string label)
    {
        if (value.HasValue && value.Value < 0)
        {
            throw new InvalidOperationException($"{label} must be greater than or equal to 0.");
        }
    }

    private static int ResolveDefaultSortOrder(string analyteKey)
    {
        var index = CompoundMap.Analytes
            .Select((analyte, position) => new { analyte.Suffix, Position = position + 1 })
            .FirstOrDefault(item => string.Equals(item.Suffix, analyteKey, StringComparison.OrdinalIgnoreCase));

        return index?.Position ?? 999;
    }
}
