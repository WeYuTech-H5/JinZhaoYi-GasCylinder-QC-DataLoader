# Quant Parser：解析與 DB Mapping

這份文件說明 `Quant.txt` 如何被 `QuantParser` 解析，並透過 `RawRowFactory`、`CompoundMap`、`DapperRepository` 對應到 SQL Server 的 Gas QC table。

主要程式位置：

| Purpose | File |
| --- | --- |
| 解析 `Quant.txt` | `Services/Service/QuantParser.cs` |
| Quant compound 名稱與 DB 欄位對應 | `DataModels/CompoundMap.cs` |
| Parsed data 轉成 DB row | `Services/Service/RawRowFactory.cs` |
| 寫入 SQL Server | `Services/Infrastructure/DapperRepository.cs` |
| QC row model | `DataModels/QcDataRow.cs` |
| LOT 主檔 model | `DataModels/MfgLot.cs` |

## 1. 整體資料流

```text
Quant.txt
  ↓
QuantParser.ParseAsync()
  ↓
ParsedQuantFile
  ↓
RawRowFactory.Create()
  ↓
QcDataRow
  ↓
DapperRepository.ExecuteImportAsync()
  ↓
dbo.ZZ_NF_GAS_QC_LOT_* tables
```

`QuantParser` 只負責解析文字檔，不負責 DB 寫入。DB 欄位組裝主要在 `RawRowFactory` 與 `DapperRepository.BuildValues()`。

## 2. Quant.txt Header Mapping

`QuantParser` 會讀取以下必要 header：

```text
Data Path : D:\data\
Data File : 0001.D
Acq On    : 19 Nov 2025  09:47
Sample    : Sample 1
Misc      : port 1  903  872>  #20251030001
```

對應關係如下：

| Quant.txt source | ParsedQuantFile | QcDataRow / DB column | 說明 |
| --- | --- | --- | --- |
| `Data Path` | `DataPath` | `DataFilepath` | 保留 Quant 原始 `Data Path`。 |
| `Data File` | `DataFile` | `DataFilename` | 會與來源 `.D` folder name 組合。 |
| `Acq On` | `AcquiredAt` | `AnlzTime` | 使用 `en-US` culture 解析日期時間。 |
| `Sample` | `Sample` | 目前不直接寫入主要 DB 欄位 | 保留在 parsed model，方便追查。 |
| `Misc` | `Misc` | `Description` | 完整 Misc 文字寫入 DB。 |
| `Misc` 最後 `#` 後文字 | `LotNo` | `LotNo` | 例如 `#20251030001` → `20251030001`。 |
| `.D` folder name | `SampleNo` | `SampleNo` | 例如 `xxx_903.D` → `903`；`xxx_V006.D` → `6`。 |
| Source folder `STD` / `PORT N` | `Source.Port` | `Port` | `STD` 寫 STD table；`PORT N` 寫 PORT table。 |

## 3. SampleNo 解析規則

`SampleNo` 不是從 `Quant.txt` 內容取得，而是從 `Quant.txt` 所在的 `.D` 資料夾名稱解析。

範例：

| `.D` folder name | SampleNo |
| --- | ---: |
| `STD[20251119 1032]_903.D` | `903` |
| `PORT 2[20251119 1147]_023.D` | `23` |
| `ABC_V006.D` | `6` |

解析失敗時，`QuantParser` 會丟出 `InvalidDataException`，該檔案不會繼續匯入。

## 4. LotNo 解析規則

`LotNo` 來自 `Misc` 欄位最後的 `#` marker。

範例：

```text
Misc : port 1  903  872>  #20251030001
```

解析結果：

```text
LotNo = 20251030001
Description = "port 1  903  872>  #20251030001"
```

若 `Misc` 找不到 `#LotNo` 格式，`QuantParser` 會丟出 `InvalidDataException`。

## 5. Compound Row 解析

Quant compound 區塊範例：

```text
Compound                   R.T. QIon  Response  Conc Units Dev(Min)
--------------------------------------------------------------------------
 2) IPA                    2.164   45  4534252   143.41 ppb       87
 3) Acetone                2.134   58  1204986    68.36 ppb       98
30) ChloroBenzene.         5.845  112 11315788    90.51 ppb       89
```

`QuantParser` 會解析：

| Quant column | Parsed model | DB target |
| --- | --- | --- |
| Compound name | `QuantCompound.Analyte` | 透過 `CompoundMap` 決定欄位 suffix。 |
| `R.T.` | `QuantCompound.RetentionTime` | `RT_*` |
| `Response` | `QuantCompound.Response` | `Area_*` |
| `Conc` | `QuantCompound.Concentration` | raw 建立階段不直接寫 DB。 |

`Conc` 可能是數字、`N.D.` 或 `No Calib`。若不是數字，`Concentration` 會是 `null`。

## 6. CompoundMap 命名規則

