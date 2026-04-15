# JinZhaoYi Gas QC DataLoader

`.NET 8 Worker Service` for importing Gas QC `Quant.txt` files into SQL Server. The application is built as `WinExe`, so it can stay resident in the background when deployed as a Windows Service. `RunOnce=true` is available for local verification.

## 專案結構

- `Configuration`: 排程設定、DB 表名、logging options。
- `DataModels`: Quant、LOT、RF 與 DB row 用到的 DTO/model。
- `Logging`: Serilog 初始化與檔案輸出設定。
- `Logs`: Serilog 檔案輸出目錄，實際 `.log` 不提交。
- `Services/Infrastructure`: SQL connection factory 與 Dapper repository。
- `Services/Interface`: 介面契約。
- `Services/Service`: parser、scanner、calculation、orchestrator、job 實作。
- `Services/Processing`: `Worker`，負責 BackgroundService 宿主與輪詢。

## 設定

主要設定在 `src/JinZhaoYi.GasQcDataLoader/appsettings.json`。

正式 DB 密碼不可提交到 GitHub。repo 內的 `appsettings.json` 只保留範例連線字串，實際開發機設定請放在被 `.gitignore` 排除的 `appsettings.Development.json`、user-secrets 或環境變數。

主要設定項：

| 設定 | 說明 |
| --- | --- |
| `ConnectionStrings:Connection` | SQL Server 連線字串，repo 內只放範例值 |
| `Scheduler:WatchRoot` | 每日資料夾根目錄，例如 `GAS` |
| `Scheduler:IntervalSeconds` | 常駐輪詢秒數 |
| `Scheduler:MinimumIntervalSeconds` | 最小輪詢秒數保護 |
| `Scheduler:StableFolderMinutes` | 資料夾最後修改時間需穩定幾分鐘後才處理 |
| `Scheduler:InstrumentName` | 寫入 `Inst`，預設 `QC-01` |
| `Scheduler:SampleType` | `ZZ_NF_GAS_MFG_LOT.SampleType` 為空時的 fallback，預設 `TO14C1` |
| `Scheduler:DryRun` | `true` 時只解析和驗證，不寫 DB、不搬移資料夾 |
| `Scheduler:RunOnce` | `true` 時跑完一輪即結束，適合手動測試 |
| `Scheduler:MoveProcessedFilesToDone` | 成功匯入後是否搬移 `.D` 資料夾到 `Done` |
| `Scheduler:CreateUser` | 寫入 DB 的 `CREATE_USER` |
| `Scheduler:Tables` | 所有讀寫資料表名稱 |
| `AppLogging` | Serilog 最小層級、檔案 sink、Seq sink |

user-secrets 範例：

```powershell
dotnet user-secrets set "ConnectionStrings:Connection" "<connection-string>" --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj
```

## 資料來源責任

| 資料來源 | 用途 | 程式是否修改 |
| --- | --- | --- |
| `Quant.txt` | 正式匯入來源，提供 compound `Response`、`R.T.` 與 Misc 中的 `LotNo` | 不修改 |
| `ZZ_NF_GAS_MFG_LOT` | 人工維護 LOT 主檔，提供 `LotNo` 驗證與 `SamplName`、`SampleNo`、`SampleType`、`Container`、`EMVolts`、`RelativeEM` | 只查詢，不修改 |
| `ZZ_NF_GAS_QC_RF` | RF 參考係數來源，提供各 compound 的 `Area_*` | 只查詢，不修改 |
| Excel `Query2` | 只作公式與結果驗證參考，不作正式匯入來源 | 不修改 |
| `.D` 資料夾 | 匯入成功後搬移到 `Done` | 搬移 |

## 匯入規則

