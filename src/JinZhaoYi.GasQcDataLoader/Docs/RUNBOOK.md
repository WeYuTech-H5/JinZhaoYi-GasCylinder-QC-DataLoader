# 操作手冊

本文件說明如何安全啟動、設定與維運 GAS QC DataLoader。資料流請先看[現行架構](ARCHITECTURE.md)。

## 執行環境

| 項目 | 要求 |
| --- | --- |
| 作業系統 | Windows；程式可作為 console process 或 Windows Service 執行。 |
| 建置 SDK | `global.json` 指定 `.NET SDK 9.0.302`。 |
| 目標框架 | `net8.0`。SDK 版本與目標框架是兩件事。 |
| 資料庫 | SQL Server，執行帳號需具備實際流程使用的查詢、insert、update 權限。 |
| 檔案權限 | 需讀取 GAS 來源、讀寫 state／匯出／Log 路徑；使用網路分享時，Windows Service 帳號也要有 UNC 權限。 |
| Excel 模板 | Query2、COA 大卡、COA 小卡模板必須存在且可讀。 |
| PDF | 建議安裝 LibreOffice 並設定 `Scheduler:CoaExport:LibreOfficePath`。 |
| RF Extractor | 使用「從 STD 匯入 RF」功能時，必須可連線到設定的外部 RF Extractor API。 |

## 設定載入順序

ASP.NET Core 後載入的來源會覆寫先前來源。常用方式如下：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. 環境變數
4. command-line 參數

環境變數使用雙底線表示巢狀設定，例如：

```powershell
$env:ConnectionStrings__Connection = "Server=...;Database=...;..."
$env:Scheduler__DryRun = "true"
$env:Scheduler__WatchRoot = "C:\temp\gas-qc-local\input"
```

不要將密碼、正式 connection string 或 API secret 提交到 Git。`appsettings.Local.json` 與 `appsettings.Development.json` 已列在 `.gitignore`，可用來保存個人環境覆寫；檔名必須配合環境名稱，例如使用 `appsettings.Local.json` 前先設定 `$env:DOTNET_ENVIRONMENT = "Local"`。正式環境建議使用受控的環境變數或部署設定。

## 啟動前檢查

依序確認：

1. `dotnet --version` 可解析 `global.json` 要求的 SDK。
2. SQL Server 可連線，必要資料表與增量 SQL 已準備完成。
3. `WatchRoot` 存在，服務帳號能讀取其中檔案。
4. `ProcessedStatePath` 的父目錄可寫入。
5. `ExportRoot`、模板與 LibreOffice 路徑存在。
6. 若啟用 MFG JSON，watch directory 與 state directory 可存取。
7. 先確認 `DryRun`、`RunOnce`、`TargetMode`、`DownloadApi:Enabled` 是否符合這次目的。

> 目前版本庫中的 `appsettings.json` 是正式環境風格，`DryRun=false`。第一次執行前一定要使用本機覆寫或 command-line 參數。

## 常用啟動模式

### 安全驗證一輪後結束

此模式會掃描、解析、查 DB 驗證，但不寫 GAS QC tables、不更新 processed state、不搬檔：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- `
  --Scheduler:WatchRoot=C:\temp\gas-qc-local\input `
  --Scheduler:ProcessedStatePath=C:\temp\gas-qc-local\state\processed-quant-files.json `
  --Scheduler:DryRun=true `
  --Scheduler:RunOnce=true `
  --Scheduler:MoveProcessedFilesToDone=false `
  --Scheduler:MfgJsonImport:Enabled=false `
  --Scheduler:DownloadApi:Enabled=false
```

### 啟動網頁與 API

Quant worker 仍會啟動。以下做法讓它以 DryRun 跑一輪後停止，但 process 保留 API：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- `
  --urls=http://localhost:5080 `
  --Scheduler:WatchRoot=C:\temp\gas-qc-local\input `
  --Scheduler:ProcessedStatePath=C:\temp\gas-qc-local\state\processed-quant-files.json `
  --Scheduler:DryRun=true `
  --Scheduler:RunOnce=true `
  --Scheduler:MfgJsonImport:Enabled=false `
  --Scheduler:DownloadApi:Enabled=true
```

開啟 <http://localhost:5080>。當 `RunOnce=true` 且 API 啟用時，Quant worker 跑完一輪只會結束自身，不會停止整個 Web process。

### 正式執行指定日期

