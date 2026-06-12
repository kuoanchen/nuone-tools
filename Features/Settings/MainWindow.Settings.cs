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
        private void ResetEditableShortcutSettings()
        {
            _isUpdatingSettingsUi = true;
            _settingsCaptureTarget = ShortcutCaptureTarget.None;
            _editingShortcutSettings = CloneShortcutSettings(_shortcutSettings);
            SyncShortcutText();
            ShowHiddenSystemItemsToggle.IsOn = _editingShortcutSettings.ShowHiddenSystemItems;
            ShowSelectedFileSizeToggle.IsOn = _editingShortcutSettings.ShowSelectedFileSize;
            ShowSelectedFolderSizeToggle.IsOn = _editingShortcutSettings.ShowSelectedFolderSize;
            SyncThemeModeSelection();
            CaptureHintTextBlock.Text = "按「修改」後，再按下實體鍵，會立即套用。";
            _isUpdatingSettingsUi = false;
        }

        private void SyncThemeModeSelection()
        {
            if (ThemeModeComboBox is null)
            {
                return;
            }

            foreach (var item in ThemeModeComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag
                    && string.Equals(tag, _editingShortcutSettings.ThemeMode.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    ThemeModeComboBox.SelectedItem = item;
                    return;
                }
            }

            ThemeModeComboBox.SelectedIndex = 0;
        }

        private void ShowGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.General);
        }

        private void ShowAppearanceSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Appearance);
        }

        private void ShowAccountSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Account);
        }

        private void ShowShortcutSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Shortcuts);
        }

        private void ShowToolbarSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Toolbar);
        }

        private void ShowAutoExtractSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.AutoExtract);
        }

        private async void AddToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            var item = await ShowToolbarCommandEditorAsync(null);
            if (item is null)
            {
                return;
            }

            ToolbarCommands.Add(item);
            SaveToolbarCommandsSafe();
        }

        private async void EditToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ToolbarCommandItem item })
            {
                return;
            }

            var editedItem = await ShowToolbarCommandEditorAsync(item);
            if (editedItem is null)
            {
                return;
            }

            item.Title = editedItem.Title;
            item.Command = editedItem.Command;
            item.IconPath = editedItem.IconPath;
            item.IconGlyph = editedItem.IconGlyph;
            SaveToolbarCommandsSafe();
        }

        private async void DeleteToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ToolbarCommandItem item })
            {
                return;
            }

            var confirmed = await ConfirmAsync("刪除工具列按鈕", $"確定要刪除「{item.Title}」嗎？");
            if (!confirmed)
            {
                return;
            }

            ToolbarCommands.Remove(item);
            SaveToolbarCommandsSafe();
        }

        private void ToolbarCommandsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            SyncToolbarCommandsOrder(sender);
            SaveToolbarCommandsSafe();
        }

        private async void ToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ToolbarCommandItem item })
            {
                return;
            }

            await ExecuteToolbarCommandAsync(item);
        }

        private async void TopToolbarListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not ToolbarCommandItem item)
            {
                return;
            }

            await ExecuteToolbarCommandAsync(item);
        }

        private void ToolbarItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(GetBrushColor("InputAltBrush", "#231E2B"));
                border.BorderBrush = new SolidColorBrush(GetBrushColor("PanelStrokeBrush", "#3A3146"));
            }
        }

        private void ToolbarItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
                border.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
        }

        private async void ToolbarIconPresenter_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid { Tag: ToolbarCommandItem item } presenter ||
                presenter.Children.Count < 2 ||
                presenter.Children[0] is not Image image ||
                presenter.Children[1] is not FontIcon fontIcon)
            {
                return;
            }

            await UpdateToolbarIconVisualAsync(image, fontIcon, item.IconPath, item.DisplayGlyph);
        }

        private void ToolbarIconSummary_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBlock { Tag: ToolbarCommandItem item } textBlock)
            {
                return;
            }

            textBlock.Text = string.IsNullOrWhiteSpace(item.IconPath)
                ? $"Glyph：{item.DisplayGlyph}"
                : $"圖示檔案：{item.IconPath}";
        }

        private static async Task UpdateToolbarIconVisualAsync(Image image, FontIcon fontIcon, string? iconPath, string glyph)
        {
            var imageSource = ToolbarCommandItem.CreateIconImageSource(iconPath);
            if (imageSource is null && ToolbarCommandItem.IsExecutableIconSource(iconPath))
            {
                imageSource = await ToolbarCommandItem.CreateShellIconImageSourceAsync(iconPath);
            }

            if (imageSource is not null)
            {
                image.Source = imageSource;
                image.Visibility = Visibility.Visible;
                fontIcon.Visibility = Visibility.Collapsed;
                return;
            }

            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            fontIcon.Visibility = Visibility.Visible;
            fontIcon.Glyph = glyph;
        }

        private void SyncToolbarCommandsOrder(ListViewBase sender)
        {
            var orderedItems = sender.Items
                .OfType<ToolbarCommandItem>()
                .ToList();

            if (orderedItems.Count == 0 || orderedItems.Count != ToolbarCommands.Count)
            {
                return;
            }

            var currentOrder = ToolbarCommands.ToList();
            if (currentOrder.SequenceEqual(orderedItems))
            {
                return;
            }

            ToolbarCommands.Clear();
            foreach (var orderedItem in orderedItems)
            {
                ToolbarCommands.Add(orderedItem);
            }
        }

        private void SwitchToSettingsSection(SettingsSection section)
        {
            _activeSettingsSection = section;
            UpdateSettingsSectionVisuals();
            UpdateSharedStatusBar();
        }

        private void UpdateSettingsSectionVisuals()
        {
            var isGeneral = _activeSettingsSection == SettingsSection.General;
            var isAccount = _activeSettingsSection == SettingsSection.Account;
            var isAppearance = _activeSettingsSection == SettingsSection.Appearance;
            var isShortcuts = _activeSettingsSection == SettingsSection.Shortcuts;
            var isToolbar = _activeSettingsSection == SettingsSection.Toolbar;
            var isAutoExtract = _activeSettingsSection == SettingsSection.AutoExtract;

            GeneralSettingsContent.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
            AccountSettingsContent.Visibility = isAccount ? Visibility.Visible : Visibility.Collapsed;
            AppearanceSettingsContent.Visibility = isAppearance ? Visibility.Visible : Visibility.Collapsed;
            ShortcutSettingsContent.Visibility = isShortcuts ? Visibility.Visible : Visibility.Collapsed;
            ToolbarSettingsContent.Visibility = isToolbar ? Visibility.Visible : Visibility.Collapsed;
            AutoExtractSettingsContent.Visibility = isAutoExtract ? Visibility.Visible : Visibility.Collapsed;

            ApplySettingsNavState(GeneralSettingsNavBorder, GeneralSettingsNavText, isGeneral);
            ApplySettingsNavState(AccountSettingsNavBorder, AccountSettingsNavText, isAccount);
            ApplySettingsNavState(AppearanceSettingsNavBorder, AppearanceSettingsNavText, isAppearance);
            ApplySettingsNavState(ShortcutSettingsNavBorder, ShortcutSettingsNavText, isShortcuts);
            ApplySettingsNavState(ToolbarSettingsNavBorder, ToolbarSettingsNavText, isToolbar);
            ApplySettingsNavState(AutoExtractSettingsNavBorder, AutoExtractSettingsNavText, isAutoExtract);

            SettingsPageTitle.Text = _activeSettingsSection switch
            {
                SettingsSection.Account => "帳號",
                SettingsSection.Appearance => "外觀",
                SettingsSection.Shortcuts => "快捷鍵",
                SettingsSection.Toolbar => "工具列",
                SettingsSection.AutoExtract => "自動解壓",
                _ => "一般",
            };

            SettingsPageDescription.Text = _activeSettingsSection switch
            {
                SettingsSection.Account => "連接 Nuone 後端帳號。登入成功後會把 API、email 與 token 狀態儲存到本機 config。",
                SettingsSection.Appearance => "調整 Nuone Tools 的視覺偏好。變更後會立即儲存到本機 config。",
                SettingsSection.Shortcuts => "設定常用鍵盤快捷鍵。變更後會立即儲存到本機 config。",
                SettingsSection.Toolbar => "管理上方工具列按鈕。變更後會立即儲存到本機 config。",
                SettingsSection.AutoExtract => "監看指定目錄中的壓縮檔，依密碼清單自動嘗試解壓。變更後會立即儲存到本機 config。",
                _ => "設定檔案管理和常用鍵盤快捷鍵。變更後會立即儲存到本機 config。",
            };
        }

        private void RootLayout_ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (_shortcutSettings.ThemeMode == AppThemeMode.System)
            {
                ApplyThemePalette(GetEffectiveTheme());
            }
        }

        private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSettingsUi || ThemeModeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            {
                return;
            }

            if (!Enum.TryParse<AppThemeMode>(tag, ignoreCase: true, out var themeMode))
            {
                return;
            }

            _editingShortcutSettings.ThemeMode = themeMode;
            _shortcutSettings.ThemeMode = themeMode;
            ApplyThemePreference();
            SaveShortcutSettingsSafe();
        }

        private void ApplyThemePreference()
        {
            if (RootLayout is null)
            {
                return;
            }

            RootLayout.RequestedTheme = _shortcutSettings.ThemeMode switch
            {
                AppThemeMode.Dark => ElementTheme.Dark,
                AppThemeMode.Light => ElementTheme.Light,
                _ => ElementTheme.Default,
            };

            ApplyThemePalette(GetEffectiveTheme());
        }

        private ElementTheme GetEffectiveTheme()
        {
            return _shortcutSettings.ThemeMode switch
            {
                AppThemeMode.Dark => ElementTheme.Dark,
                AppThemeMode.Light => ElementTheme.Light,
                _ when RootLayout.ActualTheme == ElementTheme.Light => ElementTheme.Light,
                _ => ElementTheme.Dark,
            };
        }

        private void ApplyThemePalette(ElementTheme theme)
        {
            var isLight = theme == ElementTheme.Light;

            SetBrushColor("AppBackgroundBrush", isLight ? "#F5F2FA" : "#141119");
            SetBrushColor("PanelBrush", isLight ? "#FFFFFF" : "#211C28");
            SetBrushColor("PanelAltBrush", isLight ? "#F0EBF7" : "#191520");
            SetBrushColor("PanelStrokeBrush", isLight ? "#D8CCE8" : "#3A3146");
            SetBrushColor("AppRailBrush", isLight ? "#ECE6F4" : "#0F2025");
            SetBrushColor("AppRailBorderBrush", isLight ? "#D4CADE" : "#1F3138");
            SetBrushColor("AppRailActiveBrush", isLight ? "#FFFFFF" : "#2D2835");
            SetBrushColor("AppRailActiveBorderBrush", isLight ? "#CDBCE4" : "#4A3E58");
            SetBrushColor("AppRailFooterBrush", isLight ? "#F5F0FB" : "#171622");
            SetBrushColor("AppRailFooterBorderBrush", isLight ? "#D8CCE8" : "#2F2740");
            SetBrushColor("CardBrush", isLight ? "#F7F3FB" : "#2A252F");
            SetBrushColor("InputBrush", isLight ? "#FFFFFF" : "#1B1621");
            SetBrushColor("InputAltBrush", isLight ? "#FFFFFF" : "#231E2B");
            SetBrushColor("AccentBrush", isLight ? "#A53AF4" : "#BF4CFF");
            SetBrushColor("AccentSoftBrush", isLight ? "#8B35C4" : "#8B35C4");
            SetBrushColor("TextPrimaryBrush", isLight ? "#1E1528" : "#F6F2FF");
            SetBrushColor("TextSecondaryBrush", isLight ? "#6E617F" : "#B9AECF");
            SetBrushColor("SuccessBrush", "#74D89B");
            SetBrushColor("WarningBrush", "#D09A00");

            if (RootLayout.Resources.TryGetValue("AppBackgroundBrush", out var appBackgroundResource)
                && appBackgroundResource is SolidColorBrush appBackgroundBrush)
            {
                RootLayout.Background = appBackgroundBrush;
            }

            SelectedItemBackgroundBrush.Color = ParseColor(isLight ? "#E7CCFF" : "#5B147E");
            SelectedItemBorderBrush.Color = ParseColor(isLight ? "#B24DF1" : "#8C3CBC");
            UnselectedItemBackgroundBrush.Color = Colors.Transparent;
            UnselectedItemBorderBrush.Color = Colors.Transparent;

            UpdateActivePaneVisuals();
            UpdateAppSectionVisuals();
            UpdateSettingsSectionVisuals();
        }

        private void SetBrushColor(string resourceKey, string colorHex)
        {
            if (RootLayout.Resources.TryGetValue(resourceKey, out var resource) && resource is SolidColorBrush brush)
            {
                brush.Color = ParseColor(colorHex);
            }
        }

        private void ApplyThemeToDialog(ContentDialog dialog)
        {
            dialog.RequestedTheme = GetEffectiveTheme() switch
            {
                ElementTheme.Light => ElementTheme.Light,
                _ => ElementTheme.Dark,
            };
        }

        private Windows.UI.Color GetBrushColor(string resourceKey, string fallbackHex)
        {
            if (RootLayout.Resources.TryGetValue(resourceKey, out var resource) && resource is SolidColorBrush brush)
            {
                return brush.Color;
            }

            return ParseColor(fallbackHex);
        }

        private static Windows.UI.Color ParseColor(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return Colors.Transparent;
            }

            if (colorHex.Length == 7)
            {
                return ColorHelper.FromArgb(
                    255,
                    Convert.ToByte(colorHex[1..3], 16),
                    Convert.ToByte(colorHex[3..5], 16),
                    Convert.ToByte(colorHex[5..7], 16));
            }

            return ColorHelper.FromArgb(
                Convert.ToByte(colorHex[1..3], 16),
                Convert.ToByte(colorHex[3..5], 16),
                Convert.ToByte(colorHex[5..7], 16),
                Convert.ToByte(colorHex[7..9], 16));
        }

        private void ApplySettingsNavState(Border border, TextBlock textBlock, bool isSelected)
        {
            var selectedBackground = GetBrushColor("CardBrush", "#2A252F");
            var selectedBorder = GetBrushColor("PanelStrokeBrush", "#3A3146");
            var transparent = ColorHelper.FromArgb(0, 0, 0, 0);
            var primaryText = GetBrushColor("TextPrimaryBrush", "#F6F2FF");
            var secondaryText = GetBrushColor("TextSecondaryBrush", "#B9AECF");

            border.Background = new SolidColorBrush(isSelected ? selectedBackground : transparent);
            border.BorderBrush = new SolidColorBrush(isSelected ? selectedBorder : transparent);
            textBlock.Foreground = new SolidColorBrush(isSelected ? primaryText : secondaryText);
            textBlock.FontWeight = isSelected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        private void EditCopyShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.CopyToOtherPane, CopyShortcutTextBox, "複製到另一個 Pane");
        }

        private void EditMoveShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.MoveToOtherPane, MoveShortcutTextBox, "移動到另一個 Pane");
        }

        private void EditNavigateUpShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.NavigateUp, NavigateUpShortcutTextBox, "上一層");
        }

        private void EditCreateFolderShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.CreateFolder, CreateFolderShortcutTextBox, "新增資料夾");
        }

        private void EditDeleteShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.Delete, DeleteShortcutTextBox, "刪除");
        }

        private void BeginShortcutCapture(ShortcutCaptureTarget target, TextBox textBox, string label)
        {
            _settingsCaptureTarget = target;
            textBox.Text = "請按任意鍵...";
            CaptureHintTextBlock.Text = $"正在擷取「{label}」快捷鍵...";
            _ = RootLayout.Focus(FocusState.Programmatic);
        }

        private void ShowHiddenSystemItemsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSettingsUi || ShowHiddenSystemItemsToggle is null)
            {
                return;
            }

            _editingShortcutSettings.ShowHiddenSystemItems = ShowHiddenSystemItemsToggle.IsOn;
            _shortcutSettings.ShowHiddenSystemItems = ShowHiddenSystemItemsToggle.IsOn;
            SaveShortcutSettingsSafe();
            ApplySettingsToPanes();
            RefreshPane(LeftPane);
            RefreshPane(RightPane);
            CaptureHintTextBlock.Text = "已立即儲存顯示設定。";
        }

        private void ShowSelectedFileSizeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSettingsUi || ShowSelectedFileSizeToggle is null)
            {
                return;
            }

            _editingShortcutSettings.ShowSelectedFileSize = ShowSelectedFileSizeToggle.IsOn;
            _shortcutSettings.ShowSelectedFileSize = ShowSelectedFileSizeToggle.IsOn;
            SaveShortcutSettingsSafe();
            RefreshSelectionSizeDisplays();
            CaptureHintTextBlock.Text = "已立即儲存檔案大小顯示設定。";
        }

        private void ShowSelectedFolderSizeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSettingsUi || ShowSelectedFolderSizeToggle is null)
            {
                return;
            }

            _editingShortcutSettings.ShowSelectedFolderSize = ShowSelectedFolderSizeToggle.IsOn;
            _shortcutSettings.ShowSelectedFolderSize = ShowSelectedFolderSizeToggle.IsOn;
            SaveShortcutSettingsSafe();
            RefreshSelectionSizeDisplays();
            CaptureHintTextBlock.Text = "已立即儲存資料夾大小顯示設定。";
        }

        private void AccountApiUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingAccountUi || AccountApiUrlTextBox is null)
            {
                return;
            }

            _accountSettings.ApiBaseUrl = AccountApiUrlTextBox.Text.Trim();
            SaveShortcutSettingsSafe();
        }

        private void AccountEmailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingAccountUi || AccountEmailTextBox is null)
            {
                return;
            }

            _accountSettings.Email = AccountEmailTextBox.Text.Trim();
            SaveShortcutSettingsSafe();
        }

        private async void LoginAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAccountLoginRunning)
            {
                return;
            }

            var apiBaseUrl = NormalizeApiBaseUrl(AccountApiUrlTextBox?.Text);
            var email = AccountEmailTextBox?.Text.Trim() ?? string.Empty;
            var password = AccountPasswordBox?.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                await ShowMessageAsync("帳號登入失敗", "API 位址不能為空。");
                return;
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await ShowMessageAsync("帳號登入失敗", "請輸入 email 與 password。");
                return;
            }

            _isAccountLoginRunning = true;
            _accountSettings.ApiBaseUrl = apiBaseUrl;
            _accountSettings.Email = email;
            _accountSettings.LastStatusText = "登入中...";
            UpdateAccountSettingsUi();
            SaveShortcutSettingsSafe();

            var backgroundWorkId = BeginBackgroundWork("帳號登入中");

            try
            {
                var result = await LoginToIdentityAsync(apiBaseUrl, email, password);

                _accountSettings.Token = result.Token;
                _accountSettings.UserDisplayName = result.DisplayName;
                _accountSettings.ServiceAccountsSummary = result.ServiceAccountsSummary;
                _accountSettings.PayloadJson = result.PayloadJson;
                _accountSettings.ServiceAccountsJson = result.ServiceAccountsJson;
                _accountSettings.LastLoginText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _accountSettings.LastStatusText = "登入成功";
                if (AccountPasswordBox is not null)
                {
                    AccountPasswordBox.Password = string.Empty;
                }

                SaveShortcutSettingsSafe();
                UpdateAccountSettingsUi();
                UpdateSharedStatusBar();
            }
            catch (Exception ex)
            {
                _accountSettings.Token = string.Empty;
                _accountSettings.UserDisplayName = string.Empty;
                _accountSettings.ServiceAccountsSummary = string.Empty;
                _accountSettings.PayloadJson = string.Empty;
                _accountSettings.ServiceAccountsJson = string.Empty;
                _accountSettings.LastStatusText = $"登入失敗：{ex.Message}";
                SaveShortcutSettingsSafe();
                UpdateAccountSettingsUi();
                UpdateSharedStatusBar();
                await ShowMessageAsync("帳號登入失敗", ex.Message);
            }
            finally
            {
                _isAccountLoginRunning = false;
                UpdateAccountSettingsUi();
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        private void ClearAccountSessionButton_Click(object sender, RoutedEventArgs e)
        {
            _accountSettings.Token = string.Empty;
            _accountSettings.UserDisplayName = string.Empty;
            _accountSettings.ServiceAccountsSummary = string.Empty;
            _accountSettings.PayloadJson = string.Empty;
            _accountSettings.ServiceAccountsJson = string.Empty;
            _accountSettings.LastStatusText = "已清除本機登入狀態";
            if (AccountPasswordBox is not null)
            {
                AccountPasswordBox.Password = string.Empty;
            }

            SaveShortcutSettingsSafe();
            UpdateAccountSettingsUi();
            UpdateSharedStatusBar();
        }

        private void SaveSettingsPage_Click(object sender, RoutedEventArgs e)
        {
            if (HasDuplicateShortcut(_editingShortcutSettings))
            {
                CaptureHintTextBlock.Text = "單鍵快捷鍵不能使用相同按鍵。";
                return;
            }

            _editingShortcutSettings.ShowHiddenSystemItems = ShowHiddenSystemItemsToggle.IsOn;
            _shortcutSettings = CloneShortcutSettings(_editingShortcutSettings);
            SaveShortcutSettingsSafe();
            ApplySettingsToPanes();
            RefreshPane(LeftPane);
            RefreshPane(RightPane);
            CaptureHintTextBlock.Text = "已儲存快捷鍵設定。";
            SwitchToAppSection(AppSection.FileManager);
        }

        private void CancelSettingsPage_Click(object sender, RoutedEventArgs e)
        {
            ResetEditableShortcutSettings();
            SwitchToAppSection(AppSection.FileManager);
        }

        private void CustomGroupsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            SaveCustomGroupsSafe();
        }

        private void GroupedPathsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            SaveCustomGroupsSafe();
        }

        private static bool HasDuplicateShortcut(ShortcutSettings settings)
        {
            var keys = new[]
            {
                settings.CopyToOtherPaneKey,
                settings.MoveToOtherPaneKey,
                settings.NavigateUpKey,
                settings.DeleteKey,
            };

            return keys.Distinct().Count() != keys.Length;
        }

        private void SyncShortcutText()
        {
            CopyShortcutTextBox.Text = FormatShortcutKey(_editingShortcutSettings.CopyToOtherPaneKey);
            MoveShortcutTextBox.Text = FormatShortcutKey(_editingShortcutSettings.MoveToOtherPaneKey);
            NavigateUpShortcutTextBox.Text = FormatShortcutKey(_editingShortcutSettings.NavigateUpKey);
            CreateFolderShortcutTextBox.Text = FormatCreateFolderShortcutKey(_editingShortcutSettings.CreateFolderKey);
            DeleteShortcutTextBox.Text = FormatShortcutKey(_editingShortcutSettings.DeleteKey);
        }

        private void ApplySettingsToPanes()
        {
            LeftPane.ShowHiddenSystemItems = _shortcutSettings.ShowHiddenSystemItems;
            RightPane.ShowHiddenSystemItems = _shortcutSettings.ShowHiddenSystemItems;
        }

        private static ShortcutSettings CloneShortcutSettings(ShortcutSettings source)
        {
            return new ShortcutSettings
            {
                CopyToOtherPaneKey = source.CopyToOtherPaneKey,
                MoveToOtherPaneKey = source.MoveToOtherPaneKey,
                NavigateUpKey = source.NavigateUpKey,
                CreateFolderKey = source.CreateFolderKey,
                DeleteKey = source.DeleteKey,
                ThemeMode = source.ThemeMode,
                ShowSelectedFileSize = source.ShowSelectedFileSize,
                ShowSelectedFolderSize = source.ShowSelectedFolderSize,
                ShowHiddenSystemItems = source.ShowHiddenSystemItems,
            };
        }
    }
}
