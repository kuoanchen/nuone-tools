# Nuone Tools

![Version](https://img.shields.io/badge/version-1.202606.2-blue.svg?cacheSeconds=2592000)
![AssemblyVersion](https://img.shields.io/badge/assembly-1.2026.6.2-purple.svg?cacheSeconds=2592000)
![TargetFramework](https://img.shields.io/badge/framework-net8.0-windows10.0.19041.0-0a7ea4.svg?cacheSeconds=2592000)

WinUI 3 檔案管理工具，支援雙 Pane 瀏覽、群組捷徑、背景自動化、批次重新命名、工具列命令與原生 Windows Shell 操作流程。

## 專案資訊

- 專案名稱：Nuone Tools
- RootNamespace：`nuone_tools`
- Version：`1.202606.2`
- AssemblyVersion：`1.2026.6.2`
- FileVersion：`1.2026.6.2`
- InformationalVersion：`1.2026.6.2`
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

