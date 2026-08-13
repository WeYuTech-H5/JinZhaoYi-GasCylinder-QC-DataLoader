# ADR-0001：Quant 匯入改為 raw-only

- 狀態：已採用，程式碼仍保留舊流程以便確認過渡期影響
- 決策日期：2026-08-05
- 相關變更：`6a83e89`（暫停 Quant 匯入衍生計算並更新流程文件）

## 背景

專案最初的流程是在 Quant 自動匯入時：

1. 解析 Quant 與 acqmeth。
2. 取得當時可用 RF。
3. 寫入 STD／PORT raw。
4. 同時計算並寫入 AVG、QC、RPD、PPB 衍生資料。
5. 視設定回寫 MFG LOT QC 結果。

後來新增手動 Query2 流程：使用者先選取 RF、STD raw、PORT raw，系統再依這次選取重新計算 AVG、QC、RPD、PPB。這使「匯入時自動算一次」與「使用者匯出時依選取重算」成為兩套不同時間點、不同 RF 選擇的結果。

手動 Query2 不讀匯入階段的衍生 tables，因此自動計算已不是現行 Query2 的必要條件。

## 決策

Quant 背景匯入只執行：

- 掃描與解析 Quant／acqmeth。
- LOT 與 MFG 主檔驗證。
- raw identity 建立與 DB 查重。
- 寫入 STD raw／PORT raw。
- 正式成功後更新 processed state，並依設定搬到 archive。

Quant 匯入階段暫停：

- 取得 RF。
- 計算 AVG／QC／RPD／PPB。
- 寫入 STD AVG/QC/RPD 與 PORT AVG/PPB/RPD。
- `QcResultWriteback:OnQuantImport` 的 QC 回寫。

AVG、QC、RPD、PPB 改在使用者產生 Query2 preview／Excel 時，依當次選取的 RF 與 raw 重新計算。

## 結果

### 正面影響

- Query2 計算來源明確：只依賴使用者當次選取的 RF 與 raw。
- Quant 匯入不再因找不到 RF 而阻止保存有效 raw。
- 避免同一批資料在匯入與手動匯出使用不同 RF 或選取範圍，卻被誤認為同一份計算結果。

### 需要接受的影響

- `STD_AVG`、`STD_QC`、`STD_RPD`、`PORT_AVG`、`PORT_PPB`、`PORT_RPD` 不再新增 Quant 匯入資料。
- 舊 `/api/port-ppb-options` 與 `/api/exports/port-ppb-csv` 只能看到既有 `PORT_PPB`。
- 新 CSV／COA 流程必須使用 Query2 成功匯出後保存的 Excel PPB history。
- 舊計算程式碼與 tables 暫時仍存在，增加維護時辨識現行／舊流程的成本。

## 現行資料來源

| 輸出 | 現行來源 |
| --- | --- |
| Query2 AVG／QC／RPD／PPB | 使用者選取的 RF、STD raw、PORT raw 即時計算 |
| TO14C CSV | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY` |
| COA 大卡／小卡 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY` |
| MFG LOT QC 回寫 | Query2 正式匯出完成時的 QC 判定 |

## 為何尚未刪除舊程式碼與 tables

目前不能確認外部 MES、報表或其他程式是否直接讀取舊衍生 tables。立刻刪除會把程式內部重構擴大成跨系統相容性風險。

舊流程目前保留在 `ImportOrchestrator`、`ImportWriteSetBuilder` 與 `DapperRepository` 的相關程式碼中，但呼叫點已停用或改走 raw-only。

## 正式移除前的必要條件

1. 盤點所有直接讀取 `STD_AVG`、`STD_QC`、`STD_RPD`、`PORT_AVG`、`PORT_PPB`、`PORT_RPD` 的外部使用者。
2. 將舊 PORT PPB APIs 移除、標示停用，或改成讀 Excel PPB history／即時計算。
3. 確認 `Scheduler:QcResultWriteback:OnQuantImport` 永久不再需要。
4. 使用正式資料比對 Query2、CSV、COA、QC 回寫與舊結果。
5. 完成資料保留與舊 table 退場方案後，才刪除程式碼、設定與 table references。

## 若需要暫時恢復舊流程

恢復不是單純取消一段註解就算完成。至少需要：

1. 在 `ImportOrchestrator.ImportCandidatesAsync` 恢復 RF 查詢與完整 `BuildWriteSet`。
2. 在 `DapperRepository.ExecuteImportAsync` 恢復 STD／PORT 衍生處理與必要 QC 回寫。
3. 確認 `UseAverageSnapshotTables` 與 `QcResultWriteback:OnQuantImport` 的正式設定。
4. 以完整測試與正式樣本比對 AVG、QC、RPD、PPB、Query2 與 MFG LOT QC。
5. 評估同一資料在匯入時與手動 Query2 時產生不同結果時，哪一份才是權威資料。

未完成上述確認前，raw-only 仍是現行且唯一應依賴的 Quant 匯入行為。
