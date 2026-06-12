# {{ProjectName}}

![Version](https://img.shields.io/badge/version-{{Version}}-blue.svg?cacheSeconds=2592000)
![AssemblyVersion](https://img.shields.io/badge/assembly-{{AssemblyVersion}}-purple.svg?cacheSeconds=2592000)
![TargetFramework](https://img.shields.io/badge/framework-{{TargetFramework}}-0a7ea4.svg?cacheSeconds=2592000)

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
