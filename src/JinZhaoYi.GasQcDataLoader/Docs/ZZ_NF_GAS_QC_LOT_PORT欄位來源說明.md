# Gas QC 欄位來源說明

文件日期：2026-04-15

## 1. 文件目的

本文件說明 Gas QC DataLoader 寫入資料表時，各欄位在匯入流程中的來源與計算方式。

`ZZ_NF_GAS_QC_LOT_PORT` 是 PORT 原始資料表。每一個 PORT `.D\Quant.txt` 會產生一筆 raw row 寫入此表。

本文件同時補充以下計算表欄位來源：

| 資料表 | 用途 |
|---|---|
| `ZZ_NF_GAS_QC_LOT_PORT_AVG` | PORT 連續群組最後兩筆 raw 的 Area 平均；此表只保留最新一筆 |
| `ZZ_NF_GAS_QC_LOT_PORT_PPB` | PORT AVG 依 RF 與 PORT 時間點有效 STD AVG 換算後的 PPB |
| `ZZ_NF_GAS_QC_LOT_PORT_RPD` | PORT 連續群組最後兩筆 raw 的 RPD |
| `ZZ_NF_GAS_QC_LOT_STD_AVG` | STD 連續群組最後兩筆 raw 的 Area 平均；此表只保留最新一筆 |
| `ZZ_NF_GAS_QC_LOT_STD_RPD` | STD 連續群組最後兩筆 raw 的 RPD |

## 2. 資料來源總覽

| 來源 | 用途 |
|---|---|
| PORT `.D\Quant.txt` | 取得 `Acq On`、`Misc`、compound 的 `Response` 與 `R.T.` |
| `.D` 資料夾名稱 | 取得 `SampleNo`，例如 `_023.D` 代表 `SampleNo = 23` |
| 上層資料夾名稱 | 取得 `Port`，例如 `PORT 2`；若資料夾誤寫成 `PROT 11`，程式會視為 `PORT 11` |
| `ZZ_NF_GAS_MFG_LOT` | 人工維護 LOT 主檔；以 `LotNo` 查詢 `si0_id`、`SampleName`、`SampleType`、`Container`、`EMVolts`、`RelativeEM` |
| `ZZ_NF_GAS_QC_RF` | 取得 RF 的 `Area_*`，用於計算 PORT raw 的 `ppb_*` |
| `ZZ_NF_GAS_QC_LOT_STD` | 依 PORT 的 `AnlzTime` 找 `AnlzTime <= PORT時間` 的最近兩筆 STD raw，即時計算 `ACTIVE_STD_AVG` 作為 `ppb_*` 分母 |
| `appsettings.json` | 取得 `InstrumentName`、`SampleType` fallback、`CreateUser` 等設定 |
| 執行主機 | 取得 `PCName` 與 `CREATE_TIME` |

## 3. 匯入前置規則

1. 程式只處理 `STD`、`PORT X`、`PROT X` 資料夾底下的 `Quant.txt`。
2. LOT 來自 Quant.txt 的 `Misc` 欄位最後 `#` 後方文字。
3. LOT 必須存在於 `ZZ_NF_GAS_MFG_LOT.LotNo`。同一輪整批匯入時，只要任一 LOT 查不到，整批停止，不寫入 DB，也不搬到 Done。
4. PORT raw 的 `ppb_*` 不使用 Quant.txt 內的 `Conc ppb`。
5. PORT raw 的 `ppb_*` 使用公式：

```text
ppb_* = RF.Area_* * PORT_RAW.Area_* / ACTIVE_STD_AVG.Area_*
```

其中 `ACTIVE_STD_AVG` 是依該 PORT 的 `AnlzTime`，從 STD raw 找 `AnlzTime <= PORT時間` 的最近兩筆後即時計算。這樣即使 `ZZ_NF_GAS_QC_LOT_STD_AVG` 最終只保留最後一筆快照，歷史 PORT 重跑仍可對回 Excel 當下使用的 STD AVG。

## 4. 固定欄位來源

