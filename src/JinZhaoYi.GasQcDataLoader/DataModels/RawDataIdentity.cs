using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record RawDataIdentity(
    string LotNo,
    string Port,
    int SampleNo,
    string? SampleName,
    DateTime AnlzTime)
{
    private static readonly Regex MfgSampleNameRegex = new(
        @"(?:^|[-_])(?:[A-Za-z]+)?(?<sampleNo>\d{1,4})(?:\D|$)",
        RegexOptions.Compiled);

    public string ToStableId()
    {
        var text = string.Join(
            "|",
            Normalize(LotNo),
            Normalize(Port),
            SampleNo.ToString(CultureInfo.InvariantCulture),
            Normalize(SampleName),
            AnlzTime.ToString("O", CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }

    public static RawDataIdentity FromRow(QcDataRow row) =>
        new(
            row.LotNo ?? string.Empty,
            row.Port ?? string.Empty,
            row.SampleNo ?? 0,
            row.SampleName,
            row.AnlzTime ?? DateTime.MinValue);

    public static RawDataIdentity FromParsed(ParsedQuantFile parsed, MfgLot lot) =>
        new(parsed.LotNo, parsed.Source.Port, ResolveSampleNo(parsed, lot), lot.SampleName, parsed.AcquiredAt);

    public static int ResolveSampleNo(ParsedQuantFile parsed, MfgLot? lot)
    {
        if (TryParsePositiveInt(lot?.SampleNo) is { } sampleNo)
        {
            return sampleNo;
        }

        if (TryParseMfgSampleName(lot?.SampleName) is { } sampleNameNo)
        {
            return sampleNameNo;
        }

        return parsed.SampleNo;
    }

    private static int? TryParsePositiveInt(string? value)
    {
        if (int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    private static int? TryParseMfgSampleName(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var match = MfgSampleNameRegex.Match(trimmed);

        return match.Success ? TryParsePositiveInt(match.Groups["sampleNo"].Value) : null;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
}
