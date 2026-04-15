# JinZhaoYi Gas QC DataLoader

`.NET 8 Worker Service` for importing Gas QC `Quant.txt` files into SQL Server. The application is built as `WinExe`, so it can stay resident in the background when deployed as a Windows Service. `RunOnce=true` is still available for local dry-run verification.

## 專案結構

- `Configuration`: 排程、DB 表名、logging options。
- `DataModels`: Quant 與 DB row 用到的 DTO/model。
- `Logging`: Serilog 初始化與檔案輸出設定。
- `Logs`: Serilog 檔案輸出目錄。
- `Services/Infrastructure`: SQL connection factory 與 Dapper repository。
- `Services/Interface`: 介面契約。
- `Services/Service`: parser、scanner、calculation、orchestrator、job 實作。
- `Services/Processing`: `Worker`，負責 BackgroundService 宿主與輪詢。

## 設定

主要設定在 `src/JinZhaoYi.GasQcDataLoader/appsettings.json`:

- `ConnectionStrings:Connection`: SQL Server 連線字串。
- `Scheduler:WatchRoot`: 每日資料夾根目錄，例如 `GAS`。
- `Scheduler:IntervalSeconds`: 常駐輪詢秒數。
- `Scheduler:MinimumIntervalSeconds`: 最小輪詢秒數保護。
- `Scheduler:StableFolderMinutes`: 資料夾最後修改時間需穩定幾分鐘後才處理。
- `Scheduler:InstrumentName`: 寫入 `Inst`，預設 `QC-01`。
- `Scheduler:SampleType`: 寫入 `SampleType`，預設 `TO14C1`。
- `Scheduler:DryRun`: `true` 時只解析和驗證，不寫 DB。
- `Scheduler:RunOnce`: `true` 時跑完一輪即結束，適合手動測試。
- `Scheduler:CreateUser`: 寫入 DB 的 `CREATE_USER`。
- `Scheduler:Tables`: 所有讀寫資料表名稱。
- `AppLogging`: Serilog 最小層級、檔案 sink、Seq sink。

不要把正式 DB 密碼提交到 GitHub。開發機請用 user-secrets 或環境變數覆蓋連線字串:

```powershell
dotnet user-secrets set "ConnectionStrings:Connection" "<connection-string>" --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj
```

## 匯入規則

- 每個 `.D/Quant.txt` 轉成一筆 raw row。
- `STD` 寫入 `ZZ_NF_GAS_QC_LOT_STD`。
- `PORT X` 寫入 `ZZ_NF_GAS_QC_LOT_PORT`。
- `Area_*` 來自 Quant `Response`。
- `RT_*` 來自 Quant `R.T.`。
- STD raw 不採用 Quant `Conc ppb`，`ppb_*` 維持 `NULL`。
- PORT raw `ppb_*` 使用 `RF.Area * PORT_RAW.Area / ACTIVE_STD_AVG.Area`。
- `ACTIVE_STD_AVG` 是依 PORT 的 `AnlzTime`，從 `ZZ_NF_GAS_QC_LOT_STD` 找 `AnlzTime <= PORT時間` 的最近兩筆 STD raw 後即時計算，不直接讀 `ZZ_NF_GAS_QC_LOT_STD_AVG` 最後快照。
- STD/PORT AVG 使用目前處理時間點以前 raw 表最近兩筆的 Area 平均。
- `ZZ_NF_GAS_QC_LOT_STD_AVG` 與 `ZZ_NF_GAS_QC_LOT_PORT_AVG` 是 snapshot table，寫入新 AVG 前會清空舊資料，因此表內永遠只保留一筆。
- STD/PORT RPD 使用 `(MAX - MIN) / AVERAGE(MAX, MIN)`。
- PORT PPB 使用 `RF.Area * PORT_AVG.Area / ACTIVE_STD_AVG.Area`，寫入 `ZZ_NF_GAS_QC_LOT_PORT_PPB` 的 `Area_*` 欄位。
- PORT PPB 若遇到相同 `ID + LotNo + Port`，會替換成最新 AVG 對應的 PPB。
- `ZZ_NF_GAS_QC_RF` 和 `ZZ_NF_GAS_MFG_LOT` 只查詢，不修改。
- 任一 LOT 查不到 `ZZ_NF_GAS_MFG_LOT.LotNo` 時，整個日期資料夾停止，不寫入任何資料。

## 執行

```powershell
dotnet test .\JinZhaoYi.GasQcDataLoader.sln
```

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- --Scheduler:RunOnce=true --Scheduler:StableFolderMinutes=0
```

目前 `appsettings.json` 的 `Scheduler:DryRun` 預設為 `true`。確認 LOT 主檔與公式結果後，再改為 `false` 寫入 DB。

## 目前 dry-run 結果

以 `GAS/20251119` 測試:

- 掃描到 68 個 `Quant.txt`。
- 解析到 11 個不同 LOT。
- 目前 DB 的 `ZZ_NF_GAS_MFG_LOT` 查不到下列 LOT，因此整批停止且未寫入:
  - `20251030001`
  - `20251117006`
  - `20251117007`
  - `20251118001`
  - `20251118002`
  - `20251118003`
  - `20251118004`
  - `20251118005`
  - `20251118006`
  - `20251118007`
  - `20251118008`

## 待確認

- `ZZ_NF_GAS_MFG_LOT` 是否確定以 `LotNo` 欄位查詢，或實際 LOT 主鍵欄位另有名稱。
- `RF` 每日來源規則，目前程式取 `AnlzTime <= Quant 最早時間` 的最新 RF row。
- `si0_id` 第一版由 `ZZ_NF_GAS_MFG_LOT.ID` 帶入。
- `STD_QC` 公式已保留在程式中，但目前未寫 DB，因尚未確認對應資料表。
