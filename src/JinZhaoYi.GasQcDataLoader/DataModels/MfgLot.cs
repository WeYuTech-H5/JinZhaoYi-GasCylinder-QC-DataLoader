namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed class MfgLot
{
    public decimal Id { get; set; }

    public string LotNo { get; set; } = string.Empty;

    public int? Si0Id { get; set; }

    public string? SampleName { get; set; }

    public string? SampleNo { get; set; }

    public string? SampleType { get; set; }

    public string? Container { get; set; }

    public string? EMVolts { get; set; }

    public string? RelativeEM { get; set; }
}