- 每個 `.D\Quant.txt` 轉成一筆 raw row。
- `STD` 寫入 `ZZ_NF_GAS_QC_LOT_STD`。
- `PORT X` 寫入 `ZZ_NF_GAS_QC_LOT_PORT`。
- `PROT X` 會視為 `PORT X`，支援現場資料夾 typo。
- `Area_*` 來自 Quant `Response`。
- `RT_*` 來自 Quant `R.T.`。
- STD raw 不採用 Quant `Conc ppb`，`ppb_*` 維持 `NULL`。
- PORT raw `ppb_*` 使用 `RF.Area * PORT_RAW.Area / ACTIVE_STD_AVG.Area`。
- `ACTIVE_STD_AVG` 是依 PORT 的 `AnlzTime`，從 `ZZ_NF_GAS_QC_LOT_STD` 找 `AnlzTime <= PORT時間` 的最近兩筆 STD raw 後即時計算，不直接讀 `ZZ_NF_GAS_QC_LOT_STD_AVG` 最後快照。
- STD/PORT AVG、RPD、PPB 依同一輪同一天資料夾的連續群組處理，取該群組最後兩筆 raw 計算。
- `ZZ_NF_GAS_QC_LOT_STD_AVG` 與 `ZZ_NF_GAS_QC_LOT_PORT_AVG` 是 snapshot table，寫入新 AVG 前會清空舊資料，因此表內永遠只保留最新一筆。
- AVG 表 `Area_*` 欄位需保留小數，現行 DB 已調整為 `decimal(18,6)`。
- STD/PORT RPD 使用 `(MAX - MIN) / AVERAGE(MAX, MIN)`。
- PORT PPB 使用 `RF.Area * PORT_AVG.Area / ACTIVE_STD_AVG.Area`，寫入 `ZZ_NF_GAS_QC_LOT_PORT_PPB` 的 `Area_*` 欄位。
- `ZZ_NF_GAS_QC_RF` 和 `ZZ_NF_GAS_MFG_LOT` 只查詢，不修改。
- 任一 LOT 查不到 `ZZ_NF_GAS_MFG_LOT.LotNo` 時，整批停止，不寫入任何資料，也不搬移 `.D` 資料夾。

## 查重規則

Raw 表查重：

```text
LotNo + Port + SampleNo + DataFilename
```

計算表查重：

```text
ID + LotNo + Port + DataFilename
```

加入 `DataFilename` 是因為 raw `ID` 目前使用 `yyyyMMdd + SampleNo`，同一天同 SampleNo 可能有多筆 raw。

## 執行

測試：

```powershell
dotnet test .\JinZhaoYi.GasQcDataLoader.sln
```

單輪正式匯入：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- --Scheduler:RunOnce=true --Scheduler:StableFolderMinutes=0 --Scheduler:DryRun=false
```

DryRun：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- --Scheduler:RunOnce=true --Scheduler:StableFolderMinutes=0 --Scheduler:DryRun=true
```

## 20251119 實跑結果

以 `GAS\20251119` 實跑：

| 類型 | 筆數 |
| --- | ---: |
| 掃描 Quant.txt | 68 |
| 成功處理 | 68 |
| 搬移到 Done | 68 |
| `ZZ_NF_GAS_QC_LOT_STD` | 18 |
| `ZZ_NF_GAS_QC_LOT_PORT` | 50 |
| `ZZ_NF_GAS_QC_LOT_STD_AVG` | 1 |
| `ZZ_NF_GAS_QC_LOT_STD_RPD` | 6 |
| `ZZ_NF_GAS_QC_LOT_PORT_AVG` | 1 |
| `ZZ_NF_GAS_QC_LOT_PORT_PPB` | 10 |
| `ZZ_NF_GAS_QC_LOT_PORT_RPD` | 10 |

代表性 Excel 比對：

| 項目 | Acetone | IPA |
| --- | ---: | ---: |
| `PORT 2[20251119 1147]_023.D` PORT PPB | 98.228605394643 | 105.475527415479 |
| `PORT 2[20251119 1147]_023.D` PORT RPD | 0.030336845014 | 0.021699853922 |
| `STD[20251119 1032]_903.D` STD RPD | 0.000563829471 | 0.015242791187 |
| 最新 STD AVG `STD[20251120 0412]_903.D` | 1041657.500000 | 3688957.500000 |
| 最新 PORT AVG `PORT 12[20251120 0342]_034.D` | 1028027.500000 | 3469187.500000 |

## 文件

- `Docs/程式碼流程與資料流說明書.md`: 程式入口、排程、資料流、DB 寫入、Done 搬移與維運說明。
- `Docs/ZZ_NF_GAS_QC_LOT_PORT欄位來源說明.md`: PORT raw 與 STD/PORT 計算表欄位來源。

## 待確認

- RF 每日來源規則目前以程式既有邏輯取可用 RF，若未來規則更明確，需調整 `DapperRepository.GetLatestRfAsync()`。
- `STD_QC` 公式已保留在程式中，但目前未寫 DB，因尚未確認對應資料表。
