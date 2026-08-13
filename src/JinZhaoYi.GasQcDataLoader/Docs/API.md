# API 參考

本文件對應目前 `Program.cs` 註冊的 26 個 endpoints。所有 endpoints 只有在 `Scheduler:DownloadApi:Enabled=true` 時存在。

目前程式沒有 Swagger／OpenAPI、認證或授權 middleware，而且 CORS 允許所有 origin。部署時應由內部網路、反向代理或其他基礎設施限制存取，不要直接暴露到公開網路。

## 共通規則

- 日期格式固定為 `yyyyMMdd`。
- 日期區間必須同時提供 `startDate`、`endDate`，且 `endDate >= startDate`。
- 部分 Excel PPB endpoints 仍接受單一 `batchDate`，等同開始與結束為同一天。
- 分頁預設 `page=1`、`pageSize=50`；`pageSize` 必須介於 1 到 500。
- 下載成功時通常回傳檔案；驗證錯誤回傳 `400` JSON，查無資料通常回傳 `404` JSON。
- Request／response property 使用 ASP.NET Core 預設 JSON 命名行為；範例採 camelCase。

## 完整 endpoint 清單

### 原始資料與 RF

| Method | Path | 用途 | 主要輸入 |
| --- | --- | --- | --- |
| `GET` | `/api/export-options` | 舊版單日 raw 選項清單。 | `batchDate` |
| `GET` | `/api/export-groups` | 依日期區間回傳分組後的 STD／PORT raw，供 Query2 選取。 | `startDate`、`endDate` |
| `GET` | `/api/rf-options` | 取得 RF 選項。 | 無 |
| `GET` | `/api/std-rf-source-options` | 查詢可作為 RF 匯入來源的 STD raw。 | `search`；可用 `page`／`pageSize`，未分頁時可用 `limit`，預設 200 |
| `POST` | `/api/rf/import-from-std` | 以 STD raw 呼叫外部 RF Extractor，轉換並保存 RF。 | JSON：`stdRawId` |

### 舊 PORT PPB 與 Excel PPB history

| Method | Path | 用途 | 主要輸入 |
| --- | --- | --- | --- |
| `GET` | `/api/port-ppb-options` | 舊版 `PORT_PPB` 選項。Quant raw-only 後不會有新自動 PPB。 | `batchDate`；選用 `page`、`pageSize` |
| `GET` | `/api/excel-ppb-options` | Query2 成功匯出後的 PPB history，供 CSV／COA 選取。 | `batchDate`，或 `startDate`＋`endDate`；選用 `search`、`exportSessionId`、`page`、`pageSize` |

### Dynamic AREA

| Method | Path | 用途 | 主要輸入 |
| --- | --- | --- | --- |
| `GET` | `/api/query2/dynamic-area-fields` | 取得 Dynamic AREA 欄位定義。 | `includeInactive`，預設 `true` |
| `POST` | `/api/query2/dynamic-area-fields` | 新增或 upsert Dynamic AREA 欄位。 | `columnName` 或 `displayName`；選用 `sortOrder`、`isActive` |
| `PUT` | `/api/query2/dynamic-area-fields/{fieldKey}` | 更新指定欄位。 | path `fieldKey`；JSON：`displayName`、`sortOrder`、`isActive` |
| `DELETE` | `/api/query2/dynamic-area-fields/{fieldKey}` | 將欄位設為 inactive，不是實體刪除。 | path `fieldKey` |
| `GET` | `/api/query2/dynamic-area-port-values` | 取得各 field／PORT 的 AREA 值。 | 無 |
| `PUT` | `/api/query2/dynamic-area-port-values` | 批次 upsert field／PORT AREA 值。 | JSON：`values[]` |

### QC 規則

| Method | Path | 用途 | 主要輸入 |
| --- | --- | --- | --- |
| `GET` | `/api/qc-result-settings` | 取得 0.5L／1L 壓力門檻與 analyte 濃度範圍。 | 無 |
| `PUT` | `/api/qc-result-settings` | 更新壓力與濃度 QC 規則。 | `pressureRules[]`、`concentrationRules[]` |

### Query2

| Method | Path | 用途 | 副作用 |
| --- | --- | --- | --- |
| `POST` | `/api/exports/query2-excel/preview` | 由 RF、STD raw、PORT raw 建立可編輯 preview。 | 無正式匯出寫入。 |
| `POST` | `/api/exports/query2-excel/recalculate` | 以 DB canonical data 重建基準，再套用 preview 的手動修改並重算。 | 無正式匯出寫入。 |
| `POST` | `/api/exports/query2-excel/from-preview` | 由已編輯 preview 正式產生 Excel。 | 保存 PPB history、QC 回寫、preview edit log。 |
| `POST` | `/api/exports/query2-excel` | 不經手動 preview，直接以選取資料建立並下載 Excel。 | 保存 PPB history、QC 回寫；沒有手動 edit log。 |

兩種正式 Query2 匯出都會先檢查 QC 判定。只要有 PPB row 因 Container 或壓力規則不足而得到「未判定」，就回傳 `400`，不產生正式下載，也不執行後續 DB 寫入。

### CSV、COA 與摘要