| DB 欄位 | 來源或規則 | 備註 |
|---|---|---|
| `SID` | 程式寫入 DB 時產生 | 依日期資料夾 `yyyyMMdd` 產生區間 `yyyyMMdd000` 到 `yyyyMMdd999`，取目前該表最大 SID 後加 1 |
| `ID` | 程式產生 | STD 與 PORT raw table 統一使用 `yyyyMMdd` + `SampleNo` 三碼，例如 `20251119023` |
| `AnlzTime` | Quant.txt header `Acq On` | 例如 `19 Nov 2025 11:32` 解析為 `2025-11-19 11:32:00` |
| `Inst` | `appsettings.json` 的 `Scheduler:InstrumentName` | 目前預設 `QC-01` |
| `Port` | 上層 PORT 資料夾名稱 | 例如 `PORT 2`；`PROT 11` 會正規化為 `PORT 11` |
| `si0_id` | `ZZ_NF_GAS_MFG_LOT.ID` | 以 Quant LOT 查 `ZZ_NF_GAS_MFG_LOT.LotNo` 後取得 |
| `SampleNo` | `.D` 資料夾名稱 | 例如 `PORT 2[20251119 1132]_023.D` 解析為 `23` |
| `LotNo` | Quant.txt header `Misc` | 取 `#` 後方 LOT，例如 `#20251117006` |
| `DataFilename` | 相對於 PORT 資料夾的 Quant 路徑 | 例如 `PORT 2[20251119 1132]_023.D\Quant.txt` |
| `DataFilepath` | Quant.txt 所在 `.D` 資料夾完整路徑 | 例如 `C:\...\PORT 2[20251119 1132]_023.D` |
| `PCName` | 執行程式的 Windows 主機名稱 | 由 `Environment.MachineName` 取得 |
| `Container` | `ZZ_NF_GAS_MFG_LOT.Container` | MFG LOT 主檔補值 |
| `Description` | Quant.txt header `Misc` 完整文字 | 例如 `port 2  023  1078>  #20251117006` |
| `EMVolts` | `ZZ_NF_GAS_MFG_LOT.EMVolts` | 以 Quant LOT 查主檔後帶入 STD / PORT raw row |
| `RelativeEM` | `ZZ_NF_GAS_MFG_LOT.RelativeEM` | 以 Quant LOT 查主檔後帶入 STD / PORT raw row |
| `SampleName` | `ZZ_NF_GAS_MFG_LOT.SamplName` | MFG LOT 主檔補值 |
| `SampleType` | 優先 `ZZ_NF_GAS_MFG_LOT.SampleType`，否則用 `appsettings.json` 的 `Scheduler:SampleType` | 目前 fallback 預設 `TO14C1` |
| `CREATE_USER` | `appsettings.json` 的 `Scheduler:CreateUser` | 目前設定為 `Andy` |
| `CREATE_TIME` | 程式建立 raw row 的時間 | 使用執行主機的 `DateTime.Now` |
| `EDIT_USER` | 第一版固定 `NULL` | 目前匯入不做更新 |
| `EDIT_TIME` | 第一版固定 `NULL` | 目前匯入不做更新 |

## 5. Area 欄位來源

所有 `Area_*` 欄位都來自 Quant.txt compound 表格中的 `Response` 欄位。

若 Quant.txt 找不到對應 compound，該欄位寫入 `NULL`。

| DB 欄位 | Quant compound 名稱 | Quant 欄位 |
|---|---|---|
| `Area_Acetone` | `Acetone` | `Response` |
| `Area_IPA` | `IPA` | `Response` |
| `Area_Methlene` | `Methylene Chloride` | `Response` |
| `Area_CNF` | `CNF` | `Response` |
| `Area_Cyclopentane` | `Cyclopentane` | `Response` |
| `Area_2-Butanone` | `2-Butanone` | `Response` |
| `Area_Ethyl Acetate` | `Ethyl Acetate` | `Response` |
| `Area_Benzene` | `Benzene` | `Response` |
| `Area_Carbon Tetrachloride` | `Carbon Tetrachloride` | `Response` |
| `Area_Toluene` | `Toluene` | `Response` |
| `Area_1,2,4-TMB` | `1,2,4-TMB` | `Response` |
| `Area_Chlorobenzene-D5` | `Chlorobenzene-D5` | `Response` |
| `Area_Freon114` | `Freon114` | `Response` |
| `Area_1,1-Dichloroethene` | `1,1-Dichloroethene` | `Response` |
| `Area_Freon113` | `Freon113` | `Response` |
| `Area_1,1-Dichloroethane` | `1,1-Dichloroethane` | `Response` |
| `Area_cis-1,2-Dichloroethene` | `cis-1,2-Dichloroethene` | `Response` |
| `Area_Freon20` | `Freon20` | `Response` |
| `Area_1,1,1-Trichloroethane` | `1,1,1-Trichloroethane` | `Response` |
| `AREA_1,2-Dichloroethane` | `1,2-Dichloroethane` | `Response` |
| `Area_Trichloroethylene` | `Trichloroethylene` | `Response` |
| `Area_1,2-Dichloropropane` | `1,2-Dichloropropane` | `Response` |
| `Area_cis-1,3-Dichloropropene` | `cis-1,3-Dichloropropene` | `Response` |
| `Area_trans-1,3-Dichloropropene` | `trans-1,3-Dichloropropene` | `Response` |
| `Area_1,1,2-Trichloroethane` | `1,1,2-Trichloroethane` | `Response` |
| `Area_Tetrachloroethylene` | `Tetrachloroethylene` | `Response` |
| `Area_1,2-Dibromoethane` | `1,2-Dibromoethane` | `Response` |
| `Area_ChloroBenzene` | `ChloroBenzene` | `Response` |
| `Area_Ethylbenzene` | `EthylBenzene` | `Response` |
| `Area_p-Xylene` | `m/p-Xylene` | `Response` |
| `Area_Styrene` | `Styrene` | `Response` |
| `Area_o-Xylene` | `o-Xylene` | `Response` |
| `Area_1,1,2,2-Tetrachloroethane` | `1,1,2,2-Tetrachloroethane` | `Response` |
| `Area_1,3,5-TMB` | `1,3,5-TMB` | `Response` |
| `Area_1,3-Dichlorobenzene` | `1,3-DCB` | `Response` |
| `Area_1,4-Dichlorobenzene` | `1,4-DCB` | `Response` |
| `Area_1,2-Dichlorobenzene` | `1,2-DCB` | `Response` |
| `Area_1,2,4-TCB` | `1,2,4-TCB` | `Response` |
| `Area_HCBD` | `HCBD` | `Response` |

