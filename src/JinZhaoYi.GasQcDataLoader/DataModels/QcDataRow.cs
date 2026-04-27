namespace JinZhaoYi.GasQcDataLoader.DataModels;

// 代表一筆準備寫入 Gas QC DB 的資料。
// raw / AVG / RPD / PPB 都使用同一個模型，差異由 Areas、Ppbs、RetentionTimes 是否有值決定。
public sealed class QcDataRow
{
    public decimal? Sid { get; set; }

    public string? Id { get; set; }

    public DateTime? AnlzTime { get; set; }

    public string? Inst { get; set; }

    public string? Port { get; set; }

    public string? Si0Id { get; set; }

    public int? SampleNo { get; set; }

    public string? LotNo { get; set; }

    public string? DataFilename { get; set; }

    public string? DataFilepath { get; set; }

    public string? PcName { get; set; }

    public string? Container { get; set; }

    public string? Description { get; set; }

    public string? EmVolts { get; set; }

    public string? RelativeEm { get; set; }

    public string? SampleName { get; set; }

    public string? SampleType { get; set; }

    public string? Id1 { get; set; }

    public string? Id2 { get; set; }

    public string? CreateUser { get; set; }

    public DateTime? CreateTime { get; set; }

    public string? EditUser { get; set; }

    public DateTime? EditTime { get; set; }

    public Dictionary<string, decimal?> Areas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal?> Ppbs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal?> RetentionTimes { get; } = new(StringComparer.OrdinalIgnoreCase);

    // 計算列會沿用 raw row 的 LOT、Port、Sample 等欄位，再覆寫 ID 與計算結果。
    public QcDataRow CloneMetadata()
    {
        return new QcDataRow
        {
            Sid = Sid,
            Id = Id,
            AnlzTime = AnlzTime,
            Inst = Inst,
            Port = Port,
            Si0Id = Si0Id,
            SampleNo = SampleNo,
            LotNo = LotNo,
            DataFilename = DataFilename,
            DataFilepath = DataFilepath,
            PcName = PcName,
            Container = Container,
            Description = Description,
            EmVolts = EmVolts,
            RelativeEm = RelativeEm,
            SampleName = SampleName,
            SampleType = SampleType,
            Id1 = Id1,
            Id2 = Id2,
            CreateUser = CreateUser,
            CreateTime = CreateTime,
            EditUser = EditUser,
            EditTime = EditTime
        };
    }

    public QcDataRow DeepClone()
    {
        var clone = CloneMetadata();

        foreach (var (key, value) in Areas)
        {
            clone.Areas[key] = value;
        }

        foreach (var (key, value) in Ppbs)
        {
            clone.Ppbs[key] = value;
        }

        foreach (var (key, value) in RetentionTimes)
        {
            clone.RetentionTimes[key] = value;
        }

        return clone;
    }
}