`CompoundMap` 集中管理 Quant compound name 與 DB 欄位名稱。

一般規則：

```text
Suffix = IPA
Area column = Area_IPA
PPB column  = ppb_IPA
RT column   = RT_IPA
```

範例：

| Quant name | Suffix | Area column | RT column | PPB column |
| --- | --- | --- | --- | --- |
| `IPA` | `IPA` | `Area_IPA` | `RT_IPA` | `ppb_IPA` |
| `Acetone` | `Acetone` | `Area_Acetone` | `RT_Acetone` | `ppb_Acetone` |
| `Methylene Chloride` | `Methlene` | `Area_Methlene` | `RT_Methlene` | `ppb_Methlene` |
| `m/p-Xylene` | `p-Xylene` | `Area_p-Xylene` | `RT_p-Xylene` | `ppb_p-Xylene` |
| `ChloroBenzene.` | `ChloroBenzene` | `Area_ChloroBenzene` | `RT_ChloroBenzene` | `ppb_ChloroBenzene` |

注意：

- Quant compound name 會先 normalize。
- 前後空白會移除。
- 多個空白會壓成單一空白。
- 結尾句點會移除，例如 `ChloroBenzene.` 會視為 `ChloroBenzene`。
- 未定義於 `CompoundMap` 的 compound 會略過，不寫入 DB。

## 7. RawRowFactory：ParsedQuantFile 到 QcDataRow

`RawRowFactory.Create(parsed, lot, id)` 會建立一筆 raw `QcDataRow`。

固定欄位來源：

| QcDataRow property | DB column | Source |
| --- | --- | --- |
| `Id` | `ID` | `yyyyMMdd + SampleNo(3 digits)` |
| `AnlzTime` | `AnlzTime` | Quant `Acq On` |
| `Inst` | `Inst` | `Scheduler:InstrumentName` |
| `Port` | `Port` | source folder：`STD` / `PORT N` |
| `Si0Id` | `si0_id` | `ZZ_NF_GAS_MFG_LOT.si0_id` |
| `SampleNo` | `SampleNo` | `.D` folder name |
| `LotNo` | `LotNo` | Quant `Misc` 中的 `#LotNo` |
| `DataFilename` | `DataFilename` | `.D` folder + Quant `Data File` |
| `DataFilepath` | `DataFilepath` | Quant `Data Path` |
| `PcName` | `PCName` | `Environment.MachineName` |
| `Container` | `Container` | `ZZ_NF_GAS_MFG_LOT.Container` |
| `Description` | `Description` | Quant `Misc` |
| `EmVolts` | `EMVolts` | `ZZ_NF_GAS_MFG_LOT.EMVolts` |
| `RelativeEm` | `RelativeEM` | `ZZ_NF_GAS_MFG_LOT.RelativeEM` |
| `SampleName` | `SampleName` | `ZZ_NF_GAS_MFG_LOT.SamplName` |
| `SampleType` | `SampleType` | `ZZ_NF_GAS_MFG_LOT.SampleType`，缺值時使用 `Scheduler:SampleType` |
| `CreateUser` | `CREATE_USER` | `Scheduler:CreateUser` |
| `CreateTime` | `CREATE_TIME` | `DateTime.Now` |

Compound 欄位來源：

| QcDataRow dictionary | DB columns | Source |
| --- | --- | --- |
| `Areas[suffix]` | `Area_*` | Quant `Response` |
| `RetentionTimes[suffix]` | `RT_*` | Quant `R.T.` |
| `Ppbs[suffix]` | `ppb_*` | raw 建立階段通常不由 Quant 直接設定；PORT 會在計算階段填入。 |

## 8. DB 寫入欄位：BuildValues()

`DapperRepository.BuildValues()` 會把 `QcDataRow` 轉成 SQL insert columns。

基本欄位一定會寫：

```text
SID
ID
AnlzTime
Inst
Port
si0_id
SampleNo
LotNo
DataFilename
DataFilepath
PCName
Container
Description
EMVolts
RelativeEM
SampleName
SampleType
CREATE_USER
CREATE_TIME
EDIT_USER
EDIT_TIME
```

Compound 欄位依 table 寫入選項決定：

| Option | Columns |
| --- | --- |
| `includePpb=true` | 寫入所有 `ppb_*` |
| `includeRt=true` | 寫入所有 `RT_*` |
| `includeIdRefs=true` | 寫入 `ID1`、`ID2` |

`Area_*` 會固定依 `CompoundMap.Analytes` 全部加入。若某 compound 沒有資料，欄位值會是 `NULL`。

## 9. 寫入哪張 DB Table

來源由 top folder 決定：

| Source folder | SourceKind | Raw table |
| --- | --- | --- |
| `STD` | `Std` | `ZZ_NF_GAS_QC_LOT_STD` |
| `PORT N` | `Port` | `ZZ_NF_GAS_QC_LOT_PORT` |
| `PROT N` | `Port` | `ZZ_NF_GAS_QC_LOT_PORT` |

