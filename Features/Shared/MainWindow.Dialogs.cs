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
            var isTerminal = isBuiltInTerminal || isExternalTerminal;
            var builtInActionComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new ComboBoxItem { Content = "一般 Command", Tag = "command" },
                    new ComboBoxItem { Content = "Node.js Docker 部署", Tag = "node-docker" },
                    new ComboBoxItem { Content = "終端機", Tag = "terminal" },
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

            var iconGlyphTextBox = new TextBox
            {
                Text = existingItem?.IconGlyph ?? ToolbarCommandItem.DefaultGlyph,
                PlaceholderText = "輸入 glyph，例如 ⚙ 或從字元對應表貼上",
            };

            var iconPathTextBox = new TextBox
            {
                Text = existingItem?.IconPath ?? string.Empty,
                PlaceholderText = "本機 icon 路徑，例如 C:\\Soft\\icons\\vscode.png",
            };

            var previewGlyph = new FontIcon
            {
                Glyph = string.IsNullOrWhiteSpace(iconGlyphTextBox.Text) ? ToolbarCommandItem.DefaultGlyph : iconGlyphTextBox.Text,
                FontSize = 24,
                Foreground = new SolidColorBrush(GetBrushColor("TextPrimaryBrush", "#F6F2FF")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
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
                await UpdateToolbarIconVisualAsync(
                    previewImage,
                    previewGlyph,
                    iconPathTextBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(iconGlyphTextBox.Text)
                        ? ToolbarCommandItem.DefaultGlyph
                        : iconGlyphTextBox.Text.Trim());
            }

            iconGlyphTextBox.TextChanged += (_, _) => RefreshPreview();
            iconPathTextBox.TextChanged += (_, _) => RefreshPreview();

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

            SelectComboBoxItemByTag(nodeDockerLaunchModeComboBox, existingItem?.NodeDockerLaunchMode ?? NodeDockerLaunchMode.ExternalWindow);
            SelectComboBoxItemByTag(nodeDockerBuiltInShellComboBox, existingItem?.TerminalShellKind ?? _shortcutSettings.DefaultTerminalShellKind);
            SelectComboBoxItemByTag(terminalShellComboBox, existingItem?.TerminalShellKind ?? _shortcutSettings.DefaultTerminalShellKind);
            SelectComboBoxItemByTag(terminalWorkingDirectoryModeComboBox, existingItem?.TerminalWorkingDirectoryMode ?? _shortcutSettings.DefaultTerminalWorkingDirectoryMode);
            SelectComboBoxItemByTag(terminalTargetComboBox, isBuiltInTerminal ? OpenBuiltInTerminalCommand : OpenExternalTerminalCommand);
            SelectComboBoxItemByTag(
                builtInActionComboBox,
                isNodeDockerDeploy
                    ? "node-docker"
                    : isTerminal
                        ? "terminal"
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
            terminalSettingsPanel.Children.Add(new TextBlock { Text = "終端機入口" });
            terminalSettingsPanel.Children.Add(terminalTargetComboBox);
            terminalSettingsPanel.Children.Add(new TextBlock { Text = "預設 shell" });
            terminalSettingsPanel.Children.Add(terminalShellComboBox);
            terminalSettingsPanel.Children.Add(new TextBlock { Text = "工作目錄" });
            terminalSettingsPanel.Children.Add(terminalWorkingDirectoryModeComboBox);
            terminalSettingsPanel.Children.Add(new TextBlock { Text = "自訂工作目錄" });
            terminalSettingsPanel.Children.Add(terminalCustomWorkingDirectoryTextBox);
            terminalSettingsPanel.Children.Add(new TextBlock { Text = "啟動參數" });
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
                Text = "這個模式會使用終端機內建流程，不需要另外填 command。可切換成外部 Windows Terminal 或 Nuone Tools 內建終端機。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Visibility = isTerminal ? Visibility.Visible : Visibility.Collapsed,
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

            void RefreshCommandMode()
            {
                var selectedMode = builtInActionComboBox.SelectedItem is ComboBoxItem { Tag: string modeTag }
                    ? modeTag
                    : "command";
                var deployMode = string.Equals(selectedMode, "node-docker", StringComparison.Ordinal);
                var terminalMode = string.Equals(selectedMode, "terminal", StringComparison.Ordinal);
                var fileBunkerUploadMode = string.Equals(selectedMode, "filebunker-upload", StringComparison.Ordinal);
                var storageUploadMode = string.Equals(selectedMode, "storage-upload", StringComparison.Ordinal);
                var enhancePdfMode = string.Equals(selectedMode, "enhance-pdf", StringComparison.Ordinal);

                nodeDockerSettingsPanel.Visibility = deployMode ? Visibility.Visible : Visibility.Collapsed;
                terminalSettingsPanel.Visibility = terminalMode ? Visibility.Visible : Visibility.Collapsed;
                commandPanel.Visibility = deployMode || terminalMode || fileBunkerUploadMode || storageUploadMode || enhancePdfMode ? Visibility.Collapsed : Visibility.Visible;
                builtInCommandHint.Visibility = deployMode ? Visibility.Visible : Visibility.Collapsed;
                builtInTerminalHint.Visibility = terminalMode ? Visibility.Visible : Visibility.Collapsed;
                fileBunkerUploadHint.Visibility = fileBunkerUploadMode ? Visibility.Visible : Visibility.Collapsed;
                storageUploadHint.Visibility = storageUploadMode ? Visibility.Visible : Visibility.Collapsed;
                enhancePdfHint.Visibility = enhancePdfMode ? Visibility.Visible : Visibility.Collapsed;
                commandTextBox.IsReadOnly = deployMode || terminalMode || fileBunkerUploadMode || storageUploadMode || enhancePdfMode;
                terminalCustomWorkingDirectoryTextBox.Visibility =
                    terminalWorkingDirectoryModeComboBox.SelectedItem is ComboBoxItem { Tag: ToolbarWorkingDirectoryMode.CustomPath }
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

                    if (string.IsNullOrWhiteSpace(iconGlyphTextBox.Text) ||
                        string.Equals(iconGlyphTextBox.Text, ToolbarCommandItem.DefaultGlyph, StringComparison.Ordinal))
                    {
                        iconGlyphTextBox.Text = "\uE7B8";
                    }

                    return;
                }

                if (terminalMode)
                {
                    commandTextBox.Text = terminalTargetComboBox.SelectedItem is ComboBoxItem { Tag: string terminalCommand }
                        ? terminalCommand
                        : OpenExternalTerminalCommand;
                    if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                    {
                        titleTextBox.Text = "terminal";
                    }

                    if (string.IsNullOrWhiteSpace(iconGlyphTextBox.Text) ||
                        string.Equals(iconGlyphTextBox.Text, ToolbarCommandItem.DefaultGlyph, StringComparison.Ordinal))
                    {
                        iconGlyphTextBox.Text = "\uE756";
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

                    if (string.IsNullOrWhiteSpace(iconGlyphTextBox.Text) ||
                        string.Equals(iconGlyphTextBox.Text, ToolbarCommandItem.DefaultGlyph, StringComparison.Ordinal))
                    {
                        iconGlyphTextBox.Text = "\uE898";
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

                    if (string.IsNullOrWhiteSpace(iconGlyphTextBox.Text) ||
                        string.Equals(iconGlyphTextBox.Text, ToolbarCommandItem.DefaultGlyph, StringComparison.Ordinal))
                    {
                        iconGlyphTextBox.Text = "\uE898";
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

                    if (string.IsNullOrWhiteSpace(iconGlyphTextBox.Text) ||
                        string.Equals(iconGlyphTextBox.Text, ToolbarCommandItem.DefaultGlyph, StringComparison.Ordinal))
                    {
                        iconGlyphTextBox.Text = "\uEA90";
                    }
                }
            }

            builtInActionComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            nodeDockerLaunchModeComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            terminalTargetComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            terminalWorkingDirectoryModeComboBox.SelectionChanged += (_, _) => RefreshCommandMode();
            RefreshCommandMode();

            var panel = new StackPanel { Spacing = 14 };
            panel.Children.Add(new TextBlock
            {
                Text = "設定工具列按鈕的名稱、圖示與 command。一般 command 會在目前作用中的 pane 路徑執行；終端機、部署與 FileBunker 上傳模式可直接走內建流程。",
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
            panel.Children.Add(fileBunkerUploadHint);
            panel.Children.Add(storageUploadHint);
            panel.Children.Add(enhancePdfHint);
            panel.Children.Add(new TextBlock { Text = "Icon 路徑" });
            panel.Children.Add(iconPathRow);
            panel.Children.Add(new TextBlock { Text = "Glyph 後備圖示" });
            panel.Children.Add(iconGlyphTextBox);
            panel.Children.Add(new TextBlock
            {
                Text = "優先使用本機 png / ico / svg，或直接指定 exe / dll / lnk 路徑來抓它的程式 icon。若未填寫或路徑無法讀取，才會改用 glyph。glyph 可貼單一字元，例如 Segoe Fluent Icons、Segoe MDL2 或 emoji。",
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
            var command = string.Equals(selectedMode, "node-docker", StringComparison.Ordinal)
                ? DeployNodeDockerCommand
                : string.Equals(selectedMode, "terminal", StringComparison.Ordinal)
                    ? terminalTargetComboBox.SelectedItem is ComboBoxItem { Tag: string terminalCommand }
                        ? terminalCommand
                        : OpenExternalTerminalCommand
                : string.Equals(selectedMode, "filebunker-upload", StringComparison.Ordinal)
                    ? FileBunkerUploadCommand
                : string.Equals(selectedMode, "storage-upload", StringComparison.Ordinal)
                    ? StorageUploadCommand
                : string.Equals(selectedMode, "enhance-pdf", StringComparison.Ordinal)
                    ? EnhancePdfCommand
                    : commandTextBox.Text.Trim();
            var iconPath = iconPathTextBox.Text.Trim();
            var iconGlyph = iconGlyphTextBox.Text.Trim();
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
            filePanel.Children.Add(new TextBlock
            {
                Text = $"Log 會統一寫入全域 logging.LogDirectoryPath：{CurrentLogDirectoryPath}",
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
            panel.Children.Add(filePanel);
            panel.Children.Add(mongoPanel);
            panel.Children.Add(new TextBlock { Text = "排程類型" });
            panel.Children.Add(scheduleTypeComboBox);
            panel.Children.Add(intervalPanel);
            panel.Children.Add(scheduledPanel);
            panel.Children.Add(runMissedCheckBox);
            panel.Children.Add(new TextBlock
            {
                Text = "排程只會在 app 開著時生效；如果勾這個，超過排定時間後才開 app 也會立刻補跑。",
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
            profile.LogDirectoryPath = NormalizeLogDirectoryPath(_loggingSettings.LogDirectoryPath);
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
                MinHeight = 160,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            passwordsTextBox.Text = string.Join(Environment.NewLine, passwordValues);

            var panel = new StackPanel { Spacing = 10, Width = 680 };
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
            panel.Children.Add(new ScrollViewer
            {
                Content = passwordsTextBox,
                MaxHeight = 240,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
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
