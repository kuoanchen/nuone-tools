# Changelog

## 1.202607.1 (2026-07-01)

### 新功能

* 擴充更新進度、腳本終端機執行與目錄容量計算 ([4dd8f6e](https://github.com/kuoanchen/nuone-tools/commit/4dd8f6e4a90e76d545304c337b93fe85319caaa5))
  - 為線上更新加入下載/安裝進度條、背景工作百分比與 Windows 進度通知，讓更新流程可視化
  - 新增工具列腳本執行能力，支援把選取的 `.ps1`、`.bat`、`.cmd`、`.bash`、`.sh` 直接送到內建或外部終端機執行
  - 擴充目前目錄每列容量計算，背景掃描資料夾大小並即時更新 pane 項目尺寸，不阻塞 UI
  - 改善自動解壓對 multipart RAR 的判斷，支援等待分卷、辨識缺卷狀態與清理整組來源檔
  - 優化 toolbar icon 設定與 Fluent glyph 處理，支援 glyph 正規化、icon picker 與更清楚的 icon summary
  - 補強背景工作摘要，顯示進行中項目、進度百分比，並把更新類通知納入追蹤
* 強化檔案管理增量刷新並整理 logging 策略 ([a5eb08f](https://github.com/kuoanchen/nuone-tools/commit/a5eb08f2ca4e8b6515578724fbe7e2632d1080ff))
  - 為檔案複製、搬移與刪除加入更細的 timing 診斷與 UI 套用流程，避免每次本機操作後都整個 pane 重新整理
  - 在 `PaneViewModel` 與 `PaneDirectoryWatcher` 新增本機檔案變更的增量套用能力，支援 created/changed/deleted/renamed 直接更新清單與保留選取狀態
  - 改善 watcher debounce 與 transient download 檔案節流邏輯，降低高頻 `Changed` 事件造成的多餘刷新
  - 調整 shell context menu 選取來源與同資料夾判斷，讓右鍵操作更準確對應目前項目集合
  - 新增 `LOGGING.md`，明確規範 Error/Warning/Information/Debug 的使用原則，並同步清理大量低價值資訊型 log
  - 補強 `-f` 啟動參數與標題列 passthrough 區域處理，支援將既有視窗切回 File Manager 並改善通知按鈕互動

## 1.202606.4 (2026-06-25)

### 新功能

* 啟動時自動安裝 tools 命令別名 ([655411c](https://github.com/kuoanchen/nuone-tools/commit/655411cba1e82afed6fb440ac8be5795fcb7d7df))
  - 在 `App.xaml.cs` 新增 `tools.cmd` 與 `tools` wrapper 自動建立流程，啟動時會把 `nuone-tools.exe` 暴露成可從 cmd 與 Git Bash 呼叫的 `tools` 命令
* 支援單實例終端機喚起與外部啟動導向 ([184eed3](https://github.com/kuoanchen/nuone-tools/commit/184eed37fb6b9bd2d09f7055abdb69152db69f0a))
  - 新增 app 單實例註冊與 named pipe 轉送機制，支援將後續啟動請求導向既有 nuone-tools 視窗

## 1.202606.3 (2026-06-24)

### 新功能

* 新增應用程式更新流程並強化自動化執行權控管 ([eb15f7b](https://github.com/kuoanchen/nuone-tools/commit/eb15f7b32dc377108630819ff94c305f5be01765))
  - 在設定頁加入線上更新區塊，支援檢查新版、下載安裝、顯示版本資訊與 release notes
* 強化雙窗格分頁、拖放目標與終端機視窗適應能力 ([dab6852](https://github.com/kuoanchen/nuone-tools/commit/dab6852f6a971b569fb6269e41943e09f1ad7c8d))
  - 為左右 pane 新增分頁列、分頁切換與關閉流程，讓每個分頁各自保存路徑與返回歷史
* 擴充設定同步、通知與 PDF 增強工作流 ([23c941e](https://github.com/kuoanchen/nuone-tools/commit/23c941e0b9e9d3238dbb975e80b688a9b3351992))
  - 新增 PDF 增強功能與 `enhance_pdf.py` 腳本，支援在檔案管理器批次處理本機 PDF 並回寫輸出與失敗摘要
* 強化檔案操作通知與 PowerShell 腳本執行流程 ([895047a](https://github.com/kuoanchen/nuone-tools/commit/895047aa66cd8eeebff8da8477689af6b1eb333a))
  - 改善複製、搬移與貼上作業的背景工作紀錄，加入來源、目的地與項目明細，並同步優化完成通知內容
* 強化自動化備份與檔案管理狀態呈現 ([6e4b02d](https://github.com/kuoanchen/nuone-tools/commit/6e4b02d1d14cfc5a3526f26ea326b9ed81cca9a3))
  - 擴充自動化備份設定，加入排除資料夾名稱與 log 輸出目錄，並記錄更完整的執行結果與背景工作摘要
* 擴充雙窗格檔案管理與通知紀錄能力 ([90356b8](https://github.com/kuoanchen/nuone-tools/commit/90356b8509b25a91c598fbbfcf908b0f6e5bd688))
  - 新增 SSH 與 WSL 路徑瀏覽、遠端目錄讀取與偵錯紀錄，強化雙窗格導覽與重新整理流程
* 重構主視窗並新增終端機與模組化功能頁面 ([7aa205c](https://github.com/kuoanchen/nuone-tools/commit/7aa205cca0df371dc7b00ca8b9eaa3bbd230e49a))
  - 將 MainWindow 拆分為 Automation、FileManager、Settings、Terminal 等獨立 View 與對應綁定

### 其他

* Merge branch 'main' into dev ([52c45b0](https://github.com/kuoanchen/nuone-tools/commit/52c45b0a8620eeb829c2435fc2db76af7c0bebbd))
* Merge pull request #2 from kuoanchen/dev ([3b8225b](https://github.com/kuoanchen/nuone-tools/commit/3b8225b783c5d08b171b58ec5963f24c0bc42893))
  - 1.202606.2 (2026-06-18)
* Nueva versión 202606.2 ([3af9138](https://github.com/kuoanchen/nuone-tools/commit/3af9138c098828dbecd026ef251c580f53431ab5))

## 1.202606.2 (2026-06-18)

### 新功能

* 強化自動化備份與檔案管理狀態呈現 ([6e4b02d](https://github.com/kuoanchen/nuone-tools/commit/6e4b02d1d14cfc5a3526f26ea326b9ed81cca9a3))
  - 擴充自動化備份設定，加入排除資料夾名稱與 log 輸出目錄，並記錄更完整的執行結果與背景工作摘要
* 擴充雙窗格檔案管理與通知紀錄能力 ([90356b8](https://github.com/kuoanchen/nuone-tools/commit/90356b8509b25a91c598fbbfcf908b0f6e5bd688))
  - 新增 SSH 與 WSL 路徑瀏覽、遠端目錄讀取與偵錯紀錄，強化雙窗格導覽與重新整理流程
* 重構主視窗並新增終端機與模組化功能頁面 ([7aa205c](https://github.com/kuoanchen/nuone-tools/commit/7aa205cca0df371dc7b00ca8b9eaa3bbd230e49a))
  - 將 MainWindow 拆分為 Automation、FileManager、Settings、Terminal 等獨立 View 與對應綁定

## 1.202606.1 (2026-06-12)

### 新功能

* 強化 WinUI 檔案管理介面與雙 pane 工作流 ([d1109ff](https://github.com/kuoanchen/nuone-tools/commit/d1109ff8416093bd1995bc9000107c51ecb6e4d4))
  - 重做 MainWindow 版面，加入應用程式列、檔案管理與設定雙視圖切換
* 初始化 Nuone Tools WinUI 檔案管理工具 ([2294a93](https://github.com/kuoanchen/nuone-tools/commit/2294a931f8713348a7b2f33b211e9e3cb5ab5600))
  - 新增 WinUI 3 專案與 solution/csproj 基本設定

### 重構

* **winui:** 拆分 MainWindow 結構並整理發佈設定 ([7bdb206](https://github.com/kuoanchen/nuone-tools/commit/7bdb2061f38a333f1723629b243dbf4f3d5b0d77))
  - 將 MainWindow 的檔案管理、自動化、設定、對話框、Shell 與持久化邏輯拆成多個 partial 檔案