下列命令會寫 DB。先以同一組參數執行 DryRun，再將 `DryRun` 改成 `false`：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- `
  --Scheduler:RunOnce=true `
  --Scheduler:TargetMode=TargetDate `
  --Scheduler:BackfillEnabled=true `
  --Scheduler:BackfillTargetDate=20260813 `
  --Scheduler:StableFolderMinutes=0 `
  --Scheduler:DryRun=false `
  --Scheduler:DownloadApi:Enabled=false `
  --Scheduler:MfgJsonImport:Enabled=false
```

### 持續掃描所有新穩定檔案

```text
Scheduler:RunOnce = false
Scheduler:UseDailySchedule = false
Scheduler:TargetMode = AllNewStableFiles
```

每輪完成後會等待 `max(MinimumIntervalSeconds, IntervalSeconds)` 再掃描。

### 每日固定時間執行

```text
Scheduler:RunOnce = false
Scheduler:UseDailySchedule = true
Scheduler:DailyWakeUpTime = 02:00
```

時間格式必須是 `HH:mm`。每日模式不使用一般 interval 作為兩輪間隔。

## 關鍵設定

### Quant worker

| Setting | 說明 |
| --- | --- |
| `Scheduler:WatchRoot` | GAS 掃描根目錄。 |
| `Scheduler:StableFolderMinutes` | `.D` folder 最後變動後需等待多久才視為穩定；最小可為 0。 |
| `Scheduler:TargetMode` | `TargetDate` 只處理目標日期；`AllNewStableFiles` 依 state 處理所有新穩定資料。 |
| `Scheduler:DryRun` | `true`：解析與 DB 驗證，但不寫 GAS QC tables、不更新 state、不搬檔。 |
| `Scheduler:RunOnce` | Quant worker 是否只執行一輪。API 啟用時 process 不會因此停止。 |
| `Scheduler:IntervalSeconds` | 非每日模式的輪詢間隔。 |
| `Scheduler:MinimumIntervalSeconds` | 輪詢間隔下限，程式預設 5 秒。 |
| `Scheduler:UseDailySchedule` | 是否改成每日固定時間執行。 |
| `Scheduler:DailyWakeUpTime` | 每日執行時間，格式 `HH:mm`。 |
| `Scheduler:NormalTargetDayOffset` | 非 backfill 的目標日相對今天偏移，預設 `-1`。 |
| `Scheduler:BackfillEnabled` | 是否覆寫成指定日期。 |
| `Scheduler:BackfillTargetDate` | backfill 日期，格式 `yyyyMMdd`。 |
| `Scheduler:ProcessedStatePath` | processed Quant state JSON。未設定時位於 `ExportRoot` 或 `WatchRoot` 下。 |
| `Scheduler:MoveProcessedFilesToDone` | 正式成功後是否搬到 archive。 |
| `Scheduler:DoneFolderName` | archive 資料夾名稱，預設 `archive`。 |

### API 與匯出

| Setting | 說明 |
| --- | --- |
| `Scheduler:DownloadApi:Enabled` | 是否註冊全部 `/api/*` endpoints。 |
| `Scheduler:ExportRoot` | 匯出與下載根目錄；未設定時依 `WatchRoot` 或來源日期資料夾決定。 |
| `Scheduler:ExcelExport:Enabled` | 是否允許產生 Query2 Excel。 |
| `Scheduler:ExcelExport:TemplatePath` | Query2 Excel 模板。啟用 Excel 匯出時必填且檔案必須存在。 |
| `Scheduler:CsvExport:Enabled` | 舊匯入流程自動 CSV 的開關；API 匯出仍由 endpoint 驅動。 |
| `Scheduler:CoaExport:LargeTemplatePath` | COA 大卡模板。 |
| `Scheduler:CoaExport:SmallTemplatePath` | COA 小卡模板。 |
| `Scheduler:CoaExport:LibreOfficePath` | `soffice.com` 或 `soffice.exe` 路徑。 |
| `Scheduler:CoaExport:UseBasicPdfFallback` | LibreOffice 不可用時是否允許基本 PDF fallback。版面可能與正式模板不同。 |
| `Scheduler:QcResultWriteback:OnQuantImport` | 舊 Quant 匯入回寫開關；目前 raw-only 流程不會執行。 |

### MFG JSON

| Setting | 說明 |
| --- | --- |
| `Scheduler:MfgJsonImport:Enabled` | 是否啟動 MFG JSON worker。 |
| `WatchDirectory` | JSON 掃描資料夾，只掃最上層。 |
| `FilePattern` | 預設 `MFGExport_*.json`。 |
| `PollIntervalSeconds` | 掃描週期，程式至少使用 5 秒。 |
| `StableFileSeconds` | 檔案多久未變動才開始處理，程式至少使用 1 秒。 |
| `StateFilePath` | MFG JSON 獨立 state JSON。 |

### RF Extractor

| Setting | 說明 |
| --- | --- |
| `Scheduler:RfExtractor:BaseUrl` | 外部 RF Extractor host。 |
| `Scheduler:RfExtractor:ExportPath` | 匯出 endpoint，預設 `/api/Export`。 |
| `Scheduler:RfExtractor:OutputDirectory` | RF Extractor 回傳檔案的保存路徑。 |

## Log 在哪裡

開發環境通常位於專案下的 `src/JinZhaoYi.GasQcDataLoader/LOG`；發布後位於執行檔旁的 `LOG`。若無權寫入，會退回 `%ProgramData%\{ApplicationName}\LOG`。

| 路徑 | 用途 |
| --- | --- |
| `LOG/Application/app-YYYYMMDD.log` | 一般啟動、API 與未分類錯誤。 |
| `LOG/Sync/cycle-YYYYMMDD.log` | Quant worker 等待、每輪統計與整體失敗。 |
| `LOG/Sync/import-YYYYMMDD.log` | Quant 驗證與匯入流程。 |
| `LOG/Sync/state-YYYYMMDD.log` | processed state 比對與更新。 |
| `LOG/DataRead/scanner-YYYYMMDD.log` | 資料夾與候選檔掃描。 |
| `LOG/DataRead/quant-YYYYMMDD.log` | Quant／acqmeth 解析。 |
| `LOG/MFGJSON/scan-YYYYMMDD.log` | MFG JSON 掃描與 upsert。 |
| `LOG/serilog-selflog.txt` | Serilog 自身寫檔錯誤。 |

建議排查順序：

1. 服務是否啟動：先看 Application log。
2. 為何沒有跑 Quant：看 Sync cycle，再看 DataRead scanner。
3. Quant 為何解析或驗證失敗：看 DataRead quant，再看 Sync import。
4. 明明成功卻再次處理：看 Sync state 與 `ProcessedStatePath`。
5. MFG JSON 問題：直接看 MFGJSON scan。

## 建置與發布

```powershell
dotnet restore .\JinZhaoYi.GasQcDataLoader.sln
dotnet test .\JinZhaoYi.GasQcDataLoader.sln
dotnet publish .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o C:\temp\publish\JinZhaoYi.GasQcDataLoader
```

發布資料夾應包含執行檔、`appsettings.json`、`wwwroot` 與 `templates`。正式部署前把環境設定放到受控位置，不要直接沿用開發者電腦的絕對路徑。

## Windows Service

以下動作需要系統管理員權限；服務名稱可依環境調整：

```powershell
New-Service `
  -Name "JinZhaoYiGasQcDataLoader" `
  -DisplayName "JinZhaoYi Gas QC DataLoader" `
  -BinaryPathName '"C:\Services\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.exe"' `
  -StartupType Automatic

Start-Service -Name "JinZhaoYiGasQcDataLoader"
Get-Service -Name "JinZhaoYiGasQcDataLoader"
```

服務帳號至少需要：

- 登入 SQL Server 的權限。
- GAS 與 MFG JSON 來源路徑的讀取權限。
- state、export 與 Log 路徑的寫入權限。
- 若來源是 UNC 分享，不能只驗證互動式登入使用者；要以實際服務帳號測試。

更新服務時，先停止服務、備份現行發布目錄與設定，再替換檔案並重新啟動。不要覆寫唯一一份正式設定。

## 常見問題

### `A compatible .NET SDK was not found`

`global.json` 要求 `9.0.302`。使用 `dotnet --list-sdks` 確認；只有 .NET 8 SDK 不足以通過 SDK resolver，即使專案目標框架是 `net8.0`。

### 網頁有開，但 API 都是 404

確認 `Scheduler:DownloadApi:Enabled=true`。靜態頁面與 API 的啟用條件不同。

### `RunOnce=true` 後程式沒有離開

若 `DownloadApi:Enabled=true`，Quant worker 只會停止自己，Web process 會繼續服務。需要命令執行完自動離開時，將 API 關閉。

### 沒有掃到 Quant

依序確認 `WatchRoot`、`TargetMode`、目標日期、`StableFolderMinutes`、processed state，以及服務帳號的網路分享權限。

### Query2 匯出失敗

確認 Excel 匯出已啟用、模板存在、已選 RF／STD raw／PORT raw，並檢查 QC 是否出現「未判定」。詳細 request 規則請看 [API 參考](API.md)。

### COA Excel 成功但 PDF 失敗

確認 LibreOffice 路徑、服務帳號是否能啟動該程式，以及暫存／輸出路徑權限。Fallback 雖可避免完全失敗，但不保證與正式 Excel 模板完全相同。
