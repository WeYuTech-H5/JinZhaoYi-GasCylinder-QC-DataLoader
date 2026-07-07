這個資料夾存放製造部 JSON 同步 log。

主要檔案：
scan-YYYYMMDD.log

用途：
記錄 MFGExport_*.json 的掃描、穩定檔判斷、略過原因、匯入成功/失敗，以及寫入 ZZ_NF_GAS_MFG_LOT 的 insert/update 統計。

適合查看：
1. 製造部 JSON 是否有被背景服務掃到。
2. JSON 是否因為 hash 或狀態檔被略過。
3. JSON 匯入後新增或更新了幾筆 MFG_LOT。
4. MFG JSON 匯入失敗時的錯誤訊息。
