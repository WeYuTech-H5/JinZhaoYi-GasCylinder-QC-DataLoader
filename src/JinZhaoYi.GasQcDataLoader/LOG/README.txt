這個資料夾存放 Gas QC DataLoader 的執行 log。

系統啟動時如果 LOG 或底下子資料夾不存在，程式會自動建立。

資料夾用途：

Application
一般程式執行 log。沒有被歸到同步、讀檔、MFG JSON 的訊息會放在這裡。

Sync
Quant 同步流程 log。用來追查排程有沒有跑、哪些批次有進同步流程、processed state 有沒有擋掉舊檔。

DataRead
讀資料 log。用來追查系統實際掃到哪些 Quant.txt、acqmeth/acqemeth，以及解析出的 LOT、SampleNo、EM 欄位。

MFGJSON
製造部 JSON 同步 log。用來追查 MFGExport_*.json 掃描、略過、成功、失敗與 insert/update 結果。

serilog-selflog.txt
Serilog 自己寫 log 失敗時的備援紀錄。
