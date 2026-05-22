# COA 大卡 / 小卡欄位對應

本文說明目前程式匯出 COA 大卡、COA 小卡時，各欄位實際寫入來源。

## 共用資料來源

COA 大卡與小卡都不直接讀匯入計算表 `ZZ_NF_GAS_QC_LOT_PORT_PPB`，而是讀使用者成功匯出 Query2 Excel 後保存的 PPB history：

| 用途 | 資料表 | 查詢條件 |
| --- | --- | --- |
| COA 大卡 | `dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY` | `CAST(AnlzTime AS date) = batchDate` 且 `ExcelPpbExportId IN selectedIds` |
| COA 小卡 | `dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY` | `CAST(AnlzTime AS date) = batchDate` 且 `ExcelPpbExportId IN selectedIds` |

> 注意：COA 顯示的分析結果目前由 `QcDataRow.Areas` 取值。對 Query2 PPB row 來說，程式計算出的 PPB 結果會放在 `Areas`，保存到 history 表時寫入 `Area_*` 欄位。因此 COA 實際讀的是 `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_*`，語意上是 Query2 Excel 產生的 PPB 結果。

## COA 大卡

### 模板選擇

| 前端選項 | 判斷來源 | 使用模板 sheet |
| --- | --- | --- |
| 一般 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Container` 包含 `0.5` | `COA(500 mL)` |
| 一般 | `Container` 不含 `0.5` 或空值 | `COA(1 L)` |
| 亞東 | 不判斷 `Container` | `COA(亞東)` |

### 大卡欄位

| Excel 欄位 | 顯示內容 | 來源 |
| --- | --- | --- |
| `B10` | Product Name | 一般版依 `Container` 覆寫：`1L_Cylinder` = `NF-SEMI STD`、`0.5L_Cylinder` = `STD Gas PC for Semiconductor`。亞東版維持 `COA(亞東)` 模板值。 |
| `B11` | Product Number | 依 `SampleName` 前綴覆寫：`STD-N` / `STD-T` / `AZ` = `PG000-0006`、`TSMC` = `PG000-0016`、`VSMC` = `PG000-0010`、`STD-L` = `PG000-0100`。 |
| `B12` | Certification Date | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.AnlzTime`，格式 `yyyy/M/d` |
| `B13` | Expiration Date / Cylinder 到期日 | `0.5L_Cylinder` 優先用 `ZZ_NF_GAS_MFG_LOT_PARENT.ExpirationDate`，由 `ZZ_NF_GAS_MFG_LOT.Prod_Bomb1_LotNo = ZZ_NF_GAS_MFG_LOT_PARENT.LotNo` 對應；`1L_Cylinder` 用 `AnlzTime + 364 天`。查不到母瓶效期時 fallback 到 `AnlzTime + 364 天`。格式 `yyyy/M/d`。 |
| `B14` | Cylinder Size | 一般版依 `Container` 覆寫：`1L_Cylinder` = `8.87 cm*27.7 cm`、`0.5L_Cylinder` = `5 cm*35cm`。 |
| `B15` | Cylinder# | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.SampleName` |
| `B16` | Cylinder Pressure | 一般版依 `Container` 覆寫：`1L_Cylinder` = `1000 psi`、`0.5L_Cylinder` = `950 psi`。 |
| `E10` | Cylinder Valve | 固定 `1/4"VCR Female` |
| `E11` | Cylinder Volume | 一般版依 `Container` 覆寫：`1L_Cylinder` = `1000 mL`、`0.5L_Cylinder` = `500 mL`。 |
| `E12` | Cylinder Material | 固定 `Stainless` |
| `E13` | Gas Volume | 一般版依 `Container` 覆寫：`1L_Cylinder` = `70 L`、`0.5L_Cylinder` = `41 L`。 |
| `E14` | Balance Gas | 固定 `Nitrogen` |
| `E15` | Analytical Accuracy | 固定 `±10%` |
| `E16` | Specification | 一般版依 `Container` 覆寫：`1L_Cylinder` = `±15%`、`0.5L_Cylinder` = `±10%`。 |
| sheet 名稱 | `COA_{SampleName}` | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.SampleName` |

母瓶效期來源為 `ZZ_NF_GAS_MFG_LOT_PARENT`，其中 `LotNo` 是母瓶號，對應 `ZZ_NF_GAS_MFG_LOT.Prod_Bomb1_LotNo`。

### 大卡分析結果欄位

大卡會掃描模板 `C19:C57` 的 CAS Number，將 CAS Number 移除符號後對應附件三 TO14C 的 `reptID`，再用該 `reptID` 找到系統 compound suffix，最後把對應 history 欄位寫入同列 `E` 欄。

例如：`76-14-2` 會轉成 `76142`，對應附件三的 `Freon114`，因此讀取 `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Freon114` 後寫入同列 Result Conc.。

| 大卡 CAS Number | 附件三 ID | 大卡 Component 名稱 | Excel 結果欄 | 來源資料表欄位 |
| --- | --- | --- | --- | --- |
| `76-14-2` | `76142` | Dichlorotetrafluoroethane (FC-114) | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Freon114` |
| `75-35-4` | `75354` | 1,1-Dichloroethylene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,1-Dichloroethene` |
| `76-13-1` | `76131` | 1,1,2-Trichlorotrifluoroethane(FC-113) | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Freon113` |
| `75-09-2` | `75092` | Methylene Chloride | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Methlene` |
| `75-34-3` | `75343` | 1,1-Dichloroethane | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,1-Dichloroethane` |
| `156-59-2` | `156592` | cis-1,2-Dichloroethylene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_cis-1,2-Dichloroethene` |
| `67-66-3` | `67663` | Trichloromethane (HC-20) | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Freon20` |
| `71-55-6` | `71556` | 1,1,1-Trichloroethane | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,1,1-Trichloroethane` |
| `107-06-2` | `107062` | 1,2-Dichloroethane | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.AREA_1,2-Dichloroethane` |
| `71-43-2` | `71432` | Benzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Benzene` |
| `56-23-5` | `56235` | Carbon Tetrachloride | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Carbon Tetrachloride` |
| `79-01-6` | `79016` | Trichloroethylene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Trichloroethylene` |
| `78-87-5` | `78875` | 1,2-Dichloropropane | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,2-Dichloropropane` |
| `10061-01-5` | `10061015` | cis-1,3-Dichloropropylene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_cis-1,3-Dichloropropene` |
| `10061-02-6` | `10061026` | trans-1,3-Dichloropropylene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_trans-1,3-Dichloropropene` |
| `108-88-3` | `108883` | Toluene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Toluene` |
| `79-00-5` | `79005` | 1,1,2-Trichloroethane | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,1,2-Trichloroethane` |
| `127-18-4` | `127184` | Tetrachloroethylene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Tetrachloroethylene` |
| `106-93-4` | `106934` | 1,2-Dibromoethane | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,2-Dibromoethane` |
| `108-90-7` | `108907` | Chlorobenzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_ChloroBenzene` |
| `100-41-4` | `100414` | Ethyl Benzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Ethylbenzene` |
| `106-42-3/108-38-3` | `106423` | p&m Xylenes(mixed) | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_p-Xylene` |
| `100-42-5` | `100425` | Styrene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Styrene` |
| `95-47-6` | `95476` | o-Xylene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_o-Xylene` |
| `79-34-5` | `79345` | 1,1,2,2-Tetrachloroethane | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,1,2,2-Tetrachloroethane` |
| `108-67-8` | `108678` | 1,3,5-Trimethylbenzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,3,5-TMB` |
| `95-63-6` | `95636` | 1,2,4-Trimethylbenzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,2,4-TMB` |
| `541-73-1` | `541731` | 1,3-Dichlorobenzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,3-Dichlorobenzene` |
| `106-46-7` | `106467` | 1,4-Dichlorobenzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,4-Dichlorobenzene` |
| `95-50-1` | `95501` | 1,2-Dichlorobenzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,2-Dichlorobenzene` |
| `120-82-1` | `120821` | 1,2,4-Trichlorobenzene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,2,4-TCB` |
| `87-68-3` | `87683` | Hexachloro-1,3-Butadiene | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_HCBD` |
| `67-63-0` | `67630` | Isopropanol | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_IPA` |
| `67-64-1` | `67641` | Acetone | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Acetone` |
| `311-89-7` | `311897` | Perfluorotributylamine (HC-43) | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_CNF` |
| `78-93-3` | `78933` | 2-Butanone (MEK) | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_2-Butanone` |
| `141-78-6` | `141786` | Ethyl Acetate | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Ethyl Acetate` |
| `287-92-3` | `287923` | Cyclopentane | 同列 `E` 欄 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Cyclopentane` |

### 大卡模板保留欄位

下列內容目前不由程式覆寫，維持模板原值：頁首 logo、公司資訊、CAS Number、Requested Conc.、簽核欄、頁尾等。

## COA 小卡

### 小卡 sheet 與格數

| 項目 | 規則 |
| --- | --- |
| 模板 sheet | `COA(小卡).xlsx` 的 `Report(空白)` |
| sheet 名稱 | `COA小卡_{SampleName}`；同一筆超過 9 張時續頁為 `COA小卡_{SampleName}_2`、`_3`... |
| 小卡張數 | 前端 `cardsPerPage`，可輸入 1 以上整數；每張 sheet 最多 9 格，超過 9 會自動換頁 |
| 多筆資料 | 每筆 Excel PPB history row 依小卡張數產生 1 張或多張 sheet |
| 同一張 sheet 內容 | 每格都填同一筆資料；尾頁未使用的格子會移除內容與框線 |

### 小卡 9 格位置

| 格號 | SampleName | 第一個結果 cell | 母瓶 NO | QC 日期 | 有效期限 |
| --- | --- | --- | --- | --- | --- |
| 1 | `F6` | `F8` | `B19` | `F19` | `B20` |
| 2 | `O6` | `O8` | `K19` | `O19` | `K20` |
| 3 | `X6` | `X8` | `T19` | `X19` | `T20` |
| 4 | `F28` | `F30` | `B41` | `F41` | `B42` |
| 5 | `O28` | `O30` | `K41` | `O41` | `K42` |
| 6 | `X28` | `X30` | `T41` | `X41` | `T42` |
| 7 | `F50` | `F52` | `B63` | `F63` | `B64` |
| 8 | `O50` | `O52` | `K63` | `O63` | `K64` |
| 9 | `X50` | `X52` | `T63` | `X63` | `T64` |

### 小卡固定欄位來源

| 小卡欄位 | 顯示內容 | 來源 |
| --- | --- | --- |
| SampleName cell | 樣品名稱 / 鋼瓶編號 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.SampleName` |
| 母瓶 NO cell | `母瓶 NO.  {Prod_Bomb1_LotNo}` | `ZZ_NF_GAS_MFG_LOT.Prod_Bomb1_LotNo`，由 Excel PPB history row 的 `LotNo` / `si0_id` join MFG LOT 取得；舊資料查不到時才 fallback 到 `Scheduler:CsvExport:RawLotId` |
| QC 日期 cell | `QC: yyyy/M/d` | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.AnlzTime` |
| 有效期限 cell | `{SampleName}有效期限 : yyyy/M/d` | `SampleName` + `AnlzTime + 364 天` |

### 小卡分析結果欄位

每一格從「第一個結果 cell」往下連續填 9 個品項。

| 順序 | 小卡品項 | 相對位置 | 來源資料表欄位 |
| --- | --- | --- | --- |
| 1 | Acetone | 第一個結果 cell + 0 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Acetone` |
| 2 | IPA | 第一個結果 cell + 1 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_IPA` |
| 3 | CNF | 第一個結果 cell + 2 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_CNF` |
| 4 | Cyclopentane | 第一個結果 cell + 3 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Cyclopentane` |
| 5 | 2-Butanone | 第一個結果 cell + 4 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_2-Butanone` |
| 6 | Ethyl Acetate | 第一個結果 cell + 5 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Ethyl Acetate` |
| 7 | Benzene | 第一個結果 cell + 6 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Benzene` |
| 8 | Toluene | 第一個結果 cell + 7 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_Toluene` |
| 9 | 1,2,4-TMB | 第一個結果 cell + 8 列 | `ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY.Area_1,2,4-TMB` |

### 小卡模板保留欄位

小卡的 logo、公司名、欄位標題、簽名欄等都由模板保留，程式只覆寫上表列出的 SampleName、分析結果、母瓶 NO、QC 日期、有效期限；尾頁未使用的小卡格會移除內容與框線。
