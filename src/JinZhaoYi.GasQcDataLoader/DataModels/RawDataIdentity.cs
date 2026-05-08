using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record RawDataIdentity(
    string LotNo,
    string Port,
    int SampleNo,
    string? SampleName,
    DateTime AnlzTime)
{
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
        new(parsed.LotNo, parsed.Source.Port, parsed.SampleNo, lot.SampleName, parsed.AcquiredAt);

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
}