## 6. ppb 欄位來源

所有 `ppb_*` 欄位都由程式計算，不使用 Quant.txt 的 `Conc ppb`。

公式：

```text
ppb_* = RF.Area_* * PORT_RAW.Area_* / ACTIVE_STD_AVG.Area_*
```

若 RF、PORT Area、STD AVG Area 任一值為空，或 STD AVG Area 為 0，該 `ppb_*` 寫入 `NULL`。

| DB 欄位 | 對應 Area 欄位 | 來源公式 |
|---|---|---|
| `ppb_Acetone` | `Area_Acetone` | `RF.Area_Acetone * Area_Acetone / STD_AVG.Area_Acetone` |
| `ppb_IPA` | `Area_IPA` | `RF.Area_IPA * Area_IPA / STD_AVG.Area_IPA` |
| `ppb_Methlene` | `Area_Methlene` | `RF.Area_Methlene * Area_Methlene / STD_AVG.Area_Methlene` |
| `ppb_CNF` | `Area_CNF` | `RF.Area_CNF * Area_CNF / STD_AVG.Area_CNF` |
| `ppb_Cyclopentane` | `Area_Cyclopentane` | `RF.Area_Cyclopentane * Area_Cyclopentane / STD_AVG.Area_Cyclopentane` |
| `ppb_2-Butanone` | `Area_2-Butanone` | `RF.Area_2-Butanone * Area_2-Butanone / STD_AVG.Area_2-Butanone` |
| `ppb_Ethyl Acetate` | `Area_Ethyl Acetate` | `RF.Area_Ethyl Acetate * Area_Ethyl Acetate / STD_AVG.Area_Ethyl Acetate` |
| `ppb_Benzene` | `Area_Benzene` | `RF.Area_Benzene * Area_Benzene / STD_AVG.Area_Benzene` |
| `ppb_Carbon Tetrachloride` | `Area_Carbon Tetrachloride` | `RF.Area_Carbon Tetrachloride * Area_Carbon Tetrachloride / STD_AVG.Area_Carbon Tetrachloride` |
| `ppb_Toluene` | `Area_Toluene` | `RF.Area_Toluene * Area_Toluene / STD_AVG.Area_Toluene` |
| `ppb_1,2,4-TMB` | `Area_1,2,4-TMB` | `RF.Area_1,2,4-TMB * Area_1,2,4-TMB / STD_AVG.Area_1,2,4-TMB` |
| `ppb_Chlorobenzene-D5` | `Area_Chlorobenzene-D5` | `RF.Area_Chlorobenzene-D5 * Area_Chlorobenzene-D5 / STD_AVG.Area_Chlorobenzene-D5` |
| `ppb_Freon114` | `Area_Freon114` | `RF.Area_Freon114 * Area_Freon114 / STD_AVG.Area_Freon114` |
| `ppb_1,1-Dichloroethene` | `Area_1,1-Dichloroethene` | `RF.Area_1,1-Dichloroethene * Area_1,1-Dichloroethene / STD_AVG.Area_1,1-Dichloroethene` |
| `ppb_Freon113` | `Area_Freon113` | `RF.Area_Freon113 * Area_Freon113 / STD_AVG.Area_Freon113` |
| `ppb_1,1-Dichloroethane` | `Area_1,1-Dichloroethane` | `RF.Area_1,1-Dichloroethane * Area_1,1-Dichloroethane / STD_AVG.Area_1,1-Dichloroethane` |
| `ppb_cis-1,2-Dichloroethene` | `Area_cis-1,2-Dichloroethene` | `RF.Area_cis-1,2-Dichloroethene * Area_cis-1,2-Dichloroethene / STD_AVG.Area_cis-1,2-Dichloroethene` |
| `ppb_Freon20` | `Area_Freon20` | `RF.Area_Freon20 * Area_Freon20 / STD_AVG.Area_Freon20` |
| `ppb_1,1,1-Trichloroethane` | `Area_1,1,1-Trichloroethane` | `RF.Area_1,1,1-Trichloroethane * Area_1,1,1-Trichloroethane / STD_AVG.Area_1,1,1-Trichloroethane` |
| `ppb_1,2-Dichloroethane` | `AREA_1,2-Dichloroethane` | `RF.AREA_1,2-Dichloroethane * AREA_1,2-Dichloroethane / STD_AVG.AREA_1,2-Dichloroethane` |
| `ppb_Trichloroethylene` | `Area_Trichloroethylene` | `RF.Area_Trichloroethylene * Area_Trichloroethylene / STD_AVG.Area_Trichloroethylene` |
| `ppb_1,2-Dichloropropane` | `Area_1,2-Dichloropropane` | `RF.Area_1,2-Dichloropropane * Area_1,2-Dichloropropane / STD_AVG.Area_1,2-Dichloropropane` |
| `ppb_cis-1,3-Dichloropropene` | `Area_cis-1,3-Dichloropropene` | `RF.Area_cis-1,3-Dichloropropene * Area_cis-1,3-Dichloropropene / STD_AVG.Area_cis-1,3-Dichloropropene` |
| `ppb_trans-1,3-Dichloropropene` | `Area_trans-1,3-Dichloropropene` | `RF.Area_trans-1,3-Dichloropropene * Area_trans-1,3-Dichloropropene / STD_AVG.Area_trans-1,3-Dichloropropene` |
| `ppb_1,1,2-Trichloroethane` | `Area_1,1,2-Trichloroethane` | `RF.Area_1,1,2-Trichloroethane * Area_1,1,2-Trichloroethane / STD_AVG.Area_1,1,2-Trichloroethane` |
| `ppb_Tetrachloroethylene` | `Area_Tetrachloroethylene` | `RF.Area_Tetrachloroethylene * Area_Tetrachloroethylene / STD_AVG.Area_Tetrachloroethylene` |
| `ppb_1,2-Dibromoethane` | `Area_1,2-Dibromoethane` | `RF.Area_1,2-Dibromoethane * Area_1,2-Dibromoethane / STD_AVG.Area_1,2-Dibromoethane` |
| `ppb_ChloroBenzene` | `Area_ChloroBenzene` | `RF.Area_ChloroBenzene * Area_ChloroBenzene / STD_AVG.Area_ChloroBenzene` |
| `ppb_Ethylbenzene` | `Area_Ethylbenzene` | `RF.Area_Ethylbenzene * Area_Ethylbenzene / STD_AVG.Area_Ethylbenzene` |
| `ppb_p-Xylene` | `Area_p-Xylene` | `RF.Area_p-Xylene * Area_p-Xylene / STD_AVG.Area_p-Xylene` |
| `ppb_Styrene` | `Area_Styrene` | `RF.Area_Styrene * Area_Styrene / STD_AVG.Area_Styrene` |
| `ppb_o-Xylene` | `Area_o-Xylene` | `RF.Area_o-Xylene * Area_o-Xylene / STD_AVG.Area_o-Xylene` |
| `ppb_1,1,2,2-Tetrachloroethane` | `Area_1,1,2,2-Tetrachloroethane` | `RF.Area_1,1,2,2-Tetrachloroethane * Area_1,1,2,2-Tetrachloroethane / STD_AVG.Area_1,1,2,2-Tetrachloroethane` |
| `ppb_1,3,5-TMB` | `Area_1,3,5-TMB` | `RF.Area_1,3,5-TMB * Area_1,3,5-TMB / STD_AVG.Area_1,3,5-TMB` |
| `ppb_1,3-Dichlorobenzene` | `Area_1,3-Dichlorobenzene` | `RF.Area_1,3-Dichlorobenzene * Area_1,3-Dichlorobenzene / STD_AVG.Area_1,3-Dichlorobenzene` |
| `ppb_1,4-Dichlorobenzene` | `Area_1,4-Dichlorobenzene` | `RF.Area_1,4-Dichlorobenzene * Area_1,4-Dichlorobenzene / STD_AVG.Area_1,4-Dichlorobenzene` |
| `ppb_1,2-Dichlorobenzene` | `Area_1,2-Dichlorobenzene` | `RF.Area_1,2-Dichlorobenzene * Area_1,2-Dichlorobenzene / STD_AVG.Area_1,2-Dichlorobenzene` |
| `ppb_1,2,4-TCB` | `Area_1,2,4-TCB` | `RF.Area_1,2,4-TCB * Area_1,2,4-TCB / STD_AVG.Area_1,2,4-TCB` |
| `ppb_HCBD` | `Area_HCBD` | `RF.Area_HCBD * Area_HCBD / STD_AVG.Area_HCBD` |

