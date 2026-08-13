# 文件索引

如果是第一次接觸專案，請先從專案根目錄的 [README](../../../README.md) 開始。這一層放的是操作、架構與各功能的深入文件。

## 建議閱讀順序

1. [現行架構](ARCHITECTURE.md)：先理解目前真正會執行的三條資料流。
2. [操作手冊](RUNBOOK.md)：準備環境、設定、安全執行與查 Log。
3. [API 參考](API.md)：查詢與匯出 endpoints。
4. [資料庫說明](DATABASE.md)：資料表角色、讀寫方向與 SQL scripts。
5. 依需求閱讀 Query2、Quant、COA 或 MFG JSON 的細節文件。

## 文件一覽

| 文件 | 用途 |
| --- | --- |
| [現行架構](ARCHITECTURE.md) | 系統邊界、執行流程、主要類別與重要不變條件。 |
| [操作手冊](RUNBOOK.md) | SDK、設定、啟動模式、Windows Service、Log 與故障排查。 |
| [API 參考](API.md) | 目前 `Program.cs` 實際註冊的完整 API 清單。 |
| [資料庫說明](DATABASE.md) | 核心表、歷史表、設定表與 SQL scripts。 |
| [Query2 Excel 演算法](Query2Excel_FromDbRawData_README.md) | DB raw 到 Query2 preview／Excel 的詳細計算與欄位規則。 |
| [Quant Parser 與 DB Mapping](QuantParser_DB_Mapping_README.md) | `Quant.txt`、acqmeth、LOT、SampleNo 與 raw table mapping。 |
| [COA 欄位對應](COA_Field_Mapping_README.md) | COA 大卡／小卡模板與欄位來源。 |
| [MFG JSON 匯入](MFG_JSON_IMPORT_README.md) | MFG JSON 掃描、upsert、狀態檔與 Log。 |
| [ADR-0001：Quant raw-only](ADR/0001-quant-raw-only.md) | 停用 Quant 自動衍生計算的背景、影響與恢復條件。 |

## 文件維護原則

- README 與架構文件只描述「目前會執行的行為」；舊流程與決策原因放在 `ADR/`。
- API 有新增、刪除或改名時，同步更新 `API.md`。
- 設定模型或預設值改變時，同步更新 `RUNBOOK.md`。
- DB table 或 SQL script 改變時，同步更新 `DATABASE.md`。
- 不在文件中放入帳號、密碼、正式連線字串或其他機密。
