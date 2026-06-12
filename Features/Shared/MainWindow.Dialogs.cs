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

            var panel = new StackPanel { Spacing = 14 };
            panel.Children.Add(new TextBlock
            {
                Text = "設定工具列按鈕的名稱、圖示與 command。按下後會在目前作用中的 pane 路徑執行。",
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(new TextBlock { Text = "名稱" });
            panel.Children.Add(titleTextBox);
            panel.Children.Add(new TextBlock { Text = "Command" });
            panel.Children.Add(commandTextBox);
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
                Content = panel,
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
            var command = commandTextBox.Text.Trim();
            var iconPath = iconPathTextBox.Text.Trim();
            var iconGlyph = iconGlyphTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(command))
            {
                await ShowMessageAsync("工具列按鈕無效", "名稱與 command 都必須填寫。");
                return null;
            }

            return new ToolbarCommandItem
            {
                Id = existingItem?.Id ?? Guid.NewGuid(),
                Title = title,
                Command = command,
                IconPath = iconPath,
                IconGlyph = iconGlyph,
            };
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
