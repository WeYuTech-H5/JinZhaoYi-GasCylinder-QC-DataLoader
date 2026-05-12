using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class RfExtractorImportService(
    HttpClient httpClient,
    IDapperRepository repository,
    IOptions<SchedulerOptions> options,
    ILogger<RfExtractorImportService> logger) : IRfExtractorImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly SchedulerOptions _options = options.Value;

    public async Task<RfImportResult> ImportFromStdAsync(
        string stdRawId,
        CancellationToken cancellationToken)
    {
        var stdRow = await repository.GetStdRawByStableIdAsync(stdRawId, cancellationToken)
            ?? throw new InvalidOperationException($"STD raw row '{stdRawId}' was not found.");

        if (string.IsNullOrWhiteSpace(stdRow.LotNo))
        {
            throw new InvalidOperationException("Selected STD row does not have LotNo.");
        }

        var lotNo = stdRow.LotNo.Trim();
        var requestUri = BuildExtractorUri(lotNo);
        logger.LogInformation("Calling RF extractor: {RequestUri}", requestUri);

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"RF extractor GET failed. Status={(int)response.StatusCode} {response.StatusCode}. Body={responseText}");
        }

        var jsonPath = await FindLatestExtractorFileAsync(lotNo, cancellationToken);
        var export = await ReadExtractorExportAsync(jsonPath, cancellationToken);
        if (!string.Equals(export.LotNo, lotNo, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RF extractor JSON LotNo '{export.LotNo}' does not match selected STD LotNo '{lotNo}'.");
        }

        var (rfRow, warnings) = ToRfRow(stdRow, export);
        var rf = await repository.UpsertRfAsync(
            rfRow,
            export.ExportTime.LocalDateTime.Date,
            cancellationToken);

        return new RfImportResult(rf, jsonPath, warnings);
    }

    private Uri BuildExtractorUri(string lotNo)
    {
        var baseUri = new Uri(_options.RfExtractor.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var path = _options.RfExtractor.ExportPath.TrimStart('/');
        var builder = new UriBuilder(new Uri(baseUri, path))
        {
            Query = "LotNo=" + WebUtility.UrlEncode(lotNo)
        };
        return builder.Uri;
    }

    private async Task<string> FindLatestExtractorFileAsync(string lotNo, CancellationToken cancellationToken)
    {
        var directory = _options.RfExtractor.OutputDirectory;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"RF extractor output directory was not found: {directory}");
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var file = Directory
                .EnumerateFiles(directory, $"*{lotNo}*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(fileInfo => fileInfo.Exists)
                .OrderByDescending(fileInfo => fileInfo.LastWriteTimeUtc)
                .FirstOrDefault();

            if (file is not null)
            {
                return file.FullName;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        throw new FileNotFoundException(
            $"RF extractor did not create a JSON file containing LotNo '{lotNo}' in {directory}.");
    }

    private static async Task<RfExtractorExport> ReadExtractorExportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var export = await JsonSerializer.DeserializeAsync<RfExtractorExport>(
            stream,
            JsonOptions,
            cancellationToken);

        if (export is null)
        {
            throw new InvalidOperationException($"RF extractor JSON is empty: {path}");
        }

        if (string.IsNullOrWhiteSpace(export.LotNo))
        {
            throw new InvalidOperationException($"RF extractor JSON missing lotNo: {path}");
        }

        if (export.Sid <= 0)
        {
            throw new InvalidOperationException($"RF extractor JSON missing valid sID: {path}");
        }

        var nullValueItems = export.Data
            .Where(item => !item.Value.HasValue)
            .Select(item => string.IsNullOrWhiteSpace(item.PrimeName)
                ? $"seq {item.Seq}"
                : $"{item.PrimeName} (seq {item.Seq})")
            .ToArray();
        if (nullValueItems.Length > 0)
        {
            throw new InvalidOperationException(
                "RF extractor JSON contains null Data.Value: " + string.Join(", ", nullValueItems) + ".");
        }

        return export;
    }

    private (QcDataRow Row, IReadOnlyList<string> Warnings) ToRfRow(QcDataRow stdRow, RfExtractorExport export)
    {
        var row = stdRow.CloneMetadata();
        row.Id = $"RF,ppb({export.Sid.ToString(CultureInfo.InvariantCulture)})";
        row.AnlzTime = export.ExportTime.LocalDateTime;
        row.Port = "STD";
        row.SourceKind = "Rf";
        row.Si0Id = export.Sid;
        row.LotNo = export.LotNo;
        row.Ppbs.Clear();
        row.RetentionTimes.Clear();
        row.Areas.Clear();
        row.CreateUser = _options.CreateUser;
        row.CreateTime = DateTime.Now;
        row.EditUser = _options.CreateUser;
        row.EditTime = DateTime.Now;

        var warnings = new List<string>();
        foreach (var item in export.Data)
        {
            if (!TryGetAnalyte(item.PrimeName, out var analyte))
            {
                warnings.Add($"Unknown RF compound '{item.PrimeName}' (seq {item.Seq}).");
                continue;
            }

            row.Areas[analyte.Suffix] = item.Value;
        }

        return (row, warnings);
    }

    private static bool TryGetAnalyte(string? primeName, out AnalyteDefinition analyte)
    {
        if (!string.IsNullOrWhiteSpace(primeName) &&
            CompoundMap.TryGetByQuantName(primeName, out analyte))
        {
            return true;
        }

        var normalized = primeName?
            .Replace("ChlorcIde", "Chloride", StringComparison.OrdinalIgnoreCase)
            .Replace("Chlorcide", "Chloride", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(normalized) &&
            CompoundMap.TryGetByQuantName(normalized, out analyte))
        {
            return true;
        }

        analyte = default!;
        return false;
    }

    private sealed class RfExtractorExport
    {
        [JsonPropertyName("lotNo")]
        public string LotNo { get; set; } = string.Empty;

        [JsonPropertyName("sID")]
        public int Sid { get; set; }

        [JsonPropertyName("exportTime")]
        public DateTimeOffset ExportTime { get; set; }

        [JsonPropertyName("data")]
        public List<RfExtractorDataItem> Data { get; set; } = [];
    }

    private sealed class RfExtractorDataItem
    {
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("primeName")]
        public string? PrimeName { get; set; }

        [JsonPropertyName("value")]
        public decimal? Value { get; set; }
    }
}
