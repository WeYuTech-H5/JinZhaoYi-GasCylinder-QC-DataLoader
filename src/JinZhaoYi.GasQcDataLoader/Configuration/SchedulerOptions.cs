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

    // true 時常駐服務每天只在 DailyWakeUpTime 醒來執行一次；false 時沿用 IntervalSeconds 輪詢。
    public bool UseDailySchedule { get; init; }

    // 常駐每日醒來時間，格式 HH:mm，例如 02:00。
    public string DailyWakeUpTime { get; init; } = "02:00";

    // 正常模式要處理的目標日期位移；-1 代表昨天。
    public int NormalTargetDayOffset { get; init; } = -1;

    // true 時改跑 BackfillTargetDate 指定日期；false 時跑 Today + NormalTargetDayOffset。
    public bool BackfillEnabled { get; init; }

    // 補跑日期，格式 yyyyMMdd；BackfillEnabled=true 時必填。
    public string? BackfillTargetDate { get; init; }

    // 成功處理後，將 .D 資料夾搬到各 source folder 底下的 archive 目錄。
    public bool MoveProcessedFilesToDone { get; init; } = true;

    // true 時 STD_AVG / PORT_AVG 維持 snapshot table 行為；false 時保留 AVG 歷史紀錄。
    public bool UseAverageSnapshotTables { get; init; } = true;

    // 已處理檔案的目的地資料夾名稱，例如 PORT 5\archive。
    public string DoneFolderName { get; init; } = "archive";

    // Query2 Excel 匯出設定。
    public SchedulerExcelExportOptions ExcelExport { get; init; } = new();

    // TO14C PPB CSV 匯出設定。
    public SchedulerCsvExportOptions CsvExport { get; init; } = new();

    // QC 檔案下載 API 設定。
    public SchedulerDownloadApiOptions DownloadApi { get; init; } = new();

    // Windows Service 顯示名稱。
    public string ServiceName { get; init; } = "JinZhaoYi Gas QC DataLoader";

    // 連線字串名稱；實際值放在 ConnectionStrings:{ConnectionStringName}。
    public string ConnectionStringName { get; init; } = "Connection";

    // 寫入 DB 的 CREATE_USER。
    public string CreateUser { get; init; } = "Andy";

    // 所有 DB 表名集中設定，避免散落在 repository 中。
    public SchedulerTableOptions Tables { get; init; } = new();
}

public sealed class SchedulerExcelExportOptions
{
    // true 時輸出 Query2 Excel。
    public bool Enabled { get; init; }

    // Query2 Excel 模板路徑。
    public string? TemplatePath { get; init; }
}

public sealed class SchedulerCsvExportOptions
{
    public bool Enabled { get; init; }

    public string SchemaName { get; init; } = "L002010_TO14C";

    public string MaterialNo { get; init; } = "L002010";

    public string? CoACompletionDate { get; init; }

    public string SupplierID { get; init; } = "84170915";

    public string SupplierName { get; init; } = "金兆益科技股份有限公司";

    public string? TSMCFab { get; init; }

    public string? FabPhase { get; init; }

    public string ShipQty { get; init; } = "1";

    public string Maker { get; init; } = "New-Fast Technology Co., LTD";

    public string? DeliverDate { get; init; }

    public string? PONo { get; init; }

    public string CylinderMaterial { get; init; } = "SUS316";

    public string ValveMaterial { get; init; } = "SUS316";

    public string ValveType { get; init; } = "1/4\" VCR Female";

    public string Content { get; init; } = "950 psi";

    public string CylinderSize { get; init; } = "52 x 6 (cm)";

    public string SpecNo { get; init; } = "M-FMM-L0-03-030";

    public string SpecVersion { get; init; } = "1";

    public int ShelfLifeTime { get; init; } = 12;

    public string RawLotId { get; init; } = "CC-706988";

    public string MaterialName { get; init; } = "TO14C";

    public string WaterValue { get; init; } = "0.02";

    public string OxygenValue { get; init; } = "0.01";

    public string NitrogenValue { get; init; } = "99.9995";
}

public sealed class SchedulerDownloadApiOptions
{
    public bool Enabled { get; init; }
}

public sealed class SchedulerTableOptions
{
    public string MfgLot { get; init; } = "ZZ_NF_GAS_MFG_LOT";

    public string Rf { get; init; } = "ZZ_NF_GAS_QC_RF";

    public string StdRaw { get; init; } = "ZZ_NF_GAS_QC_LOT_STD";

    public string StdAvg { get; init; } = "ZZ_NF_GAS_QC_LOT_STD_AVG";

    public string StdQc { get; init; } = "ZZ_NF_GAS_QC_LOT_STD_QC";

    public string StdRpd { get; init; } = "ZZ_NF_GAS_QC_LOT_STD_RPD";

    public string PortRaw { get; init; } = "ZZ_NF_GAS_QC_LOT_PORT";

    public string PortAvg { get; init; } = "ZZ_NF_GAS_QC_LOT_PORT_AVG";

    public string PortPpb { get; init; } = "ZZ_NF_GAS_QC_LOT_PORT_PPB";

    public string PortRpd { get; init; } = "ZZ_NF_GAS_QC_LOT_PORT_RPD";
}
