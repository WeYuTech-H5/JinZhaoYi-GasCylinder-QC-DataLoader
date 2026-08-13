namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record Query2DynamicAreaField(
    string FieldKey,
    string ColumnName,
    string DisplayName,
    int SortOrder,
    bool IsActive);

public sealed record Query2DynamicAreaFieldDto(
    string FieldKey,
    string ColumnName,
    string DisplayName,
    int SortOrder,
    bool IsActive);

public sealed class Query2DynamicAreaFieldUpsertRequest
{
    public string? ColumnName { get; set; }

    public string? DisplayName { get; set; }

    public int? SortOrder { get; set; }

    public bool? IsActive { get; set; }
}

public sealed record Query2DynamicAreaPortValue(
    string FieldKey,
    string PortKey,
    decimal? AreaValue);

public sealed record Query2DynamicAreaPortValueDto(
    string FieldKey,
    string PortKey,
    decimal? AreaValue);

public sealed class Query2DynamicAreaPortValueUpsertRequest
{
    public IReadOnlyList<Query2DynamicAreaPortValueDto> Values { get; set; } = [];
}

public static class Query2DynamicAreaRules
{
    private const string AreaPrefix = "Area_";

    public static Query2DynamicAreaField NormalizeFieldRequest(
        Query2DynamicAreaFieldUpsertRequest request,
        IReadOnlyCollection<Query2DynamicAreaField> existingFields)
    {
        var inputName = request.ColumnName?.Trim();
        if (string.IsNullOrWhiteSpace(inputName))
        {
            inputName = request.DisplayName?.Trim();
        }

        if (string.IsNullOrWhiteSpace(inputName))
        {
            throw new InvalidOperationException("columnName or displayName is required.");
        }

        var fieldKey = inputName.StartsWith(AreaPrefix, StringComparison.OrdinalIgnoreCase)
            ? inputName[AreaPrefix.Length..].Trim()
            : inputName;
        ValidateFieldKey(fieldKey);

        if (CompoundMap.Analytes.Any(analyte => string.Equals(analyte.Suffix, fieldKey, StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(analyte.AreaColumn, $"{AreaPrefix}{fieldKey}", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Dynamic AREA field '{fieldKey}' conflicts with an existing Query2 AREA column.");
        }

        var columnName = $"{AreaPrefix}{fieldKey}";
        if (existingFields.Any(field => !string.Equals(field.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(field.ColumnName, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Dynamic AREA field '{columnName}' already exists.");
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? fieldKey
            : request.DisplayName.Trim();

        return new Query2DynamicAreaField(
            fieldKey,
            columnName,
            displayName,
            request.SortOrder ?? ResolveNextSortOrder(existingFields),
            request.IsActive ?? true);
    }

    public static string NormalizePortKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("portKey is required.");
        }

        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }

    public static string NormalizeFieldKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("fieldKey is required.");
        }

        var text = value.Trim();
        return text.StartsWith(AreaPrefix, StringComparison.OrdinalIgnoreCase)
            ? text[AreaPrefix.Length..].Trim()
            : text;
    }

    private static void ValidateFieldKey(string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            throw new InvalidOperationException("Dynamic AREA field name is required.");
        }

        if (fieldKey.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dynamic AREA field name cannot contain ':'.");
        }

        if (fieldKey.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("Dynamic AREA field key cannot contain whitespace.");
        }
    }

    private static int ResolveNextSortOrder(IReadOnlyCollection<Query2DynamicAreaField> existingFields) =>
        existingFields.Count == 0 ? 1 : existingFields.Max(field => field.SortOrder) + 1;
}
