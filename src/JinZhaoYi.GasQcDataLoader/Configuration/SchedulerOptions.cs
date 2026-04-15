namespace JinZhaoYi.GasQcDataLoader.Configuration;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    // 每日資料夾根目錄，例如 ...\GAS，底下會有 20251119 這種日期資料夾。
    public string WatchRoot { get; init; } = @"C:\Users\Andy\Downloads\download_2026-04-14_17-26-38\GAS";

    // 常駐服務模式下每隔幾秒掃描一次。
    public int IntervalSeconds { get; init; } = 60;

    // 輪詢最短間隔，避免設定錯誤造成忙迴圈。
    public int MinimumIntervalSeconds { get; init; } = 5;

    // 資料夾最後修改時間需要穩定幾分鐘，避免檔案還在複製時就開始解析。
    public int StableFolderMinutes { get; init; } = 3;

    // 寫入 DB 的 Inst 欄位。
    public string InstrumentName { get; init; } = "QC-01";

    // 寫入 DB 的 SampleType 欄位。
    public string SampleType { get; init; } = "TO14C1";

    // true 時只解析、驗證、計算預計筆數，不寫入 DB。
    public bool DryRun { get; init; } = true;

    // true 時只跑一輪掃描後結束，適合手動測試或排程執行。
    public bool RunOnce { get; init; }

    // 成功處理後，將 .D 資料夾搬到日期資料夾底下的 Done 目錄。
    public bool MoveProcessedFilesToDone { get; init; } = true;

    // 已處理檔案的目的地資料夾名稱，例如 yyyyMMdd\Done。
    public string DoneFolderName { get; init; } = "Done";

    // Windows Service 顯示名稱。
    public string ServiceName { get; init; } = "JinZhaoYi Gas QC DataLoader";

    // 連線字串名稱；實際值放在 ConnectionStrings:{ConnectionStringName}。
    public string ConnectionStringName { get; init; } = "Connection";

    // 寫入 DB 的 CREATE_USER。
    public string CreateUser { get; init; } = "Andy";

    // 所有 DB 表名集中設定，避免散落在 repository 中。
    public SchedulerTableOptions Tables { get; init; } = new();
}

public sealed class SchedulerTableOptions
{
    public string MfgLot { get; init; } = "ZZ_NF_GAS_MFG_LOT";

    public string Rf { get; init; } = "ZZ_NF_GAS_QC_RF";

    public string StdRaw { get; init; } = "ZZ_NF_GAS_QC_LOT_STD";

    public string StdAvg { get; init; } = "ZZ_NF_GAS_QC_LOT_STD_AVG";

    public string StdRpd { get; init; } = "ZZ_NF_GAS_QC_LOT_STD_RPD";

    public string PortRaw { get; init; } = "ZZ_NF_GAS_QC_LOT_PORT";

    public string PortAvg { get; init; } = "ZZ_NF_GAS_QC_LOT_PORT_AVG";

    public string PortPpb { get; init; } = "ZZ_NF_GAS_QC_LOT_PORT_PPB";

    public string PortRpd { get; init; } = "ZZ_NF_GAS_QC_LOT_PORT_RPD";
}
