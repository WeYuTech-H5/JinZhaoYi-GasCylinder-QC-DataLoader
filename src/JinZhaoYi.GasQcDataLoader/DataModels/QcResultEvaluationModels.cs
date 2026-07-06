namespace JinZhaoYi.GasQcDataLoader.DataModels;

public static class QcResultValues
{
    public const string Pass = "Pass";

    public const string Fail = "Fail";

    public const string Unknown = "Unknown";
}

public sealed record MfgLotQcUpdate(
    string LotNo,
    int? Si0Id,
    string? ProdOrder,
    string CalType,
    string? CalId,
    string? IniPrs,
    string QcComplete,
    string QcInst,
    string? QcPort,
    DateTime? QcTime,
    string Result,
    string? RfId,
    string? FnlPrs,
    string? FailDesc);

public sealed record QcPressureReading(decimal? IniPrs, decimal? FnlPrs, string? IniPrsText, string? FnlPrsText);