## 7. RT 欄位來源

所有 `RT_*` 欄位都來自 Quant.txt compound 表格中的 `R.T.` 欄位。

若 Quant.txt 找不到對應 compound，該欄位寫入 `NULL`。

| DB 欄位 | Quant compound 名稱 | Quant 欄位 |
|---|---|---|
| `RT_Acetone` | `Acetone` | `R.T.` |
| `RT_IPA` | `IPA` | `R.T.` |
| `RT_Methlene` | `Methylene Chloride` | `R.T.` |
| `RT_CNF` | `CNF` | `R.T.` |
| `RT_Cyclopentane` | `Cyclopentane` | `R.T.` |
| `RT_2-Butanone` | `2-Butanone` | `R.T.` |
| `RT_Ethyl Acetate` | `Ethyl Acetate` | `R.T.` |
| `RT_Benzene` | `Benzene` | `R.T.` |
| `RT_Carbon Tetrachloride` | `Carbon Tetrachloride` | `R.T.` |
| `RT_Toluene` | `Toluene` | `R.T.` |
| `RT_1,2,4-TMB` | `1,2,4-TMB` | `R.T.` |
| `RT_Chlorobenzene-D5` | `Chlorobenzene-D5` | `R.T.` |
| `RT_Freon114` | `Freon114` | `R.T.` |
| `RT_1,1-Dichloroethene` | `1,1-Dichloroethene` | `R.T.` |
| `RT_Freon113` | `Freon113` | `R.T.` |
| `RT_1,1-Dichloroethane` | `1,1-Dichloroethane` | `R.T.` |
| `RT_cis-1,2-Dichloroethene` | `cis-1,2-Dichloroethene` | `R.T.` |
| `RT_Freon20` | `Freon20` | `R.T.` |
| `RT_1,1,1-Trichloroethane` | `1,1,1-Trichloroethane` | `R.T.` |
| `RT_1,2-Dichloroethane` | `1,2-Dichloroethane` | `R.T.` |
| `RT_Trichloroethylene` | `Trichloroethylene` | `R.T.` |
| `RT_1,2-Dichloropropane` | `1,2-Dichloropropane` | `R.T.` |
| `RT_cis-1,3-Dichloropropene` | `cis-1,3-Dichloropropene` | `R.T.` |
| `RT_trans-1,3-Dichloropropene` | `trans-1,3-Dichloropropene` | `R.T.` |
| `RT_1,1,2-Trichloroethane` | `1,1,2-Trichloroethane` | `R.T.` |
| `RT_Tetrachloroethylene` | `Tetrachloroethylene` | `R.T.` |
| `RT_1,2-Dibromoethane` | `1,2-Dibromoethane` | `R.T.` |
| `RT_ChloroBenzene` | `ChloroBenzene` | `R.T.` |
| `RT_Ethylbenzene` | `EthylBenzene` | `R.T.` |
| `RT_p-Xylene` | `m/p-Xylene` | `R.T.` |
| `RT_Styrene` | `Styrene` | `R.T.` |
| `RT_o-Xylene` | `o-Xylene` | `R.T.` |
| `RT_1,1,2,2-Tetrachloroethane` | `1,1,2,2-Tetrachloroethane` | `R.T.` |
| `RT_1,3,5-TMB` | `1,3,5-TMB` | `R.T.` |
| `RT_1,3-Dichlorobenzene` | `1,3-DCB` | `R.T.` |
| `RT_1,4-Dichlorobenzene` | `1,4-DCB` | `R.T.` |
| `RT_1,2-Dichlorobenzene` | `1,2-DCB` | `R.T.` |
| `RT_1,2,4-TCB` | `1,2,4-TCB` | `R.T.` |
| `RT_HCBD` | `HCBD` | `R.T.` |

