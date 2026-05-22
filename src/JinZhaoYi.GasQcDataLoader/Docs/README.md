# JinZhaoYi Gas QC DataLoader

此專案是 `.NET 8` Windows Service / Web API，負責 GAS QC 資料匯入、Query2 Excel 匯出、Excel PPB CSV/COA 匯出，以及正式區暫時使用的 MFG JSON 同步。

## 主要功能

- 掃描 GAS 資料夾中的 `Quant.txt`，解析後寫入 SQL Server Gas QC tables。
- 依使用者選取的 RF / STD raw / PORT raw 產生 Query2 Excel。
- Query2 Excel 成功產生後，將 PPB row 保存到 `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY`。
- 使用 Excel PPB history 匯出 TO14C CSV、COA 大卡、COA 小卡。
- 掃描 `C:\temp\data\MFGJSON` 的 `MFGExport_*.json`，同步寫入 `ZZ_NF_GAS_MFG_LOT`。
- 提供前端查詢、匯出與下載 API。

## 專案結構

| 目錄 | 說明 |
| --- | --- |
| `Configuration` | `Scheduler`、table name、Excel/CSV/COA/API/MFG JSON/logging 設定模型。 |
| `DataModels` | Quant、LOT、RF、QC row、Query2、CSV、COA、MFG JSON、API DTO。 |
| `Services/Infrastructure` | SQL connection factory 與 Dapper repository。 |
| `Services/Service` | Scanner、parser、calculation、import/export orchestration、MFG JSON 匯入服務。 |
| `Services/Processing` | 背景 worker loop，包含 Quant 匯入 worker 與 MFG JSON import worker。 |
| `Docs` | 匯入流程、欄位 mapping、Query2、COA、MFG JSON 文件。 |

## 資料表

表名可由 `Scheduler:Tables` 設定覆寫，主要預設如下：

