# nuone-tools CODEMAP

這份檔案是給未來修改前先讀的「省 token 地圖」。先看這裡，再針對少數檔案搜尋，不要一開始就全 repo 掃描。

## 專案結構

- `MainWindow.xaml` / `MainWindow.xaml.cs`: 主視窗骨架，功能多數拆到 `MainWindow.*.cs` partial。
- `Views/*View.xaml` / `.xaml.cs`: WinUI 畫面與事件轉接。通常事件只呼叫 `Owner?.SomeMethod(...)`。
- `Features/*/MainWindow.*.cs`: 真正功能邏輯，依功能區拆 partial。
- `Models/*Models.cs`: 狀態與資料模型。
- `Infrastructure/MainWindow.Persistence.cs`: 設定檔讀寫、toolbar command 執行、部分外部流程。
- `Services/*`: Shell、watcher、背景服務。
- `Styles/AppStyles.xaml`: 共用樣式。

## 修改路線

- 檔案列表、pane、選取、複製/移動/刪除/重新命名：
  `Features/FileManager/MainWindow.FileOperations.cs`,
  `Features/FileManager/MainWindow.Panes.cs`,
  `Views/FileManagerView.xaml`,
  `Views/FileManagerView.xaml.cs`,
  `ViewModels/PaneViewModel.cs`,
  `Models/FileManagerModels.cs`
- 鍵盤、剪貼簿、快捷鍵：
  `Features/FileManager/MainWindow.KeyboardClipboard.cs`
- 單檔重新命名 dialog：
  `Features/FileManager/MainWindow.FileOperations.cs`
- toolbar 按鈕新增/編輯 dialog：
  `Features/Shared/MainWindow.Dialogs.cs`
- toolbar command 執行：
  `Infrastructure/MainWindow.Persistence.cs`
- toolbar/view 綁定橋接：
  `Features/Shared/MainWindow.ViewBindings.cs`
- FileBunker 上傳：
  `Features/FileManager/MainWindow.FileBunkerUpload.cs`,
  `Features/Shared/MainWindow.Dialogs.cs`,
  `Views/SettingsView.xaml`,
  `Views/SettingsView.xaml.cs`,
  `Features/Settings/MainWindow.Settings.cs`,
  `Models/AccountModels.cs`,
  `Models/AppSettingsModels.cs`,
  `Infrastructure/MainWindow.Persistence.cs`
- 設定頁：
  `Views/SettingsView.xaml`,
  `Views/SettingsView.xaml.cs`,
  `Features/Shared/MainWindow.ViewBindings.cs`,
  `Features/Settings/MainWindow.Settings.cs`,
  `Models/AppSettingsModels.cs`,
  `Infrastructure/MainWindow.Persistence.cs`
- 帳號/登入狀態：
  `Views/SettingsView.xaml`,
  `Views/SettingsView.xaml.cs`,
  `Features/Settings/MainWindow.Settings.cs`,
  `Models/AccountModels.cs`,
  `Infrastructure/MainWindow.Persistence.cs`
- Terminal：
  `Views/TerminalView.xaml`,
  `Views/TerminalView.xaml.cs`,
  `Features/Terminal/MainWindow.Terminal.cs`,
  `Features/Terminal/TerminalConPtyNative.cs`,
  `Models/TerminalModels.cs`
- Automation：
  `Views/AutomationView.xaml`,
  `Views/AutomationView.xaml.cs`,
  `Features/Automation/MainWindow.Automation.cs`,
  `Models/AutomationModels.cs`,
  `Services/*Watcher.cs`
- 群組/sidebar/context menu：
  `Features/FileManager/MainWindow.Groups.cs`,
  `Services/ShellContextMenuHost.cs`
- 主題/外觀：
  `Styles/AppStyles.xaml`,
  `App.xaml`,
  `Features/Settings/MainWindow.Settings.cs`

## 重要慣例

- `Views/*View.xaml.cs` 通常用 `internal FrameworkElementName => ...` 暴露 XAML 控制項。
- `Features/Shared/MainWindow.ViewBindings.cs` 把 `MainWindow` 內的舊 private property 對應到目前 active view 的控制項。
- 新增設定通常需要同步改：
  XAML、XAML.cs event、ViewBindings、`MainWindow.Settings.cs`、`Models/AppSettingsModels.cs`、`Infrastructure/MainWindow.Persistence.cs`。
- 內建 toolbar command 用常數字串，並透過 `IsBuiltInToolbarCommand(...)` / `ExecuteBuiltInToolbarCommandAsync(...)` 分流。
- 要拿目前 pane 選取檔案，優先用 `GetSelectedEntriesInDisplayOrder(_activePane)`。
- 一般提示用 `ShowMessageAsync(...)`；需要複製按鈕、checkbox、輸入欄等互動時用 `ContentDialog`。
- 不要回復或覆蓋使用者未要求處理的 dirty changes。
- 如果是間歇性、非同步、跨執行緒、SSH、watcher 或其他難重現問題，先補聚焦的 diagnostic log，再根據 log 修；不要只靠猜測。log 優先寫到本機 config 目錄，方便使用者直接貼內容追錯。

## 目前已知功能點

- FileBunker toolbar 不預設出現；使用者可在新增 toolbar 按鈕 dialog 勾選 `FileBunker 上傳` 來建立。
- FileBunker 上傳使用當前 active pane 的選取檔案，不開檔案選擇器。
- FileBunker 成功 dialog 有複製連結按鈕，且會自動複製上傳 URL。
- FileBunker 設定應放設定頁與 `settings.json`，不要讀 `.env`。
- 上傳 multipart file part 需要設定正確 `Content-Type`，圖片才會被 FileBunker 當圖片顯示。
- 單檔重新命名預設不顯示副檔名；checkbox `包含副檔名` 開啟後才一起編輯副檔名。
- 檔案縮圖可能回傳 null；讀取 Shell thumbnail 要先做 null/size guard。

## 驗證注意

- 使用者偏好：這個 repo 不要主動幫他跑完整 build，除非他要求或修正需要驗證。
- 之前完整 `dotnet build -nologo` 不是乾淨 baseline，曾遇到既有 WinUI/XAML 問題，例如 missing terminal event handler 與 view type/DataType resolve errors。
- 如果只改 UI/小邏輯，優先做 targeted diff review；需要 build 時先說明可能碰到既有錯誤。

## 省 token 工作流

1. 先讀本檔。
2. 依「修改路線」只打開 1 到 4 個最相關檔案。
3. 若找不到，再用 `rg` 搜尋精準 symbol，不做全 repo 大量讀檔。
4. 修改前先看 `git status --short`，避免踩到使用者改動。
5. 回覆時只講改了什麼、沒驗證什麼、還有什麼風險。
