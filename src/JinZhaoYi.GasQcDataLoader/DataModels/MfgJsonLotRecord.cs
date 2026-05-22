namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class MfgJsonLotRecord
{
    public decimal Id { get; init; }

    public string Si0Id { get; init; } = string.Empty;

    public string LotNo { get; init; } = string.Empty;

    public string? SampleName { get; init; }

    public DateTime? ProdDate { get; init; }

    public string? ProdType { get; init; }

    public string? ProdOperator { get; init; }

    public int? ProdIniPrs { get; init; }

    public int? ProdLeakTest1 { get; init; }

    public int? ProdVacuumPrs { get; init; }

    public int? ProdCan1FillingPrs { get; init; }

    public int? ProdCan2FillingPrs { get; init; }

    public int? ProdBomb2FillingPrs { get; init; }

    public int? ProdBomb1FillingPrs { get; init; }

    public int? ProdBomb3FillingPrs { get; init; }

    public int? ProdLeakTest2 { get; init; }

    public string? ProdCan1LotNo { get; init; }

    public int? ProdCan1Flow { get; init; }

    public int? ProdCan1Sec { get; init; }

    public decimal? ProdCan1Prs { get; init; }

    public string? ProdCan2LotNo { get; init; }

    public int? ProdCan2Flow { get; init; }

    public int? ProdCan2Sec { get; init; }

    public int? ProdCan2Prs { get; init; }

    public string? ProdBomb2LotNo { get; init; }

    public int? ProdBomb2Flow { get; init; }

    public int? ProdBomb2Sec { get; init; }

    public int? ProdBomb2Prs { get; init; }

    public string? ProdBomb1LotNo { get; init; }

    public int? ProdBomb1SetFillingPrs { get; init; }

    public int? ProdBomb1Prs { get; init; }

    public string? ProdBomb3LotNo { get; init; }

    public int? ProdBomb3SetFillingPrs { get; init; }

    public int? ProdBomb3Prs { get; init; }

    public string? SampleNo { get; init; }

    public string? SampleType { get; init; }

    public string? Container { get; init; }

    public string? ProdOrder { get; init; }

    public string? CalType { get; init; }

    public string? CalId { get; init; }

    public string? IniPrs { get; init; }

    public string? QcComplete { get; init; }

    public string? QcInst { get; init; }

    public string? QcPort { get; init; }

    public DateTime? QcTime { get; init; }

    public string? Result { get; init; }

    public string? RfId { get; init; }

    public string? FnlPrs { get; init; }
}