| Method | Path | 用途 | 資料來源 |
| --- | --- | --- | --- |
| `POST` | `/api/exports/port-ppb-csv` | 舊版 TO14C CSV 匯出。 | 舊 `PORT_PPB` |
| `POST` | `/api/exports/excel-ppb-csv` | 新版 TO14C CSV 匯出。 | Excel PPB history |
| `GET` | `/api/exports/std-cylinder-summary` | 匯出 STD Cylinder summary。 | Excel PPB history |
| `POST` | `/api/exports/excel-ppb-coa-large` | 匯出 COA 大卡 package。 | Excel PPB history |
| `POST` | `/api/exports/excel-ppb-coa-small` | 匯出 COA 小卡 package。 | Excel PPB history |

### 下載既有檔案

| Method | Path | 用途 |
| --- | --- | --- |
| `GET` | `/api/downloads/cylinder-qc/{batchDate}` | 從匯出根目錄尋找並下載既有 `Cylinder_Qc[...]xlsx`。 |
| `GET` | `/api/downloads/to14c-csv/{sampleName}` | 依 sample name 尋找既有 TO14C CSV。 |

## 主要 request 範例

### 建立 Query2 preview 或直接匯出

`POST /api/exports/query2-excel/preview` 與 `POST /api/exports/query2-excel` 使用相同的選取欄位：

```json
{
  "startDate": "20260801",
  "endDate": "20260813",
  "rfId": "RF 的 stable id",
  "stdRawIds": ["STD raw stable id 1", "STD raw stable id 2"],
  "portRawIds": ["PORT raw stable id 1", "PORT raw stable id 2"]
}
```

限制：

- `rfId` 必填。
- `stdRawIds` 至少一筆。
- `portRawIds` 至少一筆。
- 所選 raw 必須存在於指定日期區間，否則整個 request 回傳 `404`。

### 重算 preview

先保留 preview endpoint 回傳的完整物件，修改可編輯 row 的 `currentValues`／`manualOverrides` 後送回：

```json
{
  "preview": {
    "startDate": "20260801",
    "endDate": "20260813",
    "rfId": "RF 的 stable id",
    "stdRawIds": ["STD raw stable id 1"],
    "portRawIds": ["PORT raw stable id 1"],
    "columns": [],
    "dynamicAreaFields": [],
    "rows": []
  }
}
```

`POST /api/exports/query2-excel/recalculate` 回傳更新後 preview；正式下載時將同樣結構送到 `/from-preview`。

### Excel PPB CSV

```json
{
  "startDate": "20260801",
  "endDate": "20260813",
  "selectedIds": ["Excel PPB history id 1", "Excel PPB history id 2"]
}
```

單日相容格式：

```json
{
  "batchDate": "20260813",
  "selectedIds": ["Excel PPB history id 1"]
}
```

### COA 大卡

```json
{
  "startDate": "20260801",
  "endDate": "20260813",
  "selectedIds": ["Excel PPB history id 1"],
  "templateType": "standard"
}
```

`templateType` 只接受 `standard` 或 `yadong`，未提供時使用 `standard`。

### COA 小卡

```json
{
  "startDate": "20260801",
  "endDate": "20260813",
  "selectedIds": ["Excel PPB history id 1"],
  "cardsPerPage": 9
}
```

`cardsPerPage` 必須大於等於 1；未提供時使用 `Scheduler:CoaExport:DefaultSmallCardsPerPage`。

### 從 STD raw 匯入 RF

```json
{
  "stdRawId": "STD raw stable id"
}
```

此 endpoint 會呼叫外部 RF Extractor，因此除了 DB 之外，也依賴 `Scheduler:RfExtractor:*` 設定與網路連線。

### Dynamic AREA 欄位

新增欄位：

```json
{
  "columnName": "Area_NewCompound",
  "displayName": "NewCompound",
  "sortOrder": 20,
  "isActive": true
}
```

`fieldKey` 不能有空白或冒號，也不能與既有固定 analyte AREA 欄位衝突。

批次保存 PORT 值：

```json
{
  "values": [
    {
      "fieldKey": "NewCompound",
      "portKey": "PORT 01",
      "areaValue": 123.456
    }
  ]
}
```

### QC settings

```json
{
  "pressureRules": [
    {
      "containerType": "0.5L",
      "iniPrsMin": 0,
      "fnlPrsMin": 0,
      "isActive": true
    }
  ],
  "concentrationRules": [
    {
      "analyteKey": "指定 analyte key",
      "analyteName": "顯示名稱",
      "sortOrder": 1,
      "min05": 0,
      "max05": 1,
      "min1L": 0,
      "max1L": 1,
      "isActive": true
    }
  ]
}
```

濃度最小值不得大於最大值，數值不得小於 0；Container 目前只支援 `0.5L` 與 `1L`。

## 舊版與現行 API 的選擇

| 情境 | 使用 |
| --- | --- |
| 新增 Query2 Excel | `/api/exports/query2-excel`，或 preview → recalculate → from-preview |
| 新增 CSV／COA | 先查 `/api/excel-ppb-options`，再使用 `excel-ppb-*` endpoints |
| 查舊自動衍生 PPB | `/api/port-ppb-options` |
| 從舊 PPB 匯出 CSV | `/api/exports/port-ppb-csv` |

舊 `PORT_PPB` endpoints 仍保留相容性，但 Quant raw-only 流程不再為它產生新資料。
