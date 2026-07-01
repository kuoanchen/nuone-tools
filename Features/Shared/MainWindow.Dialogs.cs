using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace nuone_tools
{
    public sealed partial class MainWindow
    {
        private sealed record FluentIconOption(string Name, string Code);

        private sealed record FluentIconCategory(string Title, FluentIconOption[] Options);

        private sealed record FluentIconRange(string Title, int StartCodePoint, int EndCodePoint);

        private static readonly FluentIconRange[] ToolbarFluentIconAdvancedRanges =
        {
            new("E700-E7FF", 0xE700, 0xE7FF),
            new("E800-E8FF", 0xE800, 0xE8FF),
            new("E900-E9FF", 0xE900, 0xE9FF),
            new("EA00-EBFF", 0xEA00, 0xEBFF),
            new("EC00-EDFF", 0xEC00, 0xEDFF),
            new("EE00-EFFF", 0xEE00, 0xEFFF),
            new("F000-F1FF", 0xF000, 0xF1FF),
            new("F200-F3FF", 0xF200, 0xF3FF),
        };

        private static readonly FluentIconCategory[] ToolbarFluentIconCategories =
        {
            new("終端 / 執行", new[]
            {
                new FluentIconOption("Terminal", "E756"),
                new FluentIconOption("Run", "E768"),
                new FluentIconOption("Code", "EA80"),
                new FluentIconOption("Deploy", "E7B8"),
                new FluentIconOption("Sync", "E895"),
                new FluentIconOption("Refresh", "E72C"),
                new FluentIconOption("Settings", "E713"),
                new FluentIconOption("Link", "E71B"),
                new FluentIconOption("Play", "E768"),
                new FluentIconOption("Stop", "E71A"),
                new FluentIconOption("Command", "EB51"),
                new FluentIconOption("Bug", "EBE8"),
                new FluentIconOption("Developer Tools", "EBE5"),
                new FluentIconOption("Branch", "E944"),
                new FluentIconOption("Globe", "E774"),
                new FluentIconOption("Server", "F201"),
            }),
            new("檔案 / 資料夾", new[]
            {
                new FluentIconOption("Folder", "E8B7"),
                new FluentIconOption("Add Folder", "E710"),
                new FluentIconOption("Open Folder", "E838"),
                new FluentIconOption("Document", "E8A5"),
                new FluentIconOption("Library", "E8F1"),
                new FluentIconOption("Home", "E80F"),
                new FluentIconOption("Desktop", "E7F4"),
                new FluentIconOption("Preview", "E8A1"),
                new FluentIconOption("Page", "E7C3"),
                new FluentIconOption("Photo", "EB9F"),
                new FluentIconOption("Video", "E714"),
                new FluentIconOption("Music", "E189"),
                new FluentIconOption("Print", "E749"),
                new FluentIconOption("Clipboard", "E77F"),
                new FluentIconOption("Move To Folder", "E8DE"),
                new FluentIconOption("Open File", "E8E5"),
            }),
            new("上傳 / 雲端", new[]
            {
                new FluentIconOption("Upload", "E898"),
                new FluentIconOption("Download", "E896"),
                new FluentIconOption("Cloud", "E753"),
                new FluentIconOption("Storage", "E7F1"),
                new FluentIconOption("Database", "EFC7"),
                new FluentIconOption("Server", "F201"),
                new FluentIconOption("Network", "E968"),
                new FluentIconOption("Share", "E72D"),
                new FluentIconOption("Export", "EDE1"),
                new FluentIconOption("Import", "E8B5"),
                new FluentIconOption("Package", "E7B8"),
                new FluentIconOption("Archive", "F012"),
                new FluentIconOption("Globe", "E774"),
                new FluentIconOption("Hard Drive", "EDA2"),
                new FluentIconOption("Web", "E774"),
                new FluentIconOption("Send", "E724"),
            }),
            new("編輯 / 動作", new[]
            {
                new FluentIconOption("Add", "E710"),
                new FluentIconOption("Edit", "E70F"),
                new FluentIconOption("Delete", "E74D"),
                new FluentIconOption("Save", "E74E"),
                new FluentIconOption("Copy", "E8C8"),
                new FluentIconOption("Cut", "E8C6"),
                new FluentIconOption("Paste", "E77F"),
                new FluentIconOption("Rename", "E8AC"),
                new FluentIconOption("More", "E712"),
                new FluentIconOption("Back", "E72B"),
                new FluentIconOption("Forward", "E72A"),
                new FluentIconOption("Up", "E74A"),
                new FluentIconOption("Search", "E721"),
                new FluentIconOption("Filter", "E71C"),
                new FluentIconOption("Clear", "E894"),
                new FluentIconOption("Pin", "E718"),
                new FluentIconOption("Unpin", "E77A"),
            }),
            new("狀態 / 其他", new[]
            {
                new FluentIconOption("Info", "E946"),
                new FluentIconOption("Warning", "E7BA"),
                new FluentIconOption("Completed", "E73E"),
                new FluentIconOption("Clock", "E823"),
                new FluentIconOption("History", "E81C"),
                new FluentIconOption("Favorite", "E734"),
                new FluentIconOption("Magic", "EA90"),
                new FluentIconOption("Bug", "EBE8"),
                new FluentIconOption("Image", "EB9F"),
                new FluentIconOption("Print", "E749"),
                new FluentIconOption("Lock", "E72E"),
                new FluentIconOption("Unlock", "E785"),
                new FluentIconOption("Eye", "E890"),
                new FluentIconOption("Hide", "E8F4"),
                new FluentIconOption("Star", "E734"),
                new FluentIconOption("Light", "E793"),
                new FluentIconOption("Calendar", "E787"),
                new FluentIconOption("Check", "E73E"),
            }),
        };

        private async Task<string?> PromptForTextAsync(string title, string prompt, string defaultValue)
        {
            var textBox = new TextBox
            {
                Text = defaultValue,
                SelectionStart = 0,
                SelectionLength = defaultValue.Length,
            };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = prompt });
            panel.Children.Add(textBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "確定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? textBox.Text.Trim() : null;
        }

        private async Task<bool> ConfirmAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "確定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private async Task<ToolbarCommandItem?> ShowToolbarCommandEditorAsync(ToolbarCommandItem? existingItem)
        {
            var titleTextBox = new TextBox
            {
                Text = existingItem?.Title ?? string.Empty,
                PlaceholderText = "按鈕名稱，例如：vscode",
            };

            var commandTextBox = new TextBox
            {
                Text = existingItem?.Command ?? string.Empty,
                PlaceholderText = "要執行的 command，例如：code .",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 96,
            };

            var isNodeDockerDeploy = existingItem is not null && IsDeployNodeDockerCommand(existingItem.Command);
            var isFileBunkerUpload = existingItem is not null &&
                string.Equals(existingItem.Command, FileBunkerUploadCommand, StringComparison.OrdinalIgnoreCase);
            var isStorageUpload = existingItem is not null &&
                string.Equals(existingItem.Command, StorageUploadCommand, StringComparison.OrdinalIgnoreCase);
            var isEnhancePdf = existingItem is not null &&
                string.Equals(existingItem.Command, EnhancePdfCommand, StringComparison.OrdinalIgnoreCase);
            var isBuiltInTerminal = existingItem is not null && IsOpenBuiltInTerminalCommand(existingItem.Command);
            var isExternalTerminal = existingItem is not null && IsOpenExternalTerminalCommand(existingItem.Command);
            var isBuiltInTerminalExecute = existingItem is not null && IsExecuteInBuiltInTerminalCommand(existingItem.Command);
            var isExternalTerminalExecute = existingItem is not null && IsExecuteInExternalTerminalCommand(existingItem.Command);
            var isTerminalOpen = isBuiltInTerminal || isExternalTerminal;
            var isTerminalExecute = isBuiltInTerminalExecute || isExternalTerminalExecute;
            var isTerminal = isTerminalOpen || isTerminalExecute;
            var builtInActionComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "一般 Command", Tag = "command" },
                    new ComboBoxItem { Content = "Node.js Docker 部署", Tag = "node-docker" },
                    new ComboBoxItem { Content = "終端機開啟", Tag = "terminal-open" },
                    new ComboBoxItem { Content = "終端機執行", Tag = "terminal-execute" },
                    new ComboBoxItem { Content = "FileBunker 上傳", Tag = "filebunker-upload" },
                    new ComboBoxItem { Content = "Storage 上傳", Tag = "storage-upload" },
                    new ComboBoxItem { Content = "PDF 增強", Tag = "enhance-pdf" },
                },
            };
            var nodeDockerUserTextBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(existingItem?.NodeDockerUser)
                    ? DefaultNodeDockerUser
                    : existingItem.NodeDockerUser,
                PlaceholderText = "例如：admkuo",
            };
            var nodeDockerHostTextBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(existingItem?.NodeDockerHost)
                    ? DefaultNodeDockerHost
                    : existingItem.NodeDockerHost,
                PlaceholderText = "例如：docker05",
            };
            var nodeDockerRemoteDirectoryTextBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(existingItem?.NodeDockerRemoteDirectory)
                    ? DefaultNodeDockerRemoteDirectory
                    : existingItem.NodeDockerRemoteDirectory,
                PlaceholderText = "例如：~/temp",
            };
            var nodeDockerLaunchModeComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "外部視窗", Tag = NodeDockerLaunchMode.ExternalWindow },
                    new ComboBoxItem { Content = "內建終端機", Tag = NodeDockerLaunchMode.BuiltInTerminal },
                },
            };
            var nodeDockerBuiltInShellLabel = new TextBlock { Text = "內建 shell" };
            var nodeDockerBuiltInShellComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "PowerShell", Tag = TerminalShellKind.PowerShell },
                    new ComboBoxItem { Content = "Git Bash", Tag = TerminalShellKind.GitBash },
                    new ComboBoxItem { Content = "cmd", Tag = TerminalShellKind.CommandPrompt },
                },
            };

            var iconPathTextBox = new TextBox
            {
                Text = existingItem?.IconPath ?? string.Empty,
                PlaceholderText = "本機 icon 路徑，例如 C:\\Soft\\icons\\vscode.png",
            };
            var iconGlyphTextBox = new TextBox
            {
                Text = existingItem?.IconGlyph ?? string.Empty,
                PlaceholderText = "例如：E768",
            };
            var iconGlyphPickerButton = new Button
            {
                Content = "從清單挑選",
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var iconSourceComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "Icon 路徑", Tag = "path" },
                    new ComboBoxItem { Content = "Segoe Fluent Icons", Tag = "fluent" },
                },
            };
            var iconSourceMode = !string.IsNullOrWhiteSpace(existingItem?.IconPath)
                ? "path"
                : !string.IsNullOrWhiteSpace(ToolbarCommandItem.NormalizeFluentGlyph(existingItem?.IconGlyph))
                    ? "fluent"
                    : "path";
            SelectComboBoxItemByTag(iconSourceComboBox, iconSourceMode);
            var iconPathLabel = new TextBlock { Text = "Icon 路徑" };
            var iconGlyphLabel = new TextBlock { Text = "Segoe Fluent Icons" };
            var iconGlyphListLabel = new TextBlock { Text = "圖示清單" };
            var iconGlyphHint = new TextBlock
            {
                Text = "輸入 Fluent glyph，例如 E768、E756。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
            };

            var previewGlyph = new FontIcon
            {
                Glyph = string.Empty,
                FontSize = 24,
                Foreground = new SolidColorBrush(GetBrushColor("TextPrimaryBrush", "#F6F2FF")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };

            var previewImage = new Image
            {
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };

            async void RefreshPreview()
            {
                var iconSource = iconSourceComboBox.SelectedItem is ComboBoxItem { Tag: string sourceTag }
                    ? sourceTag
                    : "path";
                await UpdateToolbarIconVisualAsync(
                    previewImage,
                    previewGlyph,
                    string.Equals(iconSource, "path", StringComparison.Ordinal) ? iconPathTextBox.Text.Trim() : string.Empty,
                    string.Equals(iconSource, "fluent", StringComparison.Ordinal) ? iconGlyphTextBox.Text.Trim() : string.Empty);
            }

            iconPathTextBox.TextChanged += (_, _) => RefreshPreview();
            iconGlyphTextBox.TextChanged += (_, _) =>
            {
                RefreshPreview();
            };
            iconSourceComboBox.SelectionChanged += (_, _) => RefreshPreview();
            iconGlyphPickerButton.Click += async (_, _) =>
            {
                var selectedCode = await ShowFluentIconPickerDialogAsync(iconGlyphTextBox.Text.Trim());
                if (!string.IsNullOrWhiteSpace(selectedCode))
                {
                    iconGlyphTextBox.Text = selectedCode;
                }
            };

            var iconPreviewBorder = new Border
            {
                Width = 56,
                Height = 56,
                Background = new SolidColorBrush(GetBrushColor("InputBrush", "#1B1621")),
                BorderBrush = new SolidColorBrush(GetBrushColor("PanelStrokeBrush", "#3A3146")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new Grid
                {
                    Children =
                    {
                        previewImage,
                        previewGlyph,
                    },
                },
            };

            var iconPathRow = new Grid { ColumnSpacing = 12 };
            iconPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            iconPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            iconPathRow.Children.Add(iconPreviewBorder);
            Grid.SetColumn(iconPathTextBox, 1);
            iconPathRow.Children.Add(iconPathTextBox);

            RefreshPreview();

            var nodeDockerSettingsPanel = new StackPanel
            {
                Spacing = 10,
                Visibility = isNodeDockerDeploy ? Visibility.Visible : Visibility.Collapsed,
            };
            nodeDockerSettingsPanel.Children.Add(new TextBlock
            {
                Text = "部署設定",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            nodeDockerSettingsPanel.Children.Add(new TextBlock { Text = "執行入口" });
            nodeDockerSettingsPanel.Children.Add(nodeDockerLaunchModeComboBox);
            nodeDockerSettingsPanel.Children.Add(nodeDockerBuiltInShellLabel);
            nodeDockerSettingsPanel.Children.Add(nodeDockerBuiltInShellComboBox);
            nodeDockerSettingsPanel.Children.Add(new TextBlock { Text = "SSH 使用者" });
            nodeDockerSettingsPanel.Children.Add(nodeDockerUserTextBox);
            nodeDockerSettingsPanel.Children.Add(new TextBlock { Text = "Docker 主機" });
            nodeDockerSettingsPanel.Children.Add(nodeDockerHostTextBox);
            nodeDockerSettingsPanel.Children.Add(new TextBlock { Text = "遠端暫存目錄" });
            nodeDockerSettingsPanel.Children.Add(nodeDockerRemoteDirectoryTextBox);
            nodeDockerSettingsPanel.Children.Add(new TextBlock
            {
                Text = "按下工具列按鈕時會取目前 pane 選取的 zip，依這裡的設定上傳並執行 init.sh；可選外部視窗或 Nuone Tools 內建終端機。若 sudo 需要密碼，可在終端機中直接輸入。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
            });

            var commandPanel = new StackPanel
            {
                Spacing = 6,
            };
            commandPanel.Children.Add(new TextBlock { Text = "Command" });
            commandPanel.Children.Add(commandTextBox);

            var terminalShellComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "PowerShell", Tag = TerminalShellKind.PowerShell },
                    new ComboBoxItem { Content = "Git Bash", Tag = TerminalShellKind.GitBash },
                    new ComboBoxItem { Content = "cmd", Tag = TerminalShellKind.CommandPrompt },
                },
            };
            var terminalWorkingDirectoryModeComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "目前作用中的 pane", Tag = ToolbarWorkingDirectoryMode.ActivePane },
                    new ComboBoxItem { Content = "左側 pane", Tag = ToolbarWorkingDirectoryMode.LeftPane },
                    new ComboBoxItem { Content = "右側 pane", Tag = ToolbarWorkingDirectoryMode.RightPane },
                    new ComboBoxItem { Content = "自訂路徑", Tag = ToolbarWorkingDirectoryMode.CustomPath },
                },
            };
            var terminalCustomWorkingDirectoryTextBox = new TextBox
            {
                Text = existingItem?.TerminalCustomWorkingDirectory ?? _shortcutSettings.DefaultTerminalCustomWorkingDirectory,
                PlaceholderText = @"例如：C:\trabajo 或 \\dsm\video",
            };
            var terminalLaunchArgumentsTextBox = new TextBox
            {
                Text = existingItem?.TerminalLaunchArguments ?? string.Empty,
                PlaceholderText = @"例如：-d . -w 0",
            };
            var terminalTargetComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "外部終端機", Tag = OpenExternalTerminalCommand },
                    new ComboBoxItem { Content = "內建終端機", Tag = OpenBuiltInTerminalCommand },
                },
            };
            var terminalTargetLabel = new TextBlock { Text = "終端機入口" };
            var terminalShellLabel = new TextBlock { Text = "預設 shell" };
            var terminalWorkingDirectoryLabel = new TextBlock { Text = "工作目錄" };
            var terminalCustomWorkingDirectoryLabel = new TextBlock { Text = "自訂工作目錄" };
            var terminalLaunchArgumentsLabel = new TextBlock { Text = "啟動參數" };

            SelectComboBoxItemByTag(nodeDockerLaunchModeComboBox, existingItem?.NodeDockerLaunchMode ?? NodeDockerLaunchMode.ExternalWindow);
            SelectComboBoxItemByTag(nodeDockerBuiltInShellComboBox, existingItem?.TerminalShellKind ?? _shortcutSettings.DefaultTerminalShellKind);
            SelectComboBoxItemByTag(terminalShellComboBox, existingItem?.TerminalShellKind ?? _shortcutSettings.DefaultTerminalShellKind);
            SelectComboBoxItemByTag(terminalWorkingDirectoryModeComboBox, existingItem?.TerminalWorkingDirectoryMode ?? _shortcutSettings.DefaultTerminalWorkingDirectoryMode);
            SelectComboBoxItemByTag(
                terminalTargetComboBox,
                isBuiltInTerminal || isBuiltInTerminalExecute
                    ? OpenBuiltInTerminalCommand
                    : OpenExternalTerminalCommand);
            SelectComboBoxItemByTag(
                builtInActionComboBox,
                isNodeDockerDeploy
                    ? "node-docker"
                    : isTerminalOpen
                        ? "terminal-open"
                    : isTerminalExecute
                        ? "terminal-execute"
                    : isFileBunkerUpload
                        ? "filebunker-upload"
                    : isStorageUpload
                        ? "storage-upload"
                    : isEnhancePdf
                        ? "enhance-pdf"
                    : "command");

            var terminalSettingsPanel = new StackPanel
            {
                Spacing = 10,
                Visibility = isTerminal ? Visibility.Visible : Visibility.Collapsed,
            };
            terminalSettingsPanel.Children.Add(new TextBlock
            {
                Text = "終端機設定",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            terminalSettingsPanel.Children.Add(terminalTargetLabel);
            terminalSettingsPanel.Children.Add(terminalTargetComboBox);
            terminalSettingsPanel.Children.Add(terminalShellLabel);
            terminalSettingsPanel.Children.Add(terminalShellComboBox);
            terminalSettingsPanel.Children.Add(terminalWorkingDirectoryLabel);
            terminalSettingsPanel.Children.Add(terminalWorkingDirectoryModeComboBox);
            terminalSettingsPanel.Children.Add(terminalCustomWorkingDirectoryLabel);
            terminalSettingsPanel.Children.Add(terminalCustomWorkingDirectoryTextBox);
            terminalSettingsPanel.Children.Add(terminalLaunchArgumentsLabel);
            terminalSettingsPanel.Children.Add(terminalLaunchArgumentsTextBox);
            terminalSettingsPanel.Children.Add(new TextBlock
            {
                Text = "按下工具列按鈕時，會直接切到內建終端機並開一個新 tab。可額外填 -d .、-d left、-d right、-d C:\\path、-w 0。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
            });

            var builtInCommandHint = new TextBlock
            {
                Text = "這個模式會使用 Nuone Tools 內建的 Docker 部署流程，不需要另外填 command。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Visibility = isNodeDockerDeploy ? Visibility.Visible : Visibility.Collapsed,
            };

            var builtInTerminalHint = new TextBlock
            {
                Text = "這個模式會直接開啟終端機，不需要另外填 command。可切換成外部 Windows Terminal 或 Nuone Tools 內建終端機。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Visibility = isTerminalOpen ? Visibility.Visible : Visibility.Collapsed,
            };

            var terminalExecuteHint = new TextBlock
            {
                Text = "這個模式會在 Nuone Tools 內部終端機執行目前 pane 選取的腳本，不需要另外填 command。支援 *.ps1、*.bat、*.cmd、*.bash、*.sh，並會固定使用選取檔案所在目錄，依副檔名自動選擇 PowerShell、cmd 或 Git Bash。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Visibility = isTerminalExecute ? Visibility.Visible : Visibility.Collapsed,
            };

            var fileBunkerUploadHint = new TextBlock
            {
                Text = "這個模式會直接使用目前 pane 選取的檔案，並套用設定頁裡的 FileBunker 設定上傳，不需要另外填 command。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Visibility = isFileBunkerUpload ? Visibility.Visible : Visibility.Collapsed,
            };
            var storageUploadHint = new TextBlock
            {
                Text = "這個模式會直接使用目前 pane 選取的本機檔案，並套用設定頁裡已登入的 api.nuone.cl 帳號，把檔案上傳到 Storage，不需要另外填 command。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Visibility = isStorageUpload ? Visibility.Visible : Visibility.Collapsed,
            };
            var enhancePdfHint = new TextBlock
            {
                Text = "這個模式會直接使用目前 pane 選取的本機 PDF 檔案，呼叫內建的 enhance_pdf.py 流程，並在原目錄輸出同檔名加上 _enhanced.pdf，不需要另外填 command。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Visibility = isEnhancePdf ? Visibility.Visible : Visibility.Collapsed,
            };

            void RefreshIconSourceMode()
            {
                var iconSource = iconSourceComboBox.SelectedItem is ComboBoxItem { Tag: string sourceTag }
                    ? sourceTag
                    : "path";
                iconPathLabel.Visibility = string.Equals(iconSource, "path", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                iconPathRow.Visibility = string.Equals(iconSource, "path", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                iconGlyphLabel.Visibility = string.Equals(iconSource, "fluent", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                iconGlyphListLabel.Visibility = string.Equals(iconSource, "fluent", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                iconGlyphPickerButton.Visibility = string.Equals(iconSource, "fluent", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                iconGlyphTextBox.Visibility = string.Equals(iconSource, "fluent", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                iconGlyphHint.Visibility = string.Equals(iconSource, "fluent", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            void RefreshCommandMode()
            {
                RefreshIconSourceMode();

                var selectedMode = builtInActionComboBox.SelectedItem is ComboBoxItem { Tag: string modeTag }
                    ? modeTag
                    : "command";
                var deployMode = string.Equals(selectedMode, "node-docker", StringComparison.Ordinal);
                var terminalOpenMode = string.Equals(selectedMode, "terminal-open", StringComparison.Ordinal);
                var terminalExecuteMode = string.Equals(selectedMode, "terminal-execute", StringComparison.Ordinal);
                var terminalMode = terminalOpenMode || terminalExecuteMode;
                var showTerminalWorkingDirectory = terminalOpenMode;
                var fileBunkerUploadMode = string.Equals(selectedMode, "filebunker-upload", StringComparison.Ordinal);
                var storageUploadMode = string.Equals(selectedMode, "storage-upload", StringComparison.Ordinal);
                var enhancePdfMode = string.Equals(selectedMode, "enhance-pdf", StringComparison.Ordinal);

                nodeDockerSettingsPanel.Visibility = deployMode ? Visibility.Visible : Visibility.Collapsed;
                terminalSettingsPanel.Visibility = terminalMode ? Visibility.Visible : Visibility.Collapsed;
                commandPanel.Visibility = deployMode || terminalMode || fileBunkerUploadMode || storageUploadMode || enhancePdfMode ? Visibility.Collapsed : Visibility.Visible;
                builtInCommandHint.Visibility = deployMode ? Visibility.Visible : Visibility.Collapsed;
                builtInTerminalHint.Visibility = terminalOpenMode ? Visibility.Visible : Visibility.Collapsed;
                terminalExecuteHint.Visibility = terminalExecuteMode ? Visibility.Visible : Visibility.Collapsed;
                fileBunkerUploadHint.Visibility = fileBunkerUploadMode ? Visibility.Visible : Visibility.Collapsed;
                storageUploadHint.Visibility = storageUploadMode ? Visibility.Visible : Visibility.Collapsed;
                enhancePdfHint.Visibility = enhancePdfMode ? Visibility.Visible : Visibility.Collapsed;
                commandTextBox.IsReadOnly = deployMode || terminalMode || fileBunkerUploadMode || storageUploadMode || enhancePdfMode;
                terminalTargetLabel.Visibility = terminalOpenMode ? Visibility.Visible : Visibility.Collapsed;
                terminalTargetComboBox.Visibility = terminalOpenMode ? Visibility.Visible : Visibility.Collapsed;
                terminalWorkingDirectoryLabel.Visibility = showTerminalWorkingDirectory ? Visibility.Visible : Visibility.Collapsed;
                terminalWorkingDirectoryModeComboBox.Visibility = showTerminalWorkingDirectory ? Visibility.Visible : Visibility.Collapsed;
                var showCustomWorkingDirectory =
                    showTerminalWorkingDirectory &&
                    terminalWorkingDirectoryModeComboBox.SelectedItem is ComboBoxItem { Tag: ToolbarWorkingDirectoryMode.CustomPath };
                terminalCustomWorkingDirectoryLabel.Visibility = showCustomWorkingDirectory ? Visibility.Visible : Visibility.Collapsed;
                terminalCustomWorkingDirectoryTextBox.Visibility =
                    showCustomWorkingDirectory
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                var deployRunsInBuiltInTerminal =
                    nodeDockerLaunchModeComboBox.SelectedItem is ComboBoxItem { Tag: NodeDockerLaunchMode.BuiltInTerminal };
                nodeDockerBuiltInShellLabel.Visibility = deployMode && deployRunsInBuiltInTerminal
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                nodeDockerBuiltInShellComboBox.Visibility = deployMode && deployRunsInBuiltInTerminal
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (deployMode)
                {
                    commandTextBox.Text = DeployNodeDockerCommand;
                    if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                    {
                        titleTextBox.Text = "deploy";
                    }

                    return;
                }

                if (terminalOpenMode)
                {
                    commandTextBox.Text = terminalTargetComboBox.SelectedItem is ComboBoxItem { Tag: string terminalCommand }
                        ? terminalCommand
                        : OpenExternalTerminalCommand;
                    if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                    {
                        titleTextBox.Text = "terminal open";
                    }

                    return;
                }

                if (terminalExecuteMode)
                {
                    commandTextBox.Text = ExecuteInBuiltInTerminalCommand;
                    if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                    {
                        titleTextBox.Text = "terminal run";
                    }

                    return;
                }

                if (fileBunkerUploadMode)
                {
                    commandTextBox.Text = FileBunkerUploadCommand;
                    if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                    {
                        titleTextBox.Text = "upload";
                    }

                    return;
                }

                if (storageUploadMode)
                {
                    commandTextBox.Text = StorageUploadCommand;
                    if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                    {
                        titleTextBox.Text = "storage";
                    }

                    return;
                }

                if (enhancePdfMode)
                {
                    commandTextBox.Text = EnhancePdfCommand;
                    if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                    {
                        titleTextBox.Text = "pdf enhance";
                    }
                }
            }

            builtInActionComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            nodeDockerLaunchModeComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            terminalTargetComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            terminalWorkingDirectoryModeComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            iconSourceComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            RefreshCommandMode();

            var panel = new StackPanel { Spacing = 14 };
            panel.Children.Add(new TextBlock
            {
                Text = "設定工具列按鈕的名稱、圖示與 command。一般 command 會在目前作用中的 pane 路徑執行；終端機、部署與其他內建動作模式可直接走內建流程。",
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(new TextBlock { Text = "名稱" });
            panel.Children.Add(titleTextBox);
            panel.Children.Add(new TextBlock { Text = "內建動作" });
            panel.Children.Add(builtInActionComboBox);
            panel.Children.Add(nodeDockerSettingsPanel);
            panel.Children.Add(terminalSettingsPanel);
            panel.Children.Add(commandPanel);
            panel.Children.Add(builtInCommandHint);
            panel.Children.Add(builtInTerminalHint);
            panel.Children.Add(terminalExecuteHint);
            panel.Children.Add(fileBunkerUploadHint);
            panel.Children.Add(storageUploadHint);
            panel.Children.Add(enhancePdfHint);
            panel.Children.Add(new TextBlock { Text = "圖示來源" });
            panel.Children.Add(iconSourceComboBox);
            panel.Children.Add(iconPathLabel);
            panel.Children.Add(iconPathRow);
            panel.Children.Add(iconGlyphLabel);
            panel.Children.Add(iconGlyphListLabel);
            panel.Children.Add(iconGlyphPickerButton);
            panel.Children.Add(iconGlyphTextBox);
            panel.Children.Add(iconGlyphHint);
            panel.Children.Add(new TextBlock
            {
                Text = "可選本機 icon 路徑，或改用 Segoe Fluent Icons glyph。若未填寫或內容無效，就不顯示圖示。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
            });

            var dialog = new ContentDialog
            {
                Title = existingItem is null ? "新增工具列按鈕" : "編輯工具列按鈕",
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = 560,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
                PrimaryButtonText = "確定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var title = titleTextBox.Text.Trim();
            var selectedMode = builtInActionComboBox.SelectedItem is ComboBoxItem { Tag: string modeTag }
                ? modeTag
                : "command";
            var selectedTerminalTargetCommand = terminalTargetComboBox.SelectedItem is ComboBoxItem { Tag: string terminalTargetCommand }
                ? terminalTargetCommand
                : OpenExternalTerminalCommand;
            var command = string.Equals(selectedMode, "node-docker", StringComparison.Ordinal)
                ? DeployNodeDockerCommand
                : string.Equals(selectedMode, "terminal-open", StringComparison.Ordinal)
                    ? selectedTerminalTargetCommand
                : string.Equals(selectedMode, "terminal-execute", StringComparison.Ordinal)
                    ? ExecuteInBuiltInTerminalCommand
                : string.Equals(selectedMode, "filebunker-upload", StringComparison.Ordinal)
                    ? FileBunkerUploadCommand
                : string.Equals(selectedMode, "storage-upload", StringComparison.Ordinal)
                    ? StorageUploadCommand
                : string.Equals(selectedMode, "enhance-pdf", StringComparison.Ordinal)
                    ? EnhancePdfCommand
                    : commandTextBox.Text.Trim();
            var selectedIconSource = iconSourceComboBox.SelectedItem is ComboBoxItem { Tag: string iconSourceTag }
                ? iconSourceTag
                : "path";
            var iconPath = string.Equals(selectedIconSource, "path", StringComparison.Ordinal)
                ? iconPathTextBox.Text.Trim()
                : string.Empty;
            var iconGlyph = string.Equals(selectedIconSource, "fluent", StringComparison.Ordinal)
                ? iconGlyphTextBox.Text.Trim().ToUpperInvariant()
                : string.Empty;
            var nodeDockerUser = nodeDockerUserTextBox.Text.Trim();
            var nodeDockerHost = nodeDockerHostTextBox.Text.Trim();
            var nodeDockerRemoteDirectory = nodeDockerRemoteDirectoryTextBox.Text.Trim();
            var nodeDockerLaunchMode = nodeDockerLaunchModeComboBox.SelectedItem is ComboBoxItem { Tag: NodeDockerLaunchMode launchMode }
                ? launchMode
                : NodeDockerLaunchMode.ExternalWindow;
            var terminalShellKind = terminalShellComboBox.SelectedItem is ComboBoxItem { Tag: TerminalShellKind shellKind }
                ? shellKind
                : _shortcutSettings.DefaultTerminalShellKind;
            if (nodeDockerLaunchMode == NodeDockerLaunchMode.BuiltInTerminal &&
                nodeDockerBuiltInShellComboBox.SelectedItem is ComboBoxItem { Tag: TerminalShellKind deployShellKind })
            {
                terminalShellKind = deployShellKind;
            }
            var terminalWorkingDirectoryMode = terminalWorkingDirectoryModeComboBox.SelectedItem is ComboBoxItem { Tag: ToolbarWorkingDirectoryMode workingDirectoryMode }
                ? workingDirectoryMode
                : _shortcutSettings.DefaultTerminalWorkingDirectoryMode;
            var terminalCustomWorkingDirectory = terminalCustomWorkingDirectoryTextBox.Text.Trim();
            var terminalLaunchArguments = terminalLaunchArgumentsTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(command))
            {
                await ShowMessageAsync("工具列按鈕無效", "名稱與 command 都必須填寫。");
                return null;
            }

            if (IsDeployNodeDockerCommand(command) &&
                (string.IsNullOrWhiteSpace(nodeDockerUser) ||
                 string.IsNullOrWhiteSpace(nodeDockerHost) ||
                 string.IsNullOrWhiteSpace(nodeDockerRemoteDirectory)))
            {
                await ShowMessageAsync("工具列按鈕無效", "Node.js Docker 部署必須填寫 SSH 使用者、Docker 主機與遠端暫存目錄。");
                return null;
            }

            if ((IsOpenBuiltInTerminalCommand(command) || IsOpenExternalTerminalCommand(command)) &&
                terminalWorkingDirectoryMode == ToolbarWorkingDirectoryMode.CustomPath &&
                (string.IsNullOrWhiteSpace(terminalCustomWorkingDirectory) || !IsNavigableDirectoryPath(terminalCustomWorkingDirectory)))
            {
                await ShowMessageAsync("工具列按鈕無效", "終端機的自訂工作目錄不存在。");
                return null;
            }

            return new ToolbarCommandItem
            {
                Id = existingItem?.Id ?? Guid.NewGuid(),
                Title = title,
                Command = command,
                IconPath = iconPath,
                IconGlyph = iconGlyph,
                NodeDockerUser = nodeDockerUser,
                NodeDockerHost = nodeDockerHost,
                NodeDockerRemoteDirectory = nodeDockerRemoteDirectory,
                NodeDockerLaunchMode = nodeDockerLaunchMode,
                TerminalShellKind = terminalShellKind,
                TerminalWorkingDirectoryMode = terminalWorkingDirectoryMode,
                TerminalCustomWorkingDirectory = terminalCustomWorkingDirectory,
                TerminalLaunchArguments = terminalLaunchArguments,
            };
        }

        private static void SelectComboBoxItemByTag(ComboBox comboBox, object tagValue)
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (Equals(item.Tag, tagValue))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private async Task<string?> ShowFluentIconPickerDialogAsync(string selectedGlyphCode)
        {
            var rawSelectedCode = (selectedGlyphCode ?? string.Empty).Trim();
            var normalizedSelectedCode = System.Text.RegularExpressions.Regex.IsMatch(rawSelectedCode, "^[0-9A-Fa-f]{4,6}$")
                ? rawSelectedCode.ToUpperInvariant()
                : string.Empty;
            var searchTextBox = new TextBox
            {
                PlaceholderText = "搜尋名稱或 glyph code，例如 Folder / E8B7",
            };
            var selectedTextBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(normalizedSelectedCode)
                    ? "尚未選擇"
                    : $"已選擇：{normalizedSelectedCode}",
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 8),
            };
            var tabView = new TabView
            {
                IsAddTabButtonVisible = false,
                MinHeight = 540,
            };
            string? pickedCode = string.IsNullOrWhiteSpace(normalizedSelectedCode)
                ? null
                : normalizedSelectedCode;

            void UpdateSelectedText(string? code)
            {
                selectedTextBlock.Text = string.IsNullOrWhiteSpace(code)
                    ? "尚未選擇"
                    : $"已選擇：{code}";
            }

            var allOptions = ToolbarFluentIconCategories
                .SelectMany(category => category.Options)
                .GroupBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            static GridView BuildFluentGridView()
            {
                return new GridView
                {
                    SelectionMode = ListViewSelectionMode.Single,
                    IsItemClickEnabled = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemContainerStyle = null,
                    ItemsPanel = (ItemsPanelTemplate)XamlReader.Load(
                        @"<ItemsPanelTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                            <ItemsWrapGrid Orientation=""Horizontal"" MaximumRowsOrColumns=""6"" />
                        </ItemsPanelTemplate>"),
                };
            }

            GridViewItem BuildFluentGridItem(string code, string? name)
            {
                var glyph = ToolbarCommandItem.NormalizeFluentGlyph(code);
                var stack = new StackPanel
                {
                    Spacing = string.IsNullOrWhiteSpace(name) ? 0 : 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = glyph,
                            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                            FontSize = 24,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                    },
                };

                if (!string.IsNullOrWhiteSpace(name))
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = name,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.WrapWholeWords,
                    });
                }

                var card = new Border
                {
                    Width = 130,
                    Height = string.IsNullOrWhiteSpace(name) ? 64 : 88,
                    Padding = new Thickness(10),
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(GetBrushColor("InputBrush", "#1B1621")),
                    BorderBrush = new SolidColorBrush(GetBrushColor("PanelStrokeBrush", "#3A3146")),
                    BorderThickness = new Thickness(1),
                    Child = stack,
                };

                return new GridViewItem
                {
                    Tag = code,
                    Content = card,
                };
            }

            void WireGridView(GridView gridView)
            {
                gridView.ItemClick += (_, args) =>
                {
                    if (args.ClickedItem is GridViewItem { Tag: string code })
                    {
                        pickedCode = code;
                        UpdateSelectedText(code);
                        gridView.SelectedItem = args.ClickedItem;
                    }
                };

                gridView.SelectionChanged += (_, _) =>
                {
                    if (gridView.SelectedItem is GridViewItem { Tag: string code })
                    {
                        pickedCode = code;
                        UpdateSelectedText(code);
                    }
                };
            }

            static ScrollViewer WrapGrid(GridView gridView)
            {
                return new ScrollViewer
                {
                    Content = gridView,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
            }

            GridView BuildSafeGridView(IEnumerable<FluentIconOption> options)
            {
                var gridView = BuildFluentGridView();
                foreach (var option in options)
                {
                    var item = BuildFluentGridItem(option.Code, option.Name);
                    gridView.Items.Add(item);
                    if (string.Equals(option.Code, normalizedSelectedCode, StringComparison.OrdinalIgnoreCase))
                    {
                        gridView.SelectedItem = item;
                    }
                }
                WireGridView(gridView);
                return gridView;
            }

            GridView BuildAdvancedGridView(FluentIconRange range)
            {
                var gridView = BuildFluentGridView();
                for (var codePoint = range.StartCodePoint; codePoint <= range.EndCodePoint; codePoint++)
                {
                    var code = codePoint.ToString("X4", CultureInfo.InvariantCulture);
                    var item = BuildFluentGridItem(code, null);
                    gridView.Items.Add(item);
                    if (string.Equals(code, normalizedSelectedCode, StringComparison.OrdinalIgnoreCase))
                    {
                        gridView.SelectedItem = item;
                    }
                }
                WireGridView(gridView);
                return gridView;
            }

            TabView BuildSafeModeTabView()
            {
                var safeTabView = new TabView
                {
                    IsAddTabButtonVisible = false,
                };

                safeTabView.TabItems.Add(new TabViewItem
                {
                    Header = "全部",
                    Content = WrapGrid(BuildSafeGridView(allOptions)),
                });

                foreach (var category in ToolbarFluentIconCategories)
                {
                    safeTabView.TabItems.Add(new TabViewItem
                    {
                        Header = category.Title,
                        Content = WrapGrid(BuildSafeGridView(category.Options)),
                    });
                }

                if (safeTabView.TabItems.Count > 0)
                {
                    safeTabView.SelectedIndex = 0;
                }

                return safeTabView;
            }

            TabView BuildAdvancedModeTabView()
            {
                var advancedTabView = new TabView
                {
                    IsAddTabButtonVisible = false,
                };

                foreach (var range in ToolbarFluentIconAdvancedRanges)
                {
                    advancedTabView.TabItems.Add(new TabViewItem
                    {
                        Header = range.Title,
                        Content = WrapGrid(BuildAdvancedGridView(range)),
                    });
                }

                if (advancedTabView.TabItems.Count > 0)
                {
                    advancedTabView.SelectedIndex = 0;
                }

                return advancedTabView;
            }

            tabView.TabItems.Add(new TabViewItem
            {
                Header = "安全清單",
                Content = BuildSafeModeTabView(),
            });
            tabView.TabItems.Add(new TabViewItem
            {
                Header = "進階(大量 glyph)",
                Content = BuildAdvancedModeTabView(),
            });

            static IEnumerable<(GridView GridView, TabViewItem? ParentTab, TabViewItem? ChildTab)> EnumerateGridTargets(
                object? content,
                TabViewItem? parentTab = null)
            {
                if (content is ScrollViewer { Content: GridView singleGridView })
                {
                    yield return (singleGridView, parentTab, null);
                    yield break;
                }

                if (content is not TabView nestedTabView)
                {
                    yield break;
                }

                foreach (var nestedTab in nestedTabView.TabItems.OfType<TabViewItem>())
                {
                    if (nestedTab.Content is ScrollViewer { Content: GridView nestedGridView })
                    {
                        yield return (nestedGridView, parentTab, nestedTab);
                        continue;
                    }

                    foreach (var target in EnumerateGridTargets(nestedTab.Content, nestedTab))
                    {
                        yield return (target.GridView, parentTab ?? target.ParentTab, nestedTab);
                    }
                }
            }

            if (tabView.TabItems.Count > 0)
            {
                tabView.SelectedIndex = 0;
            }

            void ApplySearchSelection(string? rawValue)
            {
                var value = (rawValue ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                foreach (var tab in tabView.TabItems.OfType<TabViewItem>())
                {
                    foreach (var target in EnumerateGridTargets(tab.Content, tab))
                    {
                        var targetItem = target.GridView.Items
                            .OfType<GridViewItem>()
                            .FirstOrDefault(item =>
                            {
                                var code = item.Tag as string;
                                var name = ((item.Content as Border)?.Child as StackPanel)?.Children
                                    .OfType<TextBlock>()
                                    .FirstOrDefault()?.Text ?? string.Empty;
                                return string.Equals(code, value, StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains(value, StringComparison.OrdinalIgnoreCase);
                            });
                        if (targetItem is null)
                        {
                            continue;
                        }

                        tabView.SelectedItem = tab;
                        if (tab.Content is TabView nestedTabView && target.ChildTab is not null)
                        {
                            nestedTabView.SelectedItem = target.ChildTab;
                        }
                        target.GridView.SelectedItem = targetItem;
                        target.GridView.ScrollIntoView(targetItem);
                        var matchedCode = targetItem.Tag as string ?? value;
                        pickedCode = matchedCode;
                        UpdateSelectedText(matchedCode);
                        return;
                    }
                }
            }

            searchTextBox.TextChanged += (_, _) => ApplySearchSelection(searchTextBox.Text);

            var confirmButton = new Button
            {
                Content = "確定",
                MinWidth = 120,
            };
            var cancelButton = new Button
            {
                Content = "取消",
                MinWidth = 120,
            };

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children =
                {
                    cancelButton,
                    confirmButton,
                },
            };

            var root = new Grid
            {
                Background = new SolidColorBrush(GetBrushColor("SurfaceBrush", "#221C2B")),
                RequestedTheme = RootLayout.RequestedTheme,
                Padding = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                    new RowDefinition { Height = GridLength.Auto },
                },
            };
            root.Children.Add(new TextBlock
            {
                Text = "選擇 Segoe Fluent Icon",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12),
            });

            var pickerHeaderPanel = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 12),
                Children =
                {
                    selectedTextBlock,
                    searchTextBox,
                },
            };
            Grid.SetRow(pickerHeaderPanel, 1);
            root.Children.Add(pickerHeaderPanel);

            Grid.SetRow(tabView, 2);
            root.Children.Add(tabView);
            Grid.SetRow(buttonRow, 3);
            buttonRow.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(buttonRow);

            ApplySearchSelection(searchTextBox.Text);

            var pickerWindow = new Window
            {
                Content = root,
            };
            pickerWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(980, 760));

            var completion = new TaskCompletionSource<string?>();
            var completed = false;

            void Complete(string? result)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                completion.TrySetResult(result);
            }

            confirmButton.Click += (_, _) =>
            {
                Complete(pickedCode);
                pickerWindow.Close();
            };
            cancelButton.Click += (_, _) =>
            {
                Complete(null);
                pickerWindow.Close();
            };

            pickerWindow.Closed += (_, _) => Complete(null);
            pickerWindow.Activate();

            return await completion.Task;
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "知道了",
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            await dialog.ShowAsync();
        }

        private async Task<bool> ShowBackupAutomationEditorAsync(
            BackupAutomationProfile profile,
            string title = "編輯自動化工作")
        {
            var nameTextBox = new TextBox { Text = profile.Name };
            var jobTypeComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "檔案備份", Tag = AutomationJobType.FileBackup },
                    new ComboBoxItem { Content = "MongoDB 備份", Tag = AutomationJobType.MongoBackup },
                },
                SelectedIndex = profile.JobType == AutomationJobType.MongoBackup ? 1 : 0,
            };
            var sourcePathTextBox = new TextBox { Text = profile.SourcePath };
            var destinationPathTextBox = new TextBox { Text = profile.DestinationPath };
            var modeComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "備份複製", Tag = BackupAutomationMode.Copy },
                    new ComboBoxItem { Content = "同步鏡像", Tag = BackupAutomationMode.Mirror },
                },
                SelectedIndex = profile.Mode == BackupAutomationMode.Mirror ? 1 : 0,
            };
            var logDirectoryPathTextBox = new TextBox
            {
                Text = ResolveBackupAutomationLogDirectoryPath(profile.LogDirectoryPath),
                PlaceholderText = CurrentLogDirectoryPath,
            };
            var excludedFolderNamesTextBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 96,
                VerticalContentAlignment = VerticalAlignment.Top,
                PlaceholderText = ".vs, .vscode, bin, obj, packages, node_modules",
                Text = FormatExcludedFolderNamesForEditor(profile.ExcludedFolderNamesText),
            };
            var mongoToolPathTextBox = new TextBox { Text = profile.MongoToolPath };
            var mongoConnectionStringTextBox = new TextBox
            {
                Text = profile.MongoConnectionString,
                TextWrapping = TextWrapping.Wrap,
            };
            var mongoDatabaseNameTextBox = new TextBox
            {
                Text = profile.MongoDatabaseName,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 64,
            };
            var mongoRetentionCountTextBox = new TextBox { Text = profile.MongoRetentionCountText };
            var mongoUseArchiveCheckBox = new CheckBox { Content = "Archive", IsChecked = profile.MongoUseArchive };
            var mongoUseGzipCheckBox = new CheckBox { Content = "GZip", IsChecked = profile.MongoUseGzip };
            var scheduleTypeComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "每隔幾分鐘", Tag = AutomationScheduleType.Interval },
                    new ComboBoxItem { Content = "每天固定時間", Tag = AutomationScheduleType.Daily },
                    new ComboBoxItem { Content = "每週固定時間", Tag = AutomationScheduleType.Weekly },
                },
                SelectedIndex = profile.ScheduleType switch
                {
                    AutomationScheduleType.Daily => 1,
                    AutomationScheduleType.Weekly => 2,
                    _ => 0,
                },
            };
            var intervalTextBox = new TextBox { Text = profile.IntervalMinutesText };
            var scheduleTimeTextBox = new TextBox { Text = profile.ScheduleTimeText };
            var monday = new CheckBox { Content = "週一", IsChecked = profile.WeeklyMondaySelected };
            var tuesday = new CheckBox { Content = "週二", IsChecked = profile.WeeklyTuesdaySelected };
            var wednesday = new CheckBox { Content = "週三", IsChecked = profile.WeeklyWednesdaySelected };
            var thursday = new CheckBox { Content = "週四", IsChecked = profile.WeeklyThursdaySelected };
            var friday = new CheckBox { Content = "週五", IsChecked = profile.WeeklyFridaySelected };
            var saturday = new CheckBox { Content = "週六", IsChecked = profile.WeeklySaturdaySelected };
            var sunday = new CheckBox { Content = "週日", IsChecked = profile.WeeklySundaySelected };
            var runMissedCheckBox = new CheckBox
            {
                Content = "啟動 app 時補跑錯過的排程",
                IsChecked = profile.RunMissedOnStartup,
            };
            var notificationEnabledCheckBox = new CheckBox
            {
                Content = "寫入通知記錄",
                IsChecked = profile.NotificationEnabled,
            };
            var toastEnabledCheckBox = new CheckBox
            {
                Content = "顯示 Windows toast",
                IsChecked = profile.ToastEnabled,
            };

            var filePanel = new StackPanel { Spacing = 8 };
            filePanel.Children.Add(new TextBlock { Text = "來源路徑" });
            filePanel.Children.Add(sourcePathTextBox);
            filePanel.Children.Add(new TextBlock { Text = "模式" });
            filePanel.Children.Add(modeComboBox);
            filePanel.Children.Add(new TextBlock { Text = "排除資料夾名稱（可用逗號或換行分隔）" });
            filePanel.Children.Add(new ScrollViewer
            {
                Content = excludedFolderNamesTextBox,
                MaxHeight = 140,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
            filePanel.Children.Add(new TextBlock
            {
                Text = "比對的是資料夾名稱本身，適合排除 .vs、bin、obj、packages、node_modules 這類開發產物目錄。",
                Opacity = 0.78,
                TextWrapping = TextWrapping.Wrap,
            });
            var mongoOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
            mongoOptions.Children.Add(mongoUseArchiveCheckBox);
            mongoOptions.Children.Add(mongoUseGzipCheckBox);

            var mongoPanel = new StackPanel { Spacing = 8 };
            mongoPanel.Children.Add(new TextBlock { Text = "mongodump 路徑" });
            mongoPanel.Children.Add(mongoToolPathTextBox);
            mongoPanel.Children.Add(new TextBlock { Text = "Mongo URI" });
            mongoPanel.Children.Add(mongoConnectionStringTextBox);
            mongoPanel.Children.Add(new TextBlock { Text = "資料庫名稱" });
            mongoPanel.Children.Add(mongoDatabaseNameTextBox);
            mongoPanel.Children.Add(new TextBlock { Text = "保留份數" });
            mongoPanel.Children.Add(mongoRetentionCountTextBox);
            mongoPanel.Children.Add(mongoOptions);

            var weeklyPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            weeklyPanel.Children.Add(monday);
            weeklyPanel.Children.Add(tuesday);
            weeklyPanel.Children.Add(wednesday);
            weeklyPanel.Children.Add(thursday);
            weeklyPanel.Children.Add(friday);
            weeklyPanel.Children.Add(saturday);
            weeklyPanel.Children.Add(sunday);

            var intervalPanel = new StackPanel { Spacing = 8 };
            intervalPanel.Children.Add(new TextBlock { Text = "間隔分鐘" });
            intervalPanel.Children.Add(intervalTextBox);

            var scheduledPanel = new StackPanel { Spacing = 8 };
            scheduledPanel.Children.Add(new TextBlock { Text = "執行時間（HH:mm）" });
            scheduledPanel.Children.Add(scheduleTimeTextBox);
            scheduledPanel.Children.Add(weeklyPanel);

            void RefreshVisibility()
            {
                var isMongo = jobTypeComboBox.SelectedIndex == 1;
                filePanel.Visibility = isMongo ? Visibility.Collapsed : Visibility.Visible;
                mongoPanel.Visibility = isMongo ? Visibility.Visible : Visibility.Collapsed;

                var isInterval = scheduleTypeComboBox.SelectedIndex == 0;
                intervalPanel.Visibility = isInterval ? Visibility.Visible : Visibility.Collapsed;
                scheduledPanel.Visibility = isInterval ? Visibility.Collapsed : Visibility.Visible;
                weeklyPanel.Visibility = scheduleTypeComboBox.SelectedIndex == 2
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            jobTypeComboBox.SelectionChanged += (_, _) => RefreshVisibility();
            scheduleTypeComboBox.SelectionChanged += (_, _) => RefreshVisibility();
            RefreshVisibility();

            var panel = new StackPanel { Spacing = 10, Width = 720 };
            panel.Children.Add(new TextBlock { Text = "名稱" });
            panel.Children.Add(nameTextBox);
            panel.Children.Add(new TextBlock { Text = "工作類型" });
            panel.Children.Add(jobTypeComboBox);
            panel.Children.Add(new TextBlock { Text = "目的地" });
            panel.Children.Add(destinationPathTextBox);
            panel.Children.Add(new TextBlock { Text = "工作 log 目錄" });
            panel.Children.Add(logDirectoryPathTextBox);
            panel.Children.Add(new TextBlock
            {
                Text = $"每個自動化工作都會寫到自己的 log 目錄；未填時會沿用目前全域目錄：{CurrentLogDirectoryPath}",
                Opacity = 0.78,
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(filePanel);
            panel.Children.Add(mongoPanel);
            panel.Children.Add(new TextBlock { Text = "排程類型" });
            panel.Children.Add(scheduleTypeComboBox);
            panel.Children.Add(intervalPanel);
            panel.Children.Add(scheduledPanel);
            panel.Children.Add(runMissedCheckBox);
            panel.Children.Add(notificationEnabledCheckBox);
            panel.Children.Add(toastEnabledCheckBox);
            panel.Children.Add(new TextBlock
            {
                Text = "排程只會在 app 開著時生效；如果勾這個，超過排定時間後才開 app 也會立刻補跑。通知記錄與 toast 可分開控制。",
                Opacity = 0.78,
                TextWrapping = TextWrapping.Wrap,
            });

            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = 650,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
                PrimaryButtonText = "儲存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }

            var jobType = jobTypeComboBox.SelectedIndex == 1
                ? AutomationJobType.MongoBackup
                : AutomationJobType.FileBackup;
            var scheduleType = scheduleTypeComboBox.SelectedIndex switch
            {
                1 => AutomationScheduleType.Daily,
                2 => AutomationScheduleType.Weekly,
                _ => AutomationScheduleType.Interval,
            };
            if (string.IsNullOrWhiteSpace(destinationPathTextBox.Text))
            {
                await ShowMessageAsync("自動化工作無效", "目的地必須填寫。");
                return false;
            }
            if (jobType == AutomationJobType.FileBackup &&
                (string.IsNullOrWhiteSpace(sourcePathTextBox.Text) ||
                 (!File.Exists(sourcePathTextBox.Text.Trim()) && !Directory.Exists(sourcePathTextBox.Text.Trim()))))
            {
                await ShowMessageAsync("自動化工作無效", "來源路徑不存在。");
                return false;
            }
            if (jobType == AutomationJobType.MongoBackup &&
                string.IsNullOrWhiteSpace(mongoConnectionStringTextBox.Text))
            {
                await ShowMessageAsync("自動化工作無效", "Mongo URI 必須填寫。");
                return false;
            }
            if (!int.TryParse(intervalTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalMinutes) ||
                intervalMinutes <= 0)
            {
                await ShowMessageAsync("自動化工作無效", "間隔分鐘必須是大於 0 的整數。");
                return false;
            }
            if (scheduleType != AutomationScheduleType.Interval &&
                !TryParseScheduleTimeText(scheduleTimeTextBox.Text.Trim(), out _))
            {
                await ShowMessageAsync("自動化工作無效", "時間格式必須是 HH:mm。");
                return false;
            }

            _ = int.TryParse(
                mongoRetentionCountTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var retentionCount);
            retentionCount = retentionCount > 0 ? retentionCount : 7;

            profile.Name = nameTextBox.Text.Trim();
            profile.JobType = jobType;
            profile.SourcePath = sourcePathTextBox.Text.Trim();
            profile.DestinationPath = destinationPathTextBox.Text.Trim();
            profile.Mode = modeComboBox.SelectedIndex == 1 ? BackupAutomationMode.Mirror : BackupAutomationMode.Copy;
            profile.ExcludedFolderNamesText = FormatExcludedFolderNamesForStorage(excludedFolderNamesTextBox.Text);
            profile.LogDirectoryPath = ResolveBackupAutomationLogDirectoryPath(logDirectoryPathTextBox.Text);
            profile.MongoToolPath = mongoToolPathTextBox.Text.Trim();
            profile.MongoConnectionString = mongoConnectionStringTextBox.Text.Trim();
            profile.MongoDatabaseName = mongoDatabaseNameTextBox.Text.Trim();
            profile.MongoRetentionCount = retentionCount;
            profile.MongoRetentionCountText = retentionCount.ToString(CultureInfo.InvariantCulture);
            profile.MongoUseArchive = mongoUseArchiveCheckBox.IsChecked == true;
            profile.MongoUseGzip = mongoUseGzipCheckBox.IsChecked == true;
            profile.ScheduleType = scheduleType;
            profile.IntervalMinutes = intervalMinutes;
            profile.IntervalMinutesText = intervalMinutes.ToString(CultureInfo.InvariantCulture);
            profile.ScheduleTimeText = scheduleTimeTextBox.Text.Trim();
            profile.RunMissedOnStartup = runMissedCheckBox.IsChecked == true;
            profile.NotificationEnabled = notificationEnabledCheckBox.IsChecked == true;
            profile.ToastEnabled = toastEnabledCheckBox.IsChecked == true;
            profile.WeeklyDaysMask = 0;
            profile.SetWeekdaySelected(DayOfWeek.Monday, monday.IsChecked == true);
            profile.SetWeekdaySelected(DayOfWeek.Tuesday, tuesday.IsChecked == true);
            profile.SetWeekdaySelected(DayOfWeek.Wednesday, wednesday.IsChecked == true);
            profile.SetWeekdaySelected(DayOfWeek.Thursday, thursday.IsChecked == true);
            profile.SetWeekdaySelected(DayOfWeek.Friday, friday.IsChecked == true);
            profile.SetWeekdaySelected(DayOfWeek.Saturday, saturday.IsChecked == true);
            profile.SetWeekdaySelected(DayOfWeek.Sunday, sunday.IsChecked == true);
            return true;
        }

        private static string FormatExcludedFolderNamesForEditor(string rawValue)
        {
            return string.Join(
                Environment.NewLine,
                SplitExcludedFolderNames(rawValue));
        }

        private static string FormatExcludedFolderNamesForStorage(string rawValue)
        {
            return string.Join(
                Environment.NewLine,
                SplitExcludedFolderNames(rawValue));
        }

        private static IEnumerable<string> SplitExcludedFolderNames(string rawValue)
        {
            return (rawValue ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<bool> ShowAutoExtractProfileEditorAsync(
            AutoExtractProfile profile,
            string title = "編輯自動解壓")
        {
            var passwordValues = profile.Passwords
                .Select(static item => item.Value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var nameTextBox = new TextBox { Text = profile.Name };
            var watchPathTextBox = new TextBox { Text = profile.WatchPath };
            var extractorPathTextBox = new TextBox { Text = profile.ExtractorPath };
            var extensionFilterTextBox = new TextBox { Text = profile.ExtensionFilter };
            var passwordsTextBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                Height = Math.Max(240, (passwordValues.Count + 1) * 28),
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            passwordsTextBox.Text = string.Join(Environment.NewLine, passwordValues);

            var panel = new StackPanel { Spacing = 10, Width = 680 };
            var notificationEnabledCheckBox = new CheckBox
            {
                Content = "寫入通知記錄",
                IsChecked = profile.NotificationEnabled,
            };
            var toastEnabledCheckBox = new CheckBox
            {
                Content = "顯示 Windows toast",
                IsChecked = profile.ToastEnabled,
            };
            panel.Children.Add(new TextBlock { Text = "名稱" });
            panel.Children.Add(nameTextBox);
            panel.Children.Add(new TextBlock { Text = "監看目錄" });
            panel.Children.Add(watchPathTextBox);
            panel.Children.Add(new TextBlock { Text = "解壓工具路徑" });
            panel.Children.Add(extractorPathTextBox);
            panel.Children.Add(new TextBlock { Text = "副檔名篩選" });
            panel.Children.Add(extensionFilterTextBox);
            panel.Children.Add(new TextBlock { Text = "密碼清單（每行一組）" });
            panel.Children.Add(new TextBlock
            {
                Text = $"目前已載入 {passwordValues.Count} 組密碼",
                Opacity = 0.78,
            });
            panel.Children.Add(notificationEnabledCheckBox);
            panel.Children.Add(toastEnabledCheckBox);
            panel.Children.Add(new ScrollViewer
            {
                Content = passwordsTextBox,
                Height = 240,
                HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            });

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "儲存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }

            var watchPath = watchPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(watchPath) || !Directory.Exists(watchPath))
            {
                await ShowMessageAsync("自動解壓無效", "監看目錄不存在。");
                return false;
            }

            profile.Name = nameTextBox.Text.Trim();
            profile.WatchPath = watchPath;
            profile.ExtractorPath = extractorPathTextBox.Text.Trim();
            profile.ExtensionFilter = extensionFilterTextBox.Text.Trim();
            profile.NotificationEnabled = notificationEnabledCheckBox.IsChecked == true;
            profile.ToastEnabled = toastEnabledCheckBox.IsChecked == true;
            profile.Passwords.Clear();
            foreach (var password in passwordsTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal))
            {
                profile.Passwords.Add(new AutoExtractPasswordItem
                {
                    Value = password,
                    ParentProfile = profile,
                });
            }
            return true;
        }

        private async Task<string?> ShowAutoExtractPasswordPromptAsync(string title, string message)
        {
            var passwordBox = new PasswordBox
            {
                PlaceholderText = "輸入新的解壓密碼",
            };

            var content = new StackPanel
            {
                Spacing = 12,
            };
            content.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new TextBlock
            {
                Text = "可直接輸入新密碼，會自動加入這個解壓工作的密碼清單，並立即重試。",
                Opacity = 0.76,
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(passwordBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "加入並重試",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var password = passwordBox.Password?.Trim();
            return string.IsNullOrWhiteSpace(password) ? null : password;
        }

        private async Task ShowSettingsDialogAsync()
        {
            _isSettingsDialogOpen = true;

            var editingCopyKey = _shortcutSettings.CopyToOtherPaneKey;
            var editingMoveKey = _shortcutSettings.MoveToOtherPaneKey;
            var editingNavigateUpKey = _shortcutSettings.NavigateUpKey;
            var editingCreateFolderKey = _shortcutSettings.CreateFolderKey;
            var captureTarget = ShortcutCaptureTarget.None;

            var copyShortcutTextBox = CreateShortcutTextBox(editingCopyKey);
            var moveShortcutTextBox = CreateShortcutTextBox(editingMoveKey);
            var navigateUpShortcutTextBox = CreateShortcutTextBox(editingNavigateUpKey);
            var createFolderShortcutTextBox = CreateShortcutTextBox(editingCreateFolderKey);
            createFolderShortcutTextBox.Text = FormatCreateFolderShortcutKey(editingCreateFolderKey);
            var captureHintText = new TextBlock
            {
                Text = "按「修改」後，再按下實體鍵。",
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
            };

            var dialogRoot = new Grid
            {
                Width = 1360,
                Height = 860,
            };
            dialogRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            dialogRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dialogRoot.AddHandler(
                UIElement.KeyDownEvent,
                new KeyEventHandler((_, args) =>
                {
                    if (captureTarget == ShortcutCaptureTarget.None)
                    {
                        return;
                    }

                    args.Handled = true;

                    if (args.Key is Windows.System.VirtualKey.Tab
                        or Windows.System.VirtualKey.LeftShift
                        or Windows.System.VirtualKey.RightShift
                        or Windows.System.VirtualKey.LeftControl
                        or Windows.System.VirtualKey.RightControl
                        or Windows.System.VirtualKey.LeftMenu
                        or Windows.System.VirtualKey.RightMenu)
                    {
                        return;
                    }

                    if (captureTarget == ShortcutCaptureTarget.CopyToOtherPane)
                    {
                        editingCopyKey = NormalizeCapturedKey(args.Key);
                        copyShortcutTextBox.Text = FormatShortcutKey(editingCopyKey);
                    }
                    else if (captureTarget == ShortcutCaptureTarget.MoveToOtherPane)
                    {
                        editingMoveKey = NormalizeCapturedKey(args.Key);
                        moveShortcutTextBox.Text = FormatShortcutKey(editingMoveKey);
                    }
                    else if (captureTarget == ShortcutCaptureTarget.NavigateUp)
                    {
                        editingNavigateUpKey = NormalizeCapturedKey(args.Key);
                        navigateUpShortcutTextBox.Text = FormatShortcutKey(editingNavigateUpKey);
                    }
                    else if (captureTarget == ShortcutCaptureTarget.CreateFolder)
                    {
                        editingCreateFolderKey = NormalizeCapturedKey(args.Key);
                        createFolderShortcutTextBox.Text = FormatCreateFolderShortcutKey(editingCreateFolderKey);
                    }

                    captureTarget = ShortcutCaptureTarget.None;
                    captureHintText.Text = "已擷取按鍵，按儲存即可套用。";
                }),
                true);

            var sidebar = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 22, 20, 29)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 58, 49, 70)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(18),
            };

            var sidebarStack = new StackPanel { Spacing = 18 };
            sidebarStack.Children.Add(new TextBlock
            {
                Text = "設定",
                FontSize = 26,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            sidebarStack.Children.Add(new TextBox
            {
                PlaceholderText = "在設定中尋找",
                IsReadOnly = true,
            });
            sidebarStack.Children.Add(CreateSettingsNavItem("一般", true));
            sidebarStack.Children.Add(CreateSettingsNavItem("外觀", false));
            sidebarStack.Children.Add(CreateSettingsNavItem("快捷鍵", false));
            sidebar.Child = sidebarStack;
            dialogRoot.Children.Add(sidebar);

            var contentScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(32, 26, 28, 26),
            };
            Grid.SetColumn(contentScrollViewer, 1);

            var contentStack = new StackPanel { Spacing = 18 };
            contentStack.Children.Add(new TextBlock
            {
                Text = "一般",
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            contentStack.Children.Add(new TextBlock
            {
                Text = "設定常用鍵盤快捷鍵。變更後會立即套用，並儲存到本機 config。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
            });

            var shortcutCard = CreateSettingsCard("快捷鍵");
            var shortcutCardContent = (StackPanel)shortcutCard.Child!;
            shortcutCardContent.Children.Add(CreateShortcutSettingRow(
                "複製到另一個 Pane",
                "目前作用中的 pane 按下後，複製選取項目到另一側。",
                copyShortcutTextBox,
                () =>
                {
                    captureTarget = ShortcutCaptureTarget.CopyToOtherPane;
                    copyShortcutTextBox.Text = "請按任意鍵...";
                    captureHintText.Text = "正在擷取「複製到另一個 Pane」快捷鍵...";
                    _ = dialogRoot.Focus(FocusState.Programmatic);
                }));
            shortcutCardContent.Children.Add(CreateShortcutSettingRow(
                "移動到另一個 Pane",
                "目前作用中的 pane 按下後，將選取項目搬移到另一側。",
                moveShortcutTextBox,
                () =>
                {
                    captureTarget = ShortcutCaptureTarget.MoveToOtherPane;
                    moveShortcutTextBox.Text = "請按任意鍵...";
                    captureHintText.Text = "正在擷取「移動到另一個 Pane」快捷鍵...";
                    _ = dialogRoot.Focus(FocusState.Programmatic);
                }));
            shortcutCardContent.Children.Add(CreateShortcutSettingRow(
                "上一層",
                "目前作用中的 pane 按下後，回到上一層目錄。",
                navigateUpShortcutTextBox,
                () =>
                {
                    captureTarget = ShortcutCaptureTarget.NavigateUp;
                    navigateUpShortcutTextBox.Text = "請按任意鍵...";
                    captureHintText.Text = "正在擷取「上一層」快捷鍵...";
                    _ = dialogRoot.Focus(FocusState.Programmatic);
                }));
            shortcutCardContent.Children.Add(CreateShortcutSettingRow(
                "新增資料夾",
                "目前作用中的 pane 按下後，使用 Ctrl + Shift + [這顆鍵] 新增資料夾。",
                createFolderShortcutTextBox,
                () =>
                {
                    captureTarget = ShortcutCaptureTarget.CreateFolder;
                    createFolderShortcutTextBox.Text = "請按任意鍵...";
                    captureHintText.Text = "正在擷取「新增資料夾」快捷鍵...";
                    _ = dialogRoot.Focus(FocusState.Programmatic);
                }));
            shortcutCardContent.Children.Add(captureHintText);
            contentStack.Children.Add(shortcutCard);

            contentScrollViewer.Content = contentStack;
            dialogRoot.Children.Add(contentScrollViewer);

            var dialog = new ContentDialog
            {
                Content = dialogRoot,
                PrimaryButtonText = "儲存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = true,
                MinWidth = 1480,
                MaxWidth = 1680,
                Width = 1520,
                MinHeight = 920,
                MaxHeight = 980,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            try
            {
                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    return;
                }

                if (editingCopyKey == editingMoveKey ||
                    editingCopyKey == editingNavigateUpKey ||
                    editingMoveKey == editingNavigateUpKey)
                {
                    await ShowMessageAsync("快捷鍵重複", "單鍵快捷鍵不能使用相同按鍵。");
                    return;
                }

                _shortcutSettings = new ShortcutSettings
                {
                    CopyToOtherPaneKey = editingCopyKey,
                    MoveToOtherPaneKey = editingMoveKey,
                    NavigateUpKey = editingNavigateUpKey,
                    CreateFolderKey = editingCreateFolderKey,
                };

                SaveShortcutSettingsSafe();
            }
            finally
            {
                _isSettingsDialogOpen = false;
            }
        }

        private static TextBox CreateShortcutTextBox(Windows.System.VirtualKey key)
        {
            return new TextBox
            {
                Text = FormatShortcutKey(key),
                IsReadOnly = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        }

        private static StackPanel CreateShortcutSettingRow(string title, string description, TextBox shortcutTextBox, Action startCapture)
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            row.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
            });

            var editorRow = new Grid { ColumnSpacing = 10 };
            editorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            editorRow.Children.Add(shortcutTextBox);

            var captureButton = new Button
            {
                Content = "修改",
                MinWidth = 88,
                MinHeight = 38,
                Padding = new Thickness(14, 8, 14, 8),
                CornerRadius = new CornerRadius(10),
            };
            captureButton.Click += (_, _) => startCapture();
            Grid.SetColumn(captureButton, 1);
            editorRow.Children.Add(captureButton);

            row.Children.Add(editorRow);
            return row;
        }

        private static Border CreateSettingsCard(string title)
        {
            var cardContent = new StackPanel { Spacing = 14 };
            cardContent.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });

            return new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 31, 28, 36)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 75, 69, 84)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22),
                Child = cardContent,
            };
        }

        private static Border CreateSettingsNavItem(string title, bool isSelected)
        {
            return new Border
            {
                Background = new SolidColorBrush(isSelected
                    ? ColorHelper.FromArgb(255, 42, 37, 47)
                    : ColorHelper.FromArgb(0, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(isSelected
                    ? ColorHelper.FromArgb(255, 75, 69, 84)
                    : ColorHelper.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Child = new TextBlock
                {
                    Text = title,
                    FontSize = 16,
                    FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                },
            };
        }
    }
}
