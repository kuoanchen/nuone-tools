# Nuone Tools

![Version](https://img.shields.io/badge/version-1.202606.3-blue.svg?cacheSeconds=2592000)
![AssemblyVersion](https://img.shields.io/badge/assembly-1.2026.6.3-purple.svg?cacheSeconds=2592000)
![TargetFramework](https://img.shields.io/badge/framework-net8.0-windows10.0.19041.0-0a7ea4.svg?cacheSeconds=2592000)

WinUI 3 檔案管理工具，支援雙 Pane 瀏覽、群組捷徑、背景自動化、批次重新命名、工具列命令與原生 Windows Shell 操作流程。

## 專案資訊

- 專案名稱：Nuone Tools
- RootNamespace：`nuone_tools`
- Version：`1.202606.3`
- AssemblyVersion：`1.2026.6.3`
- FileVersion：`1.2026.6.3`
- InformationalVersion：`1.2026.6.3`
- TargetFramework：`net8.0-windows10.0.19041.0`
- Platforms：`x86;x64;ARM64`
- RuntimeIdentifiers：`win-x86;win-x64;win-arm64`
- Repository：https://github.com/kuoanchen/nuone-tools

## 常用指令

### 產生文件

```powershell
.\docs.cmd all
.\docs.cmd changelog
.\docs.cmd readme
```

### 發佈與打包

```powershell
.\package-release.cmd
```

## 文件產生方式

- `scripts\generate-changelog.ps1`
  - 讀取 git commit 歷史
  - 依 conventional commit 類型分組輸出 `CHANGELOG.md`
- `scripts\generate-readme.ps1`
  - 讀取 `nuone-tools.csproj` 版本資訊
  - 套用 `README.template.md`
  - 將 `CHANGELOG.md` 內容嵌入 `README.md`
- `scripts\generate-docs.ps1`
  - 依序產生 changelog 與 readme

## Changelog

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

