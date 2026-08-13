這個資料夾存放 Quant 同步流程 log。

主要檔案：
cycle-YYYYMMDD.log
記錄 worker 等待、每輪同步開始與結束、成功/失敗統計。

import-YYYYMMDD.log
記錄 Quant 匯入流程，例如解析批次、LOT 驗證、RF 取得、寫入資料集合建立、DB 寫入前後訊息。

state-YYYYMMDD.log
記錄 processed-quant-files.json 的比對與更新，例如候選檔案數、已處理略過數、待處理數、state 更新結果。

適合查看：
1. 排程到底有沒有跑。
2. 某一輪同步有沒有掃到檔案。
3. 檔案是不是被 processed state 擋掉。
4. 匯入流程停在哪一步。
