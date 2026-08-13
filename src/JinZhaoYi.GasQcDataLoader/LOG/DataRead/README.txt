這個資料夾存放讀資料與解析資料的 log。

主要檔案：
scanner-YYYYMMDD.log
記錄資料夾掃描結果，例如 WatchRoot、批次資料夾、穩定 Quant 檔案數、候選檔案數。

quant-YYYYMMDD.log
記錄 Quant.txt 與 acqmeth/acqemeth 讀檔結果，例如 Quant 路徑、LOT、SampleNo、Acq On、compound 數、EMVolts、RelativeEM。

適合查看：
1. 系統實際有沒有掃到指定 Quant.txt。
2. Quant.txt 是否被判定為正式檔案。
3. acqmeth/acqemeth 是否存在、是否成功讀到 EM 欄位。
4. LOT、SampleNo、採樣時間解析結果是否符合預期。
