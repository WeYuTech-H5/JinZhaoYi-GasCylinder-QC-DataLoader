# JinZhaoYi Gas QC DataLoader

這個專案是 `.NET 8 Worker Service`，負責：

- 掃描 GAS 資料夾中的 `Quant.txt`
- 解析並匯入 SQL Server Gas QC tables
- 從 DB 已匯入資料匯出 `Query2` Excel
- 匯出 TO14C PORT_PPB CSV
- 提供下載與手動匯出 API

## 專案結構

| 目錄 | 說明 |
| --- | --- |
| `Configuration` | Scheduler、table name、Excel/CSV/API 設定模型。 |
| `DataModels` | Quant、LOT、RF、QC row、Query2、CSV、API DTO。 |
| `Services/Infrastructure` | SQL connection factory 與 Dapper repository。 |
| `Services/Service` | Scanner、parser、calculation、import/export orchestration。 |
| `Services/Processing` | Worker background loop。 |
| `Docs` | 維護中的流程文件與 DB helper script。 |

## 主要資料表

表名可由 `Scheduler:Tables` 設定覆寫，預設如下：

| 用途 | Table |
| --- | --- |
| LOT 主檔 | `ZZ_NF_GAS_MFG_LOT` |
| RF | `ZZ_NF_GAS_QC_RF` |
| STD raw | `ZZ_NF_GAS_QC_LOT_STD` |
| STD AVG | `ZZ_NF_GAS_QC_LOT_STD_AVG` |
| STD QC | `ZZ_NF_GAS_QC_LOT_STD_QC` |
| STD RPD | `ZZ_NF_GAS_QC_LOT_STD_RPD` |
| PORT raw | `ZZ_NF_GAS_QC_LOT_PORT` |
| PORT AVG | `ZZ_NF_GAS_QC_LOT_PORT_AVG` |
| PORT PPB | `ZZ_NF_GAS_QC_LOT_PORT_PPB` |
| PORT RPD | `ZZ_NF_GAS_QC_LOT_PORT_RPD` |

## 設定重點

主要設定在 `src/JinZhaoYi.GasQcDataLoader/appsettings.json` 與 `appsettings.Development.json`。

| Setting | 說明 |
| --- | --- |
| `ConnectionStrings:Connection` | SQL Server 連線字串。 |
| `Scheduler:WatchRoot` | GAS 根目錄，可是日期資料夾上層，也可以是已經指向批次資料夾的路徑。 |
| `Scheduler:TargetMode` | `TargetDate` 處理指定日期；`AllNewStableFiles` 處理所有穩定且未處理檔案。 |
| `Scheduler:StableFolderMinutes` | `.D` folder 穩定多久後才處理。 |
| `Scheduler:DryRun` | `true` 時只解析、驗證與計算，不寫 DB。 |
| `Scheduler:RunOnce` | `true` 時跑一輪後結束。 |
| `Scheduler:BackfillEnabled` / `BackfillTargetDate` | 補跑指定 `yyyyMMdd` 日期。 |
| `Scheduler:MoveProcessedFilesToDone` | 成功後是否搬到 archive。 |
| `Scheduler:UseAverageSnapshotTables` | `true` 時 AVG tables 保持 snapshot 行為；`false` 時保留 AVG history。 |
| `Scheduler:ExcelExport:Enabled` | 是否輸出 Query2 Excel。 |
| `Scheduler:ExcelExport:TemplatePath` | Query2 Excel template 路徑。 |
| `Scheduler:DownloadApi:Enabled` | 是否啟用查詢、匯出與下載 API。 |

## 常用指令

Build/test：

```powershell
dotnet test .\JinZhaoYi.GasQcDataLoader.sln
```

單次匯入：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- --Scheduler:RunOnce=true --Scheduler:StableFolderMinutes=0 --Scheduler:DryRun=false
```

補跑指定日期：

```powershell
dotnet run --project .\src\JinZhaoYi.GasQcDataLoader\JinZhaoYi.GasQcDataLoader.csproj -- --Scheduler:RunOnce=true --Scheduler:BackfillEnabled=true --Scheduler:BackfillTargetDate=20251119 --Scheduler:StableFolderMinutes=0 --Scheduler:DryRun=false
```

## API

啟用 `Scheduler:DownloadApi:Enabled=true` 後提供：

| Endpoint | 說明 |
| --- | --- |
| `GET /api/export-groups?startDate=yyyyMMdd&endDate=yyyyMMdd` | 從 DB raw tables 讀指定日期區間，依 STD/PORT、Port、Lot、SampleName 分組，供 UI 勾選。 |
| `GET /api/rf-options` | 從 RF table 讀可選 RF rows。 |
| `POST /api/exports/query2-excel` | 依 UI 選取的 RF、STD raw、PORT raw 從 DB 重新產生 Query2 Excel。 |
| `POST /api/exports/port-ppb-csv` | 依選取 PORT_PPB rows 產生 TO14C CSV。 |
| `GET /api/downloads/cylinder-qc/{batchDate}` | 下載已產生的 `Cylinder_Qc[{batchDate}].xlsx`。 |
| `GET /api/downloads/to14c-csv/{sampleName}` | 下載指定 sample 的 TO14C CSV。 |

## TO14C PORT_PPB CSV 欄位規則

`POST /api/exports/port-ppb-csv` 會依選取的 PORT_PPB row 讀 `ZZ_NF_GAS_QC_LOT_PORT_PPB`。CSV 中 `Item,N,MEAN,SD,MAX,MIN,VALUE,DL` 的來源如下：

| 欄位 | 來源 / 規則 |
| --- | --- |
| `Item` | `To14cCsvAnalyteMap` 的 TO14C 固定品項名稱。 |
| `N` | 依範例保留空白。 |
| `MEAN` | 依範例保留空白。 |
| `SD` | 依範例保留空白。 |
| `MAX` | 依範例保留空白。 |
| `MIN` | 依範例保留空白。 |
| `VALUE` | `ZZ_NF_GAS_QC_LOT_PORT_PPB.Area_*` 的最終 PPB 結果，CSV 依規格輸出為整數。 |
| `DL` | 依範例保留空白。 |

`Water`、`Oxygen`、`Nitrogen` 為設定檔固定值。`RemainLifeTime` 由 `DeliverDate` 與 `ShelfLifeTime` 計算；若 `DeliverDate` 未設定則保留空白。

## Query2 Excel 從 DB 匯出演算法

詳細規則請看：

[Query2 Excel DB Export Algorithm](./Query2Excel_FromDbRawData_README.md)

這份文件說明：

- API 從 DB 抓哪些資料
- UI 勾選資料如何用 `RawDataIdentity` 對回 DB raw rows
- RF / STD raw / PORT raw 如何排序與分組
- active STD AVG 如何選定
- Query2 row 產生順序
- `Area_*`、`ppb_*`、`RT_*` 如何寫進 Excel
- 為什麼 `Cylinder_DataBase_20251119.xlsx` 有 5 格手動公式與標準演算法不同

## 維護中的文件

| 文件 | 用途 |
| --- | --- |
| `README.md` | 專案入口與常用設定/API。 |
| `Query2Excel_FromDbRawData_README.md` | 從 DB rawdata 產生 Query2 Excel 的主要演算法。 |
| `QuantParser_DB_Mapping_README.md` | `Quant.txt` 解析與 DB 欄位 mapping。 |
| `Create_ZZ_NF_GAS_QC_LOT_STD_QC.sql` | 建立 `ZZ_NF_GAS_QC_LOT_STD_QC` 的 helper script。 |
