namespace JinZhaoYi.GasQcDataLoader.DataModels;

public static class QcResultValues
{
    public const string Pass = "Pass";

    public const string Fail = "Fail";

    public const string Unknown = "Unknown";
}

public static class QcPressureResultValues
{
    public const string Pass = QcResultValues.Pass;

    public const string Fail = QcResultValues.Fail;

    public const string NotEvaluated = "未判定";
}

public sealed record QcJudgmentSnapshot(
    string? PpbId,
    DateTime? AnlzTime,
    string? LotNo,
    string? Port,
    string? Container,
    decimal? IniPrs,
    decimal? IniPrsMin,
    decimal? FnlPrs,
    decimal? FnlPrsMin,
    string PressureResult,
    string Result,
    string? FailDesc,
    bool IniPrsFailed,
    bool FnlPrsFailed);

public sealed class QcExportEvaluationBatch
{
    public IReadOnlyList<QcJudgmentSnapshot> Snapshots { get; init; } = [];

    public IReadOnlyList<MfgLotQcUpdate> Updates { get; init; } = [];
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
