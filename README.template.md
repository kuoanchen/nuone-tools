# {{ProjectName}}

![Version](https://img.shields.io/badge/version-{{ShieldVersion}}-blue?cacheSeconds=2592000)
![AssemblyVersion](https://img.shields.io/badge/assembly-{{ShieldAssemblyVersion}}-purple?cacheSeconds=2592000)
![TargetFramework](https://img.shields.io/badge/framework-{{ShieldTargetFramework}}-0a7ea4?cacheSeconds=2592000)

WinUI 3 檔案管理工具，支援雙 Pane 瀏覽、群組捷徑、背景自動化、批次重新命名、工具列命令與原生 Windows Shell 操作流程。

## 專案資訊

- 專案名稱：{{ProjectName}}
- RootNamespace：`{{RootNamespace}}`
- Version：`{{Version}}`
- AssemblyVersion：`{{AssemblyVersion}}`
- FileVersion：`{{FileVersion}}`
- InformationalVersion：`{{InformationalVersion}}`
- TargetFramework：`{{TargetFramework}}`
- Platforms：`{{Platforms}}`
- RuntimeIdentifiers：`{{RuntimeIdentifiers}}`
- Repository：{{RepositoryUrl}}

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

### 啟動模式

```powershell
nuone-tools.exe
nuone-tools.exe -w
nuone-tools.exe -w -it 2
```

- `nuone-tools.exe`
  - 一般模式，可開多個 UI 視窗。
- `nuone-tools.exe -w`
  - 共用既有其中一個 UI，直接切到終端機，工作目錄跟隨目前外部終端機路徑。
- `nuone-tools.exe -w -it 2`
  - 指定切到第 2 個已開啟的 UI 視窗，再進終端機。
- `tools.cmd` 與 Git Bash 用的 `tools` wrapper 會在 app 啟動時自動建立到「目前使用者 PATH 中可寫入的資料夾」。
  - 若 `WindowsApps` 不可寫，會改放到像 `C:\Users\<user>\.local\bin` 這類可寫且已在 PATH 的位置。

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

{{ChangelogContent}}