| 用途 | Table |
| --- | --- |
| MFG LOT 主檔 | `ZZ_NF_GAS_MFG_LOT` |
| RF | `ZZ_NF_GAS_QC_RF` |
| STD raw | `ZZ_NF_GAS_QC_LOT_STD` |
| STD AVG | `ZZ_NF_GAS_QC_LOT_STD_AVG` |
| STD QC | `ZZ_NF_GAS_QC_LOT_STD_QC` |
| STD RPD | `ZZ_NF_GAS_QC_LOT_STD_RPD` |
| PORT raw | `ZZ_NF_GAS_QC_LOT_PORT` |
| PORT AVG | `ZZ_NF_GAS_QC_LOT_PORT_AVG` |
| PORT PPB | `ZZ_NF_GAS_QC_LOT_PORT_PPB` |
| PORT RPD | `ZZ_NF_GAS_QC_LOT_PORT_RPD` |
| Excel PPB history | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY` |
| 匯入錯誤紀錄 | `ZZ_NF_GAS_QC_ERROR_LOG` |

`ZZ_NF_GAS_QC_LOT_PORT_PPB` 是匯入計算流程使用的 PPB table；手動 CSV/COA 匯出清單改讀 `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY`，代表使用者成功產生 Query2 Excel 後保存的 PPB 快照。

## 重要設定

主要設定在 `src/JinZhaoYi.GasQcDataLoader/appsettings.json` 與正式環境覆寫設定。

| Setting | 說明 |
| --- | --- |
| `ConnectionStrings:Connection` | SQL Server 連線字串。 |
| `Scheduler:WatchRoot` | GAS 根目錄。 |
| `Scheduler:TargetMode` | `TargetDate` 處理指定日期；`AllNewStableFiles` 處理所有穩定且未處理檔案。 |
| `Scheduler:StableFolderMinutes` | `.D` folder 穩定多久後才處理。 |
| `Scheduler:DryRun` | `true` 時只解析、驗證與計算，不寫 DB。 |
| `Scheduler:RunOnce` | `true` 時跑一輪後結束。 |
| `Scheduler:BackfillEnabled` / `BackfillTargetDate` | 補跑指定 `yyyyMMdd` 日期。 |
| `Scheduler:MoveProcessedFilesToDone` | 成功後是否搬到 archive。 |
| `Scheduler:UseAverageSnapshotTables` | `true` 時 AVG tables 保持 snapshot 行為；`false` 時保留 AVG history。 |
| `Scheduler:ExcelExport:Enabled` | 是否輸出 Query2 Excel。 |
| `Scheduler:ExcelExport:TemplatePath` | Query2 Excel template 路徑。 |
| `Scheduler:CsvExport:*` | TO14C CSV 欄位與預設值設定。 |
| `Scheduler:CoaExport:*` | COA 大卡/小卡 template 與小卡預設每頁格數。 |
| `Scheduler:DownloadApi:Enabled` | 是否啟用查詢、匯出與下載 API。 |
| `Scheduler:MfgJsonImport:Enabled` | 是否啟用 MFG JSON 背景匯入。 |
| `Scheduler:MfgJsonImport:WatchDirectory` | MFG JSON 掃描資料夾，正式預設 `C:\temp\data\MFGJSON`。 |
| `Scheduler:MfgJsonImport:FilePattern` | MFG JSON 檔名 pattern，預設 `MFGExport_*.json`。 |
| `Scheduler:MfgJsonImport:StateFilePath` | MFG JSON 處理狀態檔，預設 `C:\temp\data\MFGJSON\MfgJsonImportState.json`。 |

## Log

Log 會放在執行目錄底下的 `Logs`，並依用途分資料夾：

| 路徑 | 說明 |
| --- | --- |
| `Logs\Application\app-YYYYMMDD.log` | 一般程式執行 log。 |
| `Logs\MFGJSON\scan-YYYYMMDD.log` | MFG JSON 掃描、略過、成功、失敗、insert/update 筆數。 |
| `Logs\serilog-selflog.txt` | Serilog 自身錯誤備援 log。 |

## API

啟用 `Scheduler:DownloadApi:Enabled=true` 後提供：

| Endpoint | 說明 |
| --- | --- |
| `GET /api/export-groups?startDate=yyyyMMdd&endDate=yyyyMMdd` | 從 DB raw tables 讀取日期區間資料，回傳前端可勾選的 STD/PORT/Lot/SampleName group。 |
| `GET /api/rf-options` | 從 RF table 讀取 RF rows。 |
| `POST /api/exports/query2-excel` | 依前端選取的 RF、STD raw、PORT raw 產生 Query2 Excel，成功後保存 Excel PPB history。 |
| `GET /api/excel-ppb-options?batchDate=yyyyMMdd&page=1&pageSize=50` | 讀取 Query2 Excel 成功產生後保存的 PPB history，供 CSV/COA 勾選。 |
| `POST /api/exports/excel-ppb-csv` | 依 Excel PPB history 匯出 TO14C CSV。 |
| `POST /api/exports/excel-ppb-coa-large` | 依 Excel PPB history 匯出 COA 大卡，支援一般/亞東 template。 |
| `POST /api/exports/excel-ppb-coa-small` | 依 Excel PPB history 匯出 COA 小卡，支援 1 張以上；每頁最多 9 格，超過自動換頁。 |
| `GET /api/downloads/cylinder-qc/{batchDate}` | 下載既有 `Cylinder_Qc[{batchDate}].xlsx`。 |
| `GET /api/downloads/to14c-csv/{sampleName}` | 下載指定 sample 的 TO14C CSV。 |

## 常用指令

Build/test：

```powershell
dotnet test .\JinZhaoYi.GasQcDataLoader.sln
```

手動跑一輪 Quant 匯入：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- --Scheduler:RunOnce=true --Scheduler:StableFolderMinutes=0 --Scheduler:DryRun=false
```

手動補跑指定日期：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- --Scheduler:RunOnce=true --Scheduler:BackfillEnabled=true --Scheduler:BackfillTargetDate=20251119 --Scheduler:StableFolderMinutes=0 --Scheduler:DryRun=false
```

## 相關文件

| 文件 | 說明 |
| --- | --- |
| `Query2Excel_FromDbRawData_README.md` | 從 DB raw data 產生 Query2 Excel 的演算法與資料流。 |
| `QuantParser_DB_Mapping_README.md` | `Quant.txt` 解析與 DB 欄位 mapping。 |
| `COA_Field_Mapping_README.md` | COA 大卡/小卡欄位來源與填值規則。 |
| `MFG_JSON_IMPORT_README.md` | MFG JSON 背景匯入、欄位 mapping、狀態檔與 log 說明。 |
