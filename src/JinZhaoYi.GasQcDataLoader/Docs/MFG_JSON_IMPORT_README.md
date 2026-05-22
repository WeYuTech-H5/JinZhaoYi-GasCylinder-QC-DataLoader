# MFG JSON 匯入背景服務

此文件說明正式區暫時使用的 MFG JSON 接收端。舊 DB 的 console app 會定時查詢是否有新增 MFG 生產紀錄，若有資料就輸出 JSON 檔到指定資料夾；本服務負責掃描該資料夾並寫入 `ZZ_NF_GAS_MFG_LOT`。

## 資料流程

```text
舊 DB console app
  -> C:\temp\data\MFGJSON\MFGExport_yyyyMMdd_HHmmss.json
  -> MfgJsonImportWorker 掃描穩定檔案
  -> MfgJsonParser 驗證與轉型
  -> DapperRepository.UpsertMfgJsonLotsAsync()
  -> dbo.ZZ_NF_GAS_MFG_LOT
  -> C:\temp\data\MFGJSON\MfgJsonImportState.json
```

## 設定

| Setting | 預設值 | 說明 |
| --- | --- | --- |
| `Scheduler:MfgJsonImport:Enabled` | `false` | 是否啟用 MFG JSON 背景匯入。正式區要設為 `true`。 |
| `Scheduler:MfgJsonImport:WatchDirectory` | `C:\temp\data\MFGJSON` | 掃描 JSON 檔案的資料夾。 |
| `Scheduler:MfgJsonImport:FilePattern` | `MFGExport_*.json` | 掃描檔名 pattern。若正式檔案沒有副檔名，改成 `MFGExport_*`。 |
| `Scheduler:MfgJsonImport:PollIntervalSeconds` | `30` | 每隔幾秒掃描一次。 |
| `Scheduler:MfgJsonImport:StableFileSeconds` | `10` | 檔案最後修改時間穩定幾秒後才處理，避免寫檔尚未完成。 |
| `Scheduler:MfgJsonImport:StateFilePath` | `C:\temp\data\MFGJSON\MfgJsonImportState.json` | 匯入稽核狀態檔。 |
| `Scheduler:MfgJsonImport:CreateUserPrefix` | `MFGJSON` | 寫入 `CREATE_USER` / `EDIT_USER` 的前綴。 |

## 寫入規則

- JSON root 必須是 array。
- 每筆必要欄位為 `si0_id`、`LotNo`。
- 空字串會轉成 `null`。
- 日期與數值欄位會先轉型；任何一筆格式錯誤時，整個檔案失敗，不做部分寫入。
- 同一個 JSON 檔內不可重複 `si0_id` 或 `LotNo`。
- 寫入 `ZZ_NF_GAS_MFG_LOT` 使用 transaction。
- 用 `si0_id` / `ID` / `LotNo` 尋找既有資料。
- 找到一筆就 update；找不到就 insert。
- 若同一筆 JSON 對到多筆 DB row，整個檔案失敗，避免程式猜測要覆蓋哪筆正式資料。
- Insert 時寫入 `CREATE_USER`、`CREATE_TIME`。
- Update 時保留原 `CREATE_USER`、`CREATE_TIME`，改寫 `EDIT_USER`、`EDIT_TIME`。

`CREATE_USER` / `EDIT_USER` 會包含來源檔名，格式如下：

```text
MFGJSON(MFGExport_20260522_093603.json)
```

## 欄位對應

| JSON 欄位 | DB 欄位 |
| --- | --- |
| `si0_id` | `ID`、`si0_id` |
| `si0_SampleName` | `SamplName` |
| `si0_ProdDate` | `ProdDate` |
| `LotNo` | `LotNo` |
| `ProdType` | `ProdType` |
| `Prod_Operator` | `Prod_Operator` |
| `Prod_IniPrs` | `Prod_IniPrs` |
| `Prod_LeakTest1` | `Prod_LeakTest1` |
| `Prod_VacuumPrs` | `Prod_vacumPrs` |
| `Prod_Can1_FillingPrs` | `Prod_Can1_FillingPrs` |
| `Prod_Can2_FillingPrs` | `Prod_Can2_FillingPrs` |
| `Prod_Bomb2_FillingPrs` | `Prod_Bomb2_FillingPrs` |
| `Prod_Bomb1_FillingPrs` | `Prod_Bomb1_FillingPrs` |
| `Prod_Bomb3_FillingPrs` | `Prod_Bomb3_FillingPrs` |
| `Prod_LeakTest2` | `Prod_LeakTest2` |
| `Prod_Can1_LotNo` | `Prod_Can1_LotNo` |
| `Prod_Can1_Flow` | `Prod_Can1_Flow` |
| `Prod_Can1_Sec` | `Prod_Can1_Sec` |
| `Prod_Can1_Prs` | `Prod_Can1_Prs` |
| `Prod_Can2_LotNo` | `Prod_Can2_LotNo` |
| `Prod_Can2_Flow` | `Prod_Can2_Flow` |
| `Prod_Can2_Sec` | `Prod_Can2_Sec` |
| `Prod_Can2_Prs` | `Prod_Can2_Prs` |
| `Prod_Bomb2_LotNo` | `Prod_Bomb2_LotNo` |
| `Prod_Bomb2_Flow` | `Prod_Bomb2_Flow` |
| `Prod_Bomb2_Sec` | `Prod_Bomb2_Sec` |
| `Prod_Bomb2_Prs` | `Prod_Bomb2_Prs` |
| `Prod_Bomb1_LotNo` | `Prod_Bomb1_LotNo` |
| `Prod_Bomb1_SetFillingPrs` | `Prod_Bomb1_SetFillingPrs` |
| `Prod_Bomb1_Prs` | `Prod_Bomb1_Prs` |
| `Prod_Bomb3_LotNo` | `Prod_Bomb3_LotNo` |
| `Prod_Bomb3_SetFillingPrs` | `Prod_Bomb3_SetFillingPrs` |
| `Prod_Bomb3_Prs` | `Prod_Bomb3_Prs` |
| `SampleNo` | `SampleNo` |
| `SampleType` | `SampleType` |
| `Container` | `Container` |
| `ProdOrder` | `ProdOrder` |
| `FnlPrs` | `FnlPrs` |
| `IniPrs` | `IniPrs` |
| `QCTime` | `QCTime` |
| `QCInst` | `QCInst` |
| `QCPort` | `QCPort` |
| `Cal_id` | `Cal_id` |
| `RF_ID` | `RF_ID` |
| `QCComplete` | `QCComplete` |
| `CalType` | `CalType` |
| `Result` | `Result` |

## 狀態檔

狀態檔是稽核與避免同檔同 hash 重複匯入的輔助檔，不是 MFG LOT 主資料來源。

預設路徑：

```text
C:\temp\data\MFGJSON\MfgJsonImportState.json
```

結構包含：

- `files`：檔名、hash、狀態、處理時間、insert/update 筆數、錯誤訊息。
- `lots`：LotNo、si0_id、來源檔案、來源 hash、insert/update 動作、處理時間。

同一檔名同 hash 已成功處理過時會略過；同一檔名但內容變更時會重新處理。

## Log

MFG JSON 掃描與匯入 log 會寫到：

```text
Logs\MFGJSON\scan-YYYYMMDD.log
```

內容包含：

- 掃描開始與結束。
- 找到幾個檔案。
- 幾個檔案尚未穩定而略過。
- 成功匯入的 insert/update 筆數。
- 失敗檔案與錯誤訊息。

一般程式執行 log 會寫到：

```text
Logs\Application\app-YYYYMMDD.log
```