## 8. 範例

以 `PORT 2[20251119 1132]_023.D\Quant.txt` 為例：

| 欄位 | 結果 |
|---|---|
| `ID` | `20251119023` |
| `AnlzTime` | `2025-11-19 11:32:00` |
| `Port` | `PORT 2` |
| `SampleNo` | `23` |
| `LotNo` | `20251117006` |
| `Description` | `port 2  023  1078>  #20251117006` |
| `Area_IPA` | Quant.txt 中 IPA 的 `Response`，例如 `3654182` |
| `RT_IPA` | Quant.txt 中 IPA 的 `R.T.`，例如 `2.154` |
| `ppb_IPA` | `RF.Area_IPA * Area_IPA / ACTIVE_STD_AVG.Area_IPA` |

## 9. 注意事項

1. `ZZ_NF_GAS_QC_LOT_PORT` 是 raw table；AVG、PPB、RPD 會寫到其他表。
2. `ppb_*` 是程式依 RF 與 STD AVG 換算，不是 Quant.txt 的 `Conc ppb`。
3. 若同一檔案重跑，程式以 `LotNo + Port + SampleNo + DataFilename` 判斷 raw 是否已存在，避免重複寫入。
4. 成功寫入後，程式會嘗試把 `.D` 資料夾搬到日期資料夾底下的 `Done`；若檔案被外部程式鎖住，DB 不會 rollback，只會記錄 warning。
5. `ZZ_NF_GAS_MFG_LOT` 是人工維護主檔，程式只查詢，不更新。

