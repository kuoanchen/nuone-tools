# nuone-tools Logging 規則

這份文件是這個專案的 logging 準則，目的只有兩個：

1. 出錯時能快速定位問題。
2. 不要讓大量 `Information` / `Debug` 影響效能或淹沒真正錯誤。

## 目前實作

- logging 入口：`Infrastructure/AppLogging.cs`
- logging 套件：`Serilog` + `Serilog.Sinks.File`
- 目前輸出：每日 rolling file，檔名為 `nuone-tools-yyyyMMdd.log`
- log 目錄：由 `Logging.LogDirectoryPath` 決定；若設定失敗，會 fallback 到本機預設目錄

## 核心原則

- `Error`：保留。真正失敗、例外、外部程序錯誤、無法完成的工作都應該記。
- `Warning`：保留少量。只用在「不是正常情況，但程式還能繼續」。
- `Information`：限低頻、重要狀態轉換。
- `Debug`：只用於短期診斷；問題修完就刪掉，或至少集中在可快速移除的單一區塊。

## 什麼情況應該寫 log

適合保留的情況：

- `try/catch` 內的例外
- `App.UnhandledException`、`TaskScheduler.UnobservedTaskException` 這類全域例外
- 外部程序失敗
  - SSH
  - 壓縮 / 解壓
  - 更新
  - Python / script
- 背景工作失敗
- 檔案同步失敗
- watcher 自己丟出 error
- UI / dispatcher / named pipe / mutex 這種跨執行緒或跨程序邊界失敗
- 難重現 bug 的聚焦診斷
  - 但要能在修完後快速移除

## 什麼情況不要寫 log

不應常態保留的情況：

- 每次切頁
- 每次 selection 改變
- 每次 filter 輸入
- 每次 watcher 收到 changed / created / deleted
- 每次 terminal 輸出或輸入
- 每次 pane refresh
- 每個檔案逐筆成功處理
- 每次按鈕點擊都記一筆
- 單純成功、skip、idle、start、resolve path 這類高頻流程訊息

這些 log 最容易造成：

- log 檔暴增
- NAS / 網路路徑 I/O 變慢
- UI 卡頓或背景工作變慢
- 真正錯誤被大量雜訊蓋掉

## Information 使用規則

`Information` 只留低頻且有管理價值的事件，例如：

- app 啟動完成
- app 關閉前的重要收尾
- 使用者手動改變重要設定
- 手動觸發且耗時的工作完成
- 備份 / 同步 / 更新這種長流程的「開始 / 完成 / 失敗」

如果同一件事會在短時間連續大量發生，就不要用 `Information`。

## Debug 使用規則

`Debug` 不作為常態產品行為紀錄。

只在這些情況可接受：

- 間歇性 bug
- 非同步 race condition
- watcher / SSH / 遠端路徑
- UI thread / background thread 邊界問題
- 使用者已經明確要求先查 log、先加診斷

使用方式：

- 範圍要小
- 訊息要直接對應可疑邊界
- 修完後要刪

不要把 `Debug` 當成「我想知道程式有沒有跑到這裡」的長期方案。

## 建議的記錄方式

優先記這些資訊：

- boundary / action 名稱
- 失敗原因
- 例外型別
- 重要識別值
  - profile id
  - path
  - target
  - process id
  - pipe name

避免記這些內容：

- token
- api key
- 整份設定檔內容
- 大量檔案清單全文
- 太長的 raw payload

## 建議模式

### 低頻長流程

最多 2 到 3 筆：

- 開始
- 完成
- 失敗

### 高風險邊界

正常情況不記，失敗才記：

- queue rejected
- mutex / pipe / dispatch 失敗
- watcher error
- 外部程序 exit code 異常

### 難重現問題

暫時加聚焦 `Debug`：

- 明確標出 boundary
- 明確標出關鍵 path / id / state
- 問題修完後移除

## 專案內目前建議

- `AppLogging.Error(...)`：保留
- `AppLogging.Warning(...)`：只留真正異常
- `AppLogging.Information(...)`：只留低頻狀態轉換
- `AppendDebugLog(...)`：不要再當成大量流程追蹤工具

若真的要加新的診斷 log，優先放在：

- `catch`
- 外部流程失敗判斷
- 邊界封裝
  - 例如 `RunSafely(...)`
  - `LogBoundaryException(...)`
  - watcher `Error`

## 修改前自問

加 log 前先問自己：

1. 這筆 log 只在失敗時才會出現嗎？
2. 這筆 log 是低頻事件嗎？
3. 這筆 log 對之後查問題真的有定位價值嗎？
4. 這筆 log 若每天寫上千次，還值得存在嗎？

若第 1 到第 4 題有兩題以上回答「不是」，就不要加。