`PROT N` 會被視為 `PORT N`，用來容忍來源資料夾 typo。

完整 table flow：

```text
STD raw
  → ZZ_NF_GAS_QC_LOT_STD
  → ZZ_NF_GAS_QC_LOT_STD_AVG
  → ZZ_NF_GAS_QC_LOT_STD_QC
  → ZZ_NF_GAS_QC_LOT_STD_RPD

PORT raw
  → ZZ_NF_GAS_QC_LOT_PORT
  → ZZ_NF_GAS_QC_LOT_PORT_AVG
  → ZZ_NF_GAS_QC_LOT_PORT_PPB
  → ZZ_NF_GAS_QC_LOT_PORT_RPD
```

## 10. STD Raw vs PORT Raw 差異

STD raw：

- 來源為 `STD` folder。
- 寫入 `ZZ_NF_GAS_QC_LOT_STD`。
- `Area_*` 來自 Quant `Response`。
- `RT_*` 來自 Quant `R.T.`。
- raw 建立時不使用 Quant `Conc ppb`。

PORT raw：

- 來源為 `PORT N` 或 `PROT N` folder。
- 寫入 `ZZ_NF_GAS_QC_LOT_PORT`。
- `Area_*` 來自 Quant `Response`。
- `RT_*` 來自 Quant `R.T.`。
- `ppb_*` 由 `CalculationService.ApplyPortRawPpb()` 使用 RF 與 active STD AVG 計算。

PORT raw PPB 公式：

```text
PORT raw ppb_* = RF.Area_* × PORT_RAW.Area_* / ACTIVE_STD_AVG.Area_*
```

## 11. LOT 與 RF 驗證

在寫入 DB 前，`ImportOrchestrator` 會先驗證：

| Validation | Source table | Fail behavior |
| --- | --- | --- |
| LOT 是否存在 | `ZZ_NF_GAS_MFG_LOT` | 任一 LOT 找不到，整批停止，不寫 DB。 |
| RF 是否存在 | `ZZ_NF_GAS_QC_RF` | 找不到可用 RF，整批停止，不寫 DB。 |

LOT 查詢使用：

```text
WHERE LotNo IN @LotNos
```

RF 查詢會依該批最早採樣時間找可用 RF：

```text
CAST(AnlzTime AS date) = @AsOfDate
OR AnlzTime <= @AsOf
OR AnlzTime IS NULL
```

## 12. 查重規則

Raw table 查重：

```text
LotNo + Port + SampleNo + DataFilename
```

計算 table 查重：

```text
ID + LotNo + Port + DataFilename
```

原因：

- raw `ID` 由 `yyyyMMdd + SampleNo` 組成。
- 同一天同一個 `SampleNo` 仍可能有不同檔案。
- 因此 raw 查重需要加上 `DataFilename`。

## 13. 單筆 Mapping 範例

Quant.txt：

```text
Data Path : D:\data\
Data File : 0001.D
Acq On    : 19 Nov 2025  09:47
Sample    : Sample 1
Misc      : port 1  903  872>  #20251030001

2) IPA    2.164   45  4534252   143.41 ppb
```

假設 `.D` folder：

```text
STD[20251119 0947]_903.D
```

解析結果：

| Target | Value |
| --- | --- |
| `ID` | `20251119903` |
| `AnlzTime` | `2025-11-19 09:47` |
| `SampleNo` | `903` |
| `LotNo` | `20251030001` |
| `Description` | `port 1  903  872>  #20251030001` |
| `DataFilepath` | `D:\data\` |
| `DataFilename` | `{data folder name}\0001.D` |
| `Area_IPA` | `4534252` |
| `RT_IPA` | `2.164` |

若來源 folder 是 `STD`：

```text
Insert into dbo.ZZ_NF_GAS_QC_LOT_STD
```

若來源 folder 是 `PORT 2`：

```text
Insert into dbo.ZZ_NF_GAS_QC_LOT_PORT
```

## 14. 常見注意事項

- `Quant.txt` 缺少必要 header 會直接失敗。
- `Acq On` 日期格式解析失敗會直接失敗。
- `Misc` 沒有 `#LotNo` 會直接失敗。
- `.D` folder name 無法解析 `SampleNo` 會直接失敗。
- 未在 `CompoundMap` 定義的 compound 會被略過。
- `Conc ppb` 不是 raw 寫入的主要來源。
- `Area_*` 來自 Quant `Response`，不是 `Conc`。
- `RT_*` 來自 Quant `R.T.`。
- PORT 的 `ppb_*` 由 RF、PORT raw、active STD AVG 計算。
- 找不到 LOT 或 RF 時，整批停止，避免部分寫入造成資料不一致。