## 10. 計算表共同規則

以下規則適用於：

| 資料表 | 計算來源 |
|---|---|
| `ZZ_NF_GAS_QC_LOT_STD_AVG` | STD 連續群組最後兩筆 raw；寫入時清空舊資料，只保留最新一筆 |
| `ZZ_NF_GAS_QC_LOT_STD_RPD` | STD 連續群組最後兩筆 raw |
| `ZZ_NF_GAS_QC_LOT_PORT_AVG` | PORT 連續群組最後兩筆 raw；寫入時清空舊資料，只保留最新一筆 |
| `ZZ_NF_GAS_QC_LOT_PORT_PPB` | 本次剛算出的 PORT AVG + `ZZ_NF_GAS_QC_RF` + 依 PORT 時間點從 STD raw 即時計算的 `ACTIVE_STD_AVG`；同一 `ID + LotNo + Port` 會替換成最新資料 |
| `ZZ_NF_GAS_QC_LOT_PORT_RPD` | PORT 連續群組最後兩筆 raw |

### 10.1 連續群組最後兩筆 raw 的挑選規則

程式會先把同一輪、同一天資料夾內的穩定 Quant 檔案整批解析，並依時間排序後切成連續群組。同一群組需符合：

- 來源類型相同，例如 STD 或 PORT。
- `Port` 相同。
- `LotNo` 相同。

AVG、RPD、PORT PPB 都在群組結束時產生，使用該群組最後兩筆 raw。DB 查詢會以群組最後一筆時間為界線，取該時間以前的最近兩筆 raw。

| 類型 | 查詢條件 | 排序方式 |
|---|---|---|
| STD | `LotNo + Port + SampleType`，其中 `Port = STD`，且 `AnlzTime <= 目前處理檔案的 AnlzTime` | `AnlzTime DESC`、`CREATE_TIME DESC`、`SID DESC`，取前兩筆 |
| PORT | `LotNo + Port + SampleType`，且 `AnlzTime <= 目前處理檔案的 AnlzTime` | `AnlzTime DESC`、`CREATE_TIME DESC`、`SID DESC`，取前兩筆；目前不加 `SampleNo` |

程式取到最新兩筆後，會轉成：

| 名稱 | 意義 |
|---|---|
| `ID1` | 較早的那一筆 raw `ID` |
| `ID2` | 較新的那一筆 raw `ID` |

### 10.2 計算表固定欄位來源

| 欄位 | 來源或規則 | 適用表 |
|---|---|---|
| `SID` | 程式寫入該計算表時產生 | 全部計算表 |
| `ID` | 依計算類型產生，詳見各表章節 | 全部計算表 |
| `AnlzTime` | 沿用第二筆 raw 或 PORT AVG 的 `AnlzTime` | 全部計算表 |
| `Inst` | 沿用第二筆 raw 或 PORT AVG 的 `Inst` | 全部計算表 |
| `Port` | 沿用第二筆 raw 或 PORT AVG 的 `Port` | 全部計算表 |
| `si0_id` | 沿用第二筆 raw 或 PORT AVG 的 `si0_id` | 全部計算表 |
| `SampleNo` | 沿用第二筆 raw 或 PORT AVG 的 `SampleNo` | 全部計算表 |
| `LotNo` | 沿用第二筆 raw 或 PORT AVG 的 `LotNo` | 全部計算表 |
| `DataFilename` | 沿用第二筆 raw 或 PORT AVG 的 `DataFilename` | 全部計算表 |
| `DataFilepath` | 沿用第二筆 raw 或 PORT AVG 的 `DataFilepath` | 全部計算表 |
| `PCName` | 沿用第二筆 raw 或 PORT AVG 的 `PCName` | 全部計算表 |
| `Container` | 沿用第二筆 raw 或 PORT AVG 的 `Container` | 全部計算表 |
| `Description` | 沿用第二筆 raw 或 PORT AVG 的 `Description` | 全部計算表 |
| `EMVolts` | 沿用第二筆 raw 或 PORT AVG 的 `EMVolts` | 全部計算表 |
| `RelativeEM` | 沿用第二筆 raw 或 PORT AVG 的 `RelativeEM` | 全部計算表 |
| `SampleName` | 沿用第二筆 raw 或 PORT AVG 的 `SampleName` | 全部計算表 |
| `SampleType` | 沿用第二筆 raw 或 PORT AVG 的 `SampleType` | 全部計算表 |
| `ID1` | 參與計算的第一筆 raw `ID`；PORT PPB 沿用 PORT AVG 的 `ID1` | 全部計算表 |
| `ID2` | 參與計算的第二筆 raw `ID`；PORT PPB 沿用 PORT AVG 的 `ID2` | 全部計算表 |
| `CREATE_USER` | 沿用第二筆 raw 或 PORT AVG 的 `CREATE_USER` | 全部計算表 |
| `CREATE_TIME` | 沿用第二筆 raw 或 PORT AVG 的 `CREATE_TIME` | 全部計算表 |
| `EDIT_USER` | 沿用第二筆 raw 或 PORT AVG 的 `EDIT_USER`；目前通常為 `NULL` | 全部計算表 |
| `EDIT_TIME` | 沿用第二筆 raw 或 PORT AVG 的 `EDIT_TIME`；目前通常為 `NULL` | 全部計算表 |

`ZZ_NF_GAS_QC_LOT_STD_AVG` 與 `ZZ_NF_GAS_QC_LOT_PORT_AVG` 是最新狀態表，不保留歷史。每次產生新的 AVG 時，程式會先刪除該 AVG 表所有舊資料，再插入最新一筆 AVG。

計算表的一般查重條件是 `ID + LotNo + Port + DataFilename`。加入 `DataFilename` 是因為 raw `ID` 目前使用 `yyyyMMdd + SampleNo`，同一天同 SampleNo 可能有多筆 raw。

`ZZ_NF_GAS_QC_LOT_PORT_PPB` 使用 `ppb(si0_id)` 作為 `ID`。同一個 `ID + LotNo + Port` 重算時，程式會先刪除舊 PPB，再插入最新 PPB，避免同一 PORT/LOT 因前面兩筆先算過而保留舊值。

## 11. ZZ_NF_GAS_QC_LOT_STD_AVG 欄位說明

`ZZ_NF_GAS_QC_LOT_STD_AVG` 是 STD 連續群組最後兩筆 raw 的 Area 平均結果。

此表是 snapshot table，永遠只保留最新一筆 STD AVG。新的 STD AVG 產生時，舊資料會先被刪除。

| 欄位或欄位群組 | 來源或公式 |
|---|---|
| `ID` | `AVG(ID1:ID2)` |
| `ID1` | 參與平均的第一筆 STD raw `ID` |
| `ID2` | 參與平均的第二筆 STD raw `ID` |
| 固定欄位 | 沿用第二筆 STD raw，詳見 10.2 |
| `Area_*` | `(STD_RAW_1.Area_* + STD_RAW_2.Area_*) / 2` |
| `CREATE_USER`、`CREATE_TIME` | 沿用第二筆 STD raw |
| `EDIT_USER`、`EDIT_TIME` | 沿用第二筆 STD raw；目前通常為 `NULL` |

若兩筆 raw 任一筆的同一個 `Area_*` 為 `NULL`，該 AVG `Area_*` 寫入 `NULL`。

目前 AVG 表的 `Area_*` 欄位需保留小數，DB schema 已調整為 `decimal(18,6)`，避免 Excel 中 `.5` 的平均值被四捨五入成整數。

## 12. ZZ_NF_GAS_QC_LOT_STD_RPD 欄位說明

`ZZ_NF_GAS_QC_LOT_STD_RPD` 是 STD 連續群組最後兩筆 raw 的 RPD 結果。

| 欄位或欄位群組 | 來源或公式 |
|---|---|
| `ID` | `RPD(ID1:ID2)` |
| `ID1` | 參與 RPD 的第一筆 STD raw `ID` |
| `ID2` | 參與 RPD 的第二筆 STD raw `ID` |
| 固定欄位 | 沿用第二筆 STD raw，詳見 10.2 |
| `Area_*` | `(MAX(STD_RAW_1.Area_*, STD_RAW_2.Area_*) - MIN(STD_RAW_1.Area_*, STD_RAW_2.Area_*)) / ((MAX + MIN) / 2)` |
| `CREATE_USER`、`CREATE_TIME` | 沿用第二筆 STD raw |
| `EDIT_USER`、`EDIT_TIME` | 沿用第二筆 STD raw；目前通常為 `NULL` |

若兩筆 raw 任一筆的同一個 `Area_*` 為 `NULL`，或 `(MAX + MIN) / 2 = 0`，該 RPD `Area_*` 寫入 `NULL`。

## 13. ZZ_NF_GAS_QC_LOT_PORT_AVG 欄位說明

`ZZ_NF_GAS_QC_LOT_PORT_AVG` 是 PORT 連續群組最後兩筆 raw 的 Area 平均結果。

此表是 snapshot table，永遠只保留最新一筆 PORT AVG。新的 PORT AVG 產生時，舊資料會先被刪除。

| 欄位或欄位群組 | 來源或公式 |
|---|---|
| `ID` | `AVG(ID1:ID2)` |
| `ID1` | 參與平均的第一筆 PORT raw `ID` |
| `ID2` | 參與平均的第二筆 PORT raw `ID` |
| 固定欄位 | 沿用第二筆 PORT raw，詳見 10.2 |
| `Area_*` | `(PORT_RAW_1.Area_* + PORT_RAW_2.Area_*) / 2` |
| `ppb_*` | 目前程式不計算 PORT AVG 的 `ppb_*`，寫入 `NULL` |
| `RT_*` | 目前程式不計算 PORT AVG 的 `RT_*`，寫入 `NULL` |
| `CREATE_USER`、`CREATE_TIME` | 沿用第二筆 PORT raw |
| `EDIT_USER`、`EDIT_TIME` | 沿用第二筆 PORT raw；目前通常為 `NULL` |

若兩筆 raw 任一筆的同一個 `Area_*` 為 `NULL`，該 AVG `Area_*` 寫入 `NULL`。

目前 AVG 表的 `Area_*` 欄位需保留小數，DB schema 已調整為 `decimal(18,6)`，避免 Excel 中 `.5` 的平均值被四捨五入成整數。

## 14. ZZ_NF_GAS_QC_LOT_PORT_PPB 欄位說明

`ZZ_NF_GAS_QC_LOT_PORT_PPB` 是 PORT AVG 依 RF 與該 PORT 時間點有效 STD AVG 換算後的 PPB 結果。

| 欄位或欄位群組 | 來源或公式 |
|---|---|
| `ID` | `ppb(si0_id)`，其中 `si0_id` 來自 PORT AVG |
| `ID1` | 沿用 PORT AVG 的 `ID1` |
| `ID2` | 沿用 PORT AVG 的 `ID2` |
| 固定欄位 | 沿用 PORT AVG，詳見 10.2 |
| `Area_*` | `RF.Area_* * PORT_AVG.Area_* / ACTIVE_STD_AVG.Area_*` |
| `CREATE_USER`、`CREATE_TIME` | 沿用 PORT AVG |
| `EDIT_USER`、`EDIT_TIME` | 沿用 PORT AVG；目前通常為 `NULL` |

注意：此表沒有獨立的 `ppb_*` 欄位；PPB 計算結果是寫在 `Area_*` 欄位。

若 RF、PORT AVG、ACTIVE STD AVG 任一來源為 `NULL`，或 `ACTIVE_STD_AVG.Area_* = 0`，該 `Area_*` 寫入 `NULL`。

`ACTIVE_STD_AVG` 不直接讀 `ZZ_NF_GAS_QC_LOT_STD_AVG` 最後快照。程式會用 PORT AVG 的 `AnlzTime` 從 STD raw 表找出 `AnlzTime <= PORT時間` 的最近兩筆，並用 `(STD_RAW_1.Area_* + STD_RAW_2.Area_*) / 2` 即時計算分母。

## 15. ZZ_NF_GAS_QC_LOT_PORT_RPD 欄位說明

`ZZ_NF_GAS_QC_LOT_PORT_RPD` 是 PORT 連續群組最後兩筆 raw 的 RPD 結果。

| 欄位或欄位群組 | 來源或公式 |
|---|---|
| `ID` | `RPD(ID1:ID2)` |
| `ID1` | 參與 RPD 的第一筆 PORT raw `ID` |
| `ID2` | 參與 RPD 的第二筆 PORT raw `ID` |
| 固定欄位 | 沿用第二筆 PORT raw，詳見 10.2 |
| `Area_*` | `(MAX(PORT_RAW_1.Area_*, PORT_RAW_2.Area_*) - MIN(PORT_RAW_1.Area_*, PORT_RAW_2.Area_*)) / ((MAX + MIN) / 2)` |
| `CREATE_USER`、`CREATE_TIME` | 沿用第二筆 PORT raw |
| `EDIT_USER`、`EDIT_TIME` | 沿用第二筆 PORT raw；目前通常為 `NULL` |

若兩筆 raw 任一筆的同一個 `Area_*` 為 `NULL`，或 `(MAX + MIN) / 2 = 0`，該 RPD `Area_*` 寫入 `NULL`。

## 16. 化合物欄位套用規則

本文件中的 `Area_*`、`ppb_*`、`RT_*` 代表第 5、6、7 節列出的所有化合物欄位。

| 欄位群組 | 套用方式 |
|---|---|
| `Area_*` | 每個 compound suffix 各自獨立計算，例如 `Area_IPA` 只使用 IPA 的來源值 |
| `ppb_*` | 只有 `ZZ_NF_GAS_QC_LOT_PORT` raw 與 `ZZ_NF_GAS_QC_LOT_PORT_AVG` schema 具備此欄位群組；PORT AVG 目前寫入 `NULL` |
| `RT_*` | 只有 `ZZ_NF_GAS_QC_LOT_PORT` raw 與 `ZZ_NF_GAS_QC_LOT_PORT_AVG` schema 具備此欄位群組；PORT AVG 目前寫入 `NULL` |
