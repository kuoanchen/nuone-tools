using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualBasic.FileIO;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace nuone_tools
{
    public sealed partial class MainWindow : Window
    {
        private static readonly SolidColorBrush SelectedItemBackgroundBrush = new(ColorHelper.FromArgb(255, 91, 20, 126));
        private static readonly SolidColorBrush SelectedItemBorderBrush = new(ColorHelper.FromArgb(255, 140, 60, 188));
        private static readonly SolidColorBrush UnselectedItemBackgroundBrush = new(Colors.Transparent);
        private static readonly SolidColorBrush UnselectedItemBorderBrush = new(Colors.Transparent);
        private static readonly Thickness SelectedItemBorderThickness = new(1);
        private static readonly Thickness UnselectedItemBorderThickness = new(0);
        private static readonly string ConfigDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nuone-tools",
            "config");
        private static readonly string LegacyGroupsConfigPath = Path.Combine(ConfigDirectoryPath, "groups.json");
        private static readonly string SettingsConfigPath = Path.Combine(ConfigDirectoryPath, "settings.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly TimeSpan PaneWatcherDebounceInterval = TimeSpan.FromMilliseconds(450);
        private PaneViewModel _activePane;
        private readonly DispatcherQueueTimer _selectionFlyoutTimer;
        private readonly PaneDirectoryWatcher _leftPaneWatcher;
        private readonly PaneDirectoryWatcher _rightPaneWatcher;
        private FrameworkElement? _pendingFlyoutTarget;
        private PaneViewModel? _pendingFlyoutPane;
        private string? _pendingFlyoutPath;
        private ShortcutSettings _shortcutSettings = ShortcutSettings.CreateDefault();
        private ShortcutSettings _editingShortcutSettings = ShortcutSettings.CreateDefault();
        private AppSection _activeSection = AppSection.FileManager;
        private SettingsSection _activeSettingsSection = SettingsSection.General;
        private ShortcutCaptureTarget _settingsCaptureTarget = ShortcutCaptureTarget.None;
        private bool _isSettingsDialogOpen;

        public ObservableCollection<DriveShortcut> Drives { get; } = new();

        public ObservableCollection<PathGroup> CustomGroups { get; } = new();

        public PaneViewModel LeftPane { get; } = new("左側");

        public PaneViewModel RightPane { get; } = new("右側");

        public MainWindow()
        {
            InitializeComponent();

            _activePane = LeftPane;
            _leftPaneWatcher = new PaneDirectoryWatcher(LeftPane, DispatcherQueue, RefreshPane, PaneWatcherDebounceInterval);
            _rightPaneWatcher = new PaneDirectoryWatcher(RightPane, DispatcherQueue, RefreshPane, PaneWatcherDebounceInterval);
            ExtendsContentIntoTitleBar = true;
            TrySetWindowIcon();

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1680, 980));
            ConfigureTitleBarInsets();
            RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootLayout_KeyDown), true);

            SeedSidebar();
            LoadCustomGroups();
            LoadShortcutSettings();
            ApplySettingsToPanes();
            LoadDriveCards();
            ResetEditableShortcutSettings();

            var leftDefault = ResolveInitialLeftPath();
            var rightDefault = ResolveInitialRightPath(leftDefault);

            LeftPane.PropertyChanged += Pane_PropertyChanged;
            RightPane.PropertyChanged += Pane_PropertyChanged;
            Closed += MainWindow_Closed;

            LeftPane.NavigateTo(leftDefault);
            RightPane.NavigateTo(rightDefault);
            UpdateActivePaneVisuals();
            UpdateAppSectionVisuals();
            UpdateSettingsSectionVisuals();

            _selectionFlyoutTimer = DispatcherQueue.CreateTimer();
            _selectionFlyoutTimer.Interval = TimeSpan.FromMilliseconds(225);
            _selectionFlyoutTimer.IsRepeating = false;
            _selectionFlyoutTimer.Tick += SelectionFlyoutTimer_Tick;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            SavePanePathsSafe();
            LeftPane.PropertyChanged -= Pane_PropertyChanged;
            RightPane.PropertyChanged -= Pane_PropertyChanged;
            Closed -= MainWindow_Closed;
            _leftPaneWatcher.Dispose();
            _rightPaneWatcher.Dispose();
        }

        private void Pane_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(PaneViewModel.CurrentPath), StringComparison.Ordinal))
            {
                return;
            }

            if (ReferenceEquals(sender, LeftPane))
            {
                _leftPaneWatcher.Watch(LeftPane.CurrentPath);
                return;
            }

            if (ReferenceEquals(sender, RightPane))
            {
                _rightPaneWatcher.Watch(RightPane.CurrentPath);
            }
        }

        private void TrySetWindowIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (File.Exists(iconPath))
                {
                    AppWindow.SetIcon(iconPath);
                }
            }
            catch
            {
            }
        }

        private void ConfigureTitleBarInsets()
        {
            var titleBar = AppWindow.TitleBar;
            TopCommandBarBorder.Margin = new Thickness(
                Math.Max(titleBar.LeftInset, 12),
                0,
                Math.Max(titleBar.RightInset + 8, 140),
                0);
        }

        private void SeedSidebar()
        {
        }

        private void LoadDriveCards()
        {
            Drives.Clear();

            foreach (var drive in DriveInfo.GetDrives().Where(static d => d.IsReady))
            {
                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var used = Math.Max(total - free, 0);
                var usage = total == 0 ? 0 : Math.Round((double)used / total * 100, 1);

                Drives.Add(new DriveShortcut
                {
                    Name = $"{drive.Name.TrimEnd(Path.DirectorySeparatorChar)}",
                    RootPath = drive.RootDirectory.FullName,
                    Summary = $"{usage:0.#}%",
                    Detail = $"{FormatSize(used)} / {FormatSize(total)}",
                    UsagePercent = usage,
                });
            }
        }

        private string ResolveInitialLeftPath()
        {
            var savedPaths = LoadSavedPanePaths();
            if (!string.IsNullOrWhiteSpace(savedPaths.LeftPath) && Directory.Exists(savedPaths.LeftPath))
            {
                return savedPaths.LeftPath;
            }

            if (Directory.Exists(Environment.CurrentDirectory))
            {
                return Environment.CurrentDirectory;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private string ResolveInitialRightPath(string leftPath)
        {
            var savedPaths = LoadSavedPanePaths();
            if (!string.IsNullOrWhiteSpace(savedPaths.RightPath) && Directory.Exists(savedPaths.RightPath))
            {
                return savedPaths.RightPath;
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(documents) && !PathEquals(documents, leftPath))
            {
                return documents;
            }

            return Path.GetPathRoot(leftPath) ?? leftPath;
        }

        internal static bool PathEquals(string left, string right)
        {
            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizePath(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void OpenInPane(PaneViewModel pane, string path)
        {
            pane.NavigateTo(path);
            ActivatePane(pane);
        }

        private void RefreshPane(PaneViewModel pane)
        {
            pane.Refresh();
            LoadDriveCards();
        }

        private void NavigateUp(PaneViewModel pane)
        {
            pane.NavigateUp();
        }

        private void NavigateBack(PaneViewModel pane)
        {
            pane.GoBack();
        }

        private async Task NavigateToEditablePathAsync(PaneViewModel pane, string? rawPath = null)
        {
            ActivatePane(pane);

            var requestedPath = (rawPath ?? pane.EditablePath)?.Trim();
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                pane.EditablePath = pane.CurrentPath;
                return;
            }

            if (!Directory.Exists(requestedPath))
            {
                pane.EditablePath = pane.CurrentPath;
                await ShowMessageAsync("路徑不存在", requestedPath);
                return;
            }

            OpenInPane(pane, requestedPath);
        }

        private void ActivatePane(PaneViewModel pane)
        {
            _activePane = pane;
            UpdateActivePaneVisuals();
        }

        private void UpdateActivePaneVisuals()
        {
            var isLeftActive = ReferenceEquals(_activePane, LeftPane);

            ApplyPaneVisualState(
                LeftPaneBorder,
                LeftPathTextBox,
                LeftPaneStatusBorder,
                isLeftActive);

            ApplyPaneVisualState(
                RightPaneBorder,
                RightPathTextBox,
                RightPaneStatusBorder,
                !isLeftActive);
        }

        private static void ApplyPaneVisualState(
            Border paneBorder,
            TextBox pathTextBox,
            Border statusBorder,
            bool isActive)
        {
            var activeBorder = ColorHelper.FromArgb(255, 191, 76, 255);
            var inactiveBorder = ColorHelper.FromArgb(255, 58, 49, 70);
            var activeFill = ColorHelper.FromArgb(255, 37, 24, 46);
            var inactiveFill = ColorHelper.FromArgb(255, 27, 22, 33);

            paneBorder.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBorder);
            paneBorder.BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
            pathTextBox.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBorder);
            pathTextBox.Background = new SolidColorBrush(isActive ? activeFill : inactiveFill);
            statusBorder.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBorder);
        }

        private void OpenSelectedInExplorer()
        {
            var selectedEntries = GetSelectedEntries(_activePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            foreach (var entry in selectedEntries)
            {
                OpenPath(entry.FullPath);
            }
        }

        private void RefreshAll_Click(object sender, RoutedEventArgs e)
        {
            RefreshPane(LeftPane);
            RefreshPane(RightPane);
        }

        private void RefreshLeft_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            RefreshPane(LeftPane);
        }

        private void RefreshRight_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
            RefreshPane(RightPane);
        }

        private void NavigateUpLeft_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            NavigateUp(LeftPane);
        }

        private void NavigateUpRight_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
            NavigateUp(RightPane);
        }

        private void BackLeft_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            NavigateBack(LeftPane);
        }

        private void BackRight_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
            NavigateBack(RightPane);
        }

        private async void LeftPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await NavigateToEditablePathAsync(LeftPane, (sender as TextBox)?.Text);
            }
        }

        private async void RightPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await NavigateToEditablePathAsync(RightPane, (sender as TextBox)?.Text);
            }
        }

        private void LeftPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        private void RightPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        private void LeftPaneContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        private void RightPaneContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        private void LeftPaneList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        private void LeftPaneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncPaneSelectionFromListView(LeftPane, LeftPaneListView);
            ApplySelectionVisuals(LeftPaneListView);
        }

        private void RightPaneList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        private void RightPaneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncPaneSelectionFromListView(RightPane, RightPaneListView);
            ApplySelectionVisuals(RightPaneListView);
        }

        private void PaneListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem listViewItem)
            {
                ApplySelectionVisualToContainer(listViewItem);
            }
        }

        private void DriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path })
            {
                OpenInPane(_activePane, path);
            }
        }

        private void ShowFileManagerApp_Click(object sender, RoutedEventArgs e)
        {
            _settingsCaptureTarget = ShortcutCaptureTarget.None;
            SwitchToAppSection(AppSection.FileManager);
        }

        private void ShowSettingsApp_Click(object sender, RoutedEventArgs e)
        {
            ResetEditableShortcutSettings();
            SwitchToSettingsSection(SettingsSection.General);
            SwitchToAppSection(AppSection.Settings);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            ResetEditableShortcutSettings();
            SwitchToSettingsSection(SettingsSection.General);
            SwitchToAppSection(AppSection.Settings);
        }

        private void SwitchToAppSection(AppSection section)
        {
            _activeSection = section;
            UpdateAppSectionVisuals();
        }

        private void UpdateAppSectionVisuals()
        {
            var isFileManager = _activeSection == AppSection.FileManager;
            FileManagerView.Visibility = isFileManager ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = isFileManager ? Visibility.Collapsed : Visibility.Visible;

            ApplyAppRailButtonState(
                FileManagerAppButtonBorder,
                FileManagerAppIcon,
                FileManagerAppText,
                isFileManager);

            ApplyAppRailButtonState(
                SettingsAppButtonBorder,
                SettingsAppIcon,
                SettingsAppText,
                !isFileManager);
        }

        private static void ApplyAppRailButtonState(Border border, FontIcon icon, TextBlock label, bool isActive)
        {
            var activeBackground = ColorHelper.FromArgb(255, 45, 40, 53);
            var activeBorder = ColorHelper.FromArgb(255, 74, 62, 88);
            var inactiveBackground = ColorHelper.FromArgb(0, 0, 0, 0);
            var activeText = ColorHelper.FromArgb(255, 246, 242, 255);
            var inactiveText = ColorHelper.FromArgb(255, 185, 174, 207);

            border.Background = new SolidColorBrush(isActive ? activeBackground : inactiveBackground);
            border.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBackground);
            icon.Foreground = new SolidColorBrush(isActive ? activeText : inactiveText);
            label.Foreground = new SolidColorBrush(isActive ? activeText : inactiveText);
        }

        private void ResetEditableShortcutSettings()
        {
            _settingsCaptureTarget = ShortcutCaptureTarget.None;
            _editingShortcutSettings = CloneShortcutSettings(_shortcutSettings);
            SyncShortcutText();
            ShowHiddenSystemItemsToggle.IsOn = _editingShortcutSettings.ShowHiddenSystemItems;
            CaptureHintTextBlock.Text = "按「修改」後，再按下實體鍵。";
        }

        private void ShowGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.General);
        }

        private void ShowAppearanceSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Appearance);
        }

        private void ShowShortcutSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Shortcuts);
        }

        private void SwitchToSettingsSection(SettingsSection section)
        {
            _activeSettingsSection = section;
            UpdateSettingsSectionVisuals();
        }

        private void UpdateSettingsSectionVisuals()
        {
            var isGeneral = _activeSettingsSection == SettingsSection.General;
            var isAppearance = _activeSettingsSection == SettingsSection.Appearance;
            var isShortcuts = _activeSettingsSection == SettingsSection.Shortcuts;

            GeneralSettingsContent.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
            AppearanceSettingsContent.Visibility = isAppearance ? Visibility.Visible : Visibility.Collapsed;
            ShortcutSettingsContent.Visibility = isShortcuts ? Visibility.Visible : Visibility.Collapsed;

            ApplySettingsNavState(GeneralSettingsNavBorder, GeneralSettingsNavText, isGeneral);
            ApplySettingsNavState(AppearanceSettingsNavBorder, AppearanceSettingsNavText, isAppearance);
            ApplySettingsNavState(ShortcutSettingsNavBorder, ShortcutSettingsNavText, isShortcuts);

            SettingsPageTitle.Text = _activeSettingsSection switch
            {
                SettingsSection.Appearance => "外觀",
                SettingsSection.Shortcuts => "快捷鍵",
                _ => "一般",
            };

            SettingsPageDescription.Text = _activeSettingsSection switch
            {
                SettingsSection.Appearance => "調整 Nuone Tools 的視覺偏好。",
                SettingsSection.Shortcuts => "設定常用鍵盤快捷鍵。變更後會儲存到本機 config。",
                _ => "設定檔案管理和常用鍵盤快捷鍵。變更後會儲存到本機 config。",
            };
        }

        private static void ApplySettingsNavState(Border border, TextBlock textBlock, bool isSelected)
        {
            var selectedBackground = ColorHelper.FromArgb(255, 42, 37, 47);
            var selectedBorder = ColorHelper.FromArgb(255, 58, 49, 70);
            var transparent = ColorHelper.FromArgb(0, 0, 0, 0);
            var primaryText = ColorHelper.FromArgb(255, 246, 242, 255);
            var secondaryText = ColorHelper.FromArgb(255, 185, 174, 207);

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

        private void SaveSettingsPage_Click(object sender, RoutedEventArgs e)
        {
            if (HasDuplicateShortcut(_editingShortcutSettings))
            {
                CaptureHintTextBlock.Text = "四個動作不能使用相同的快捷鍵。";
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
                DeleteKey = source.DeleteKey,
                ShowHiddenSystemItems = source.ShowHiddenSystemItems,
            };
        }

        private void ShowAddGroupEditor_Click(object sender, RoutedEventArgs e)
        {
            AddGroupEditor.Visibility = Visibility.Visible;
            NewGroupNameTextBox.Text = string.Empty;
            _ = NewGroupNameTextBox.Focus(FocusState.Programmatic);
        }

        private void CancelAddGroup_Click(object sender, RoutedEventArgs e)
        {
            HideAddGroupEditor();
        }

        private void ConfirmAddGroup_Click(object sender, RoutedEventArgs e)
        {
            TryAddGroup();
        }

        private void NewGroupNameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                TryAddGroup();
            }
        }

        private void AddCurrentPathToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPathGroup(sender, out var group))
            {
                return;
            }

            if (AddPathToGroup(group, _activePane.CurrentPath))
            {
                SaveCustomGroupsSafe();
            }
        }

        private void GroupedPath_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path })
            {
                OpenInPane(_activePane, path);
            }
        }

        private async void RenameGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPathGroup(sender, out var group))
            {
                return;
            }

            var newName = await PromptForTextAsync("群組改名", "輸入新的群組名稱", group.Title);
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, group.Title, StringComparison.Ordinal))
            {
                return;
            }

            if (CustomGroups.Any(item => !ReferenceEquals(item, group) && string.Equals(item.Title, newName, StringComparison.OrdinalIgnoreCase)))
            {
                await ShowMessageAsync("名稱重複", "已有相同名稱的分組。");
                return;
            }

            group.Title = newName;
            SaveCustomGroupsSafe();
        }

        private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPathGroup(sender, out var group))
            {
                return;
            }

            var confirmed = await ConfirmAsync("刪除分組", $"確定要刪除分組「{group.Title}」嗎？");
            if (!confirmed)
            {
                return;
            }

            CustomGroups.Remove(group);
            SaveCustomGroupsSafe();
        }

        private void GroupedPathOpen_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetGroupedPathItem(sender, out var item))
            {
                OpenInPane(_activePane, item.Path);
            }
        }

        private async void RenameGroupedPathAlias_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetGroupedPathItem(sender, out var item))
            {
                return;
            }

            var newAlias = await PromptForTextAsync("更改別名", "輸入新的顯示名稱", item.Title);
            if (string.IsNullOrWhiteSpace(newAlias) || string.Equals(newAlias, item.Title, StringComparison.Ordinal))
            {
                return;
            }

            item.Title = newAlias;
            SaveCustomGroupsSafe();
        }

        private async void RemoveGroupedPath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetGroupedPathItem(sender, out var item) || item.ParentGroup is null)
            {
                return;
            }

            var confirmed = await ConfirmAsync("移除捷徑", $"確定要從分組移除「{item.Title}」嗎？");
            if (!confirmed)
            {
                return;
            }

            item.ParentGroup.Items.Remove(item);
            SaveCustomGroupsSafe();
        }

        private void LeftPaneList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            HandlePaneDoubleTapped(LeftPane, e);
        }

        private void RightPaneList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
            HandlePaneDoubleTapped(RightPane, e);
        }

        private void LeftPaneEntry_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            HandleItemTapped(LeftPane, sender as FrameworkElement);
        }

        private void RightPaneEntry_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
            HandleItemTapped(RightPane, sender as FrameworkElement);
        }

        private async void LeftPaneEntry_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            await HandleEntryRightTappedAsync(LeftPane, sender as FrameworkElement, e);
        }

        private async void RightPaneEntry_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
            await HandleEntryRightTappedAsync(RightPane, sender as FrameworkElement, e);
        }

        private void SyncPaneSelectionFromListView(PaneViewModel pane, ListView listView)
        {
            var selectedEntries = listView.SelectedItems.OfType<FileEntry>().ToList();
            pane.UpdateSelection(selectedEntries);
        }

        private IReadOnlyList<FileEntry> GetSelectedEntries(PaneViewModel pane)
        {
            var listView = ReferenceEquals(pane, LeftPane) ? LeftPaneListView : RightPaneListView;
            return listView.SelectedItems.OfType<FileEntry>().ToList();
        }

        private void HandleItemDoubleTapped(PaneViewModel pane, FileEntry? entry)
        {
            if (entry is null)
            {
                return;
            }

            pane.SelectedItem = entry;

            if (entry.IsDirectory)
            {
                OpenInPane(pane, entry.FullPath);
                return;
            }

            OpenSelectedInExplorer();
        }

        private void HandleItemTapped(PaneViewModel pane, FrameworkElement? element)
        {
            var entry = element?.DataContext as FileEntry;
            if (entry is null)
            {
                return;
            }

            var wasSelected = pane.SelectedItem is not null
                && PathEquals(pane.SelectedItem.FullPath, entry.FullPath)
                && pane.SelectedCount == 1;

            if (!wasSelected || element is null)
            {
                CancelPendingFlyout();
                return;
            }

            QueueSelectionFlyout(pane, element, entry.FullPath);
        }

        private async Task HandleEntryRightTappedAsync(PaneViewModel pane, FrameworkElement? element, RightTappedRoutedEventArgs e)
        {
            var entry = element?.DataContext as FileEntry;
            if (entry is null)
            {
                return;
            }

            CancelPendingFlyout();
            SelectEntryForContextMenu(pane, entry);
            e.Handled = true;

            try
            {
                var selectedPaths = GetSelectedEntriesInDisplayOrder(pane)
                    .Select(item => item.FullPath)
                    .ToArray();
                if (selectedPaths.Length == 0)
                {
                    return;
                }

                var position = e.GetPosition(RootLayout);
                ShellContextMenuHost.ShowForPaths(
                    WindowNative.GetWindowHandle(this),
                    RootLayout.XamlRoot.RasterizationScale,
                    position.X,
                    position.Y,
                    pane.CurrentPath,
                    selectedPaths);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("右鍵選單失敗", ex.Message);
            }
        }

        private void SelectEntryForContextMenu(PaneViewModel pane, FileEntry entry)
        {
            var listView = ReferenceEquals(pane, LeftPane) ? LeftPaneListView : RightPaneListView;
            var selectedEntries = listView.SelectedItems.OfType<FileEntry>().ToList();
            var isAlreadySelected = selectedEntries.Any(item => PathEquals(item.FullPath, entry.FullPath));

            if (!isAlreadySelected)
            {
                listView.SelectedItems.Clear();
                listView.SelectedItems.Add(entry);
                SyncPaneSelectionFromListView(pane, listView);
            }

            pane.SelectedItem = entry;
        }

        private void HandlePaneDoubleTapped(PaneViewModel pane, DoubleTappedRoutedEventArgs e)
        {
            CancelPendingFlyout();

            var entry = FindDataContext<FileEntry>(e.OriginalSource as DependencyObject);
            if (entry is not null)
            {
                HandleItemDoubleTapped(pane, entry);
                e.Handled = true;
                return;
            }

            NavigateUp(pane);
            e.Handled = true;
        }

        private void OpenSelected_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedInExplorer();
        }

        private void OpenPath_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetPath(sender, out var path))
            {
                OpenPath(path);
            }
        }

        private void OpenInRightPane_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetPath(sender, out var path))
            {
                OpenInOtherPane(RightPane, path);
            }
        }

        private void OpenInLeftPane_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetPath(sender, out var path))
            {
                OpenInOtherPane(LeftPane, path);
            }
        }

        private async void CopyToRightPane_Click(object sender, RoutedEventArgs e)
        {
            await CopyOrMoveToPaneAsync(sender, LeftPane, RightPane, move: false);
        }

        private async void MoveToRightPane_Click(object sender, RoutedEventArgs e)
        {
            await CopyOrMoveToPaneAsync(sender, LeftPane, RightPane, move: true);
        }

        private async void CopyToLeftPane_Click(object sender, RoutedEventArgs e)
        {
            await CopyOrMoveToPaneAsync(sender, RightPane, LeftPane, move: false);
        }

        private async void MoveToLeftPane_Click(object sender, RoutedEventArgs e)
        {
            await CopyOrMoveToPaneAsync(sender, RightPane, LeftPane, move: true);
        }

        private async void RenamePath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPath(sender, out var path))
            {
                return;
            }

            await RenameSinglePathAsync(path, _activePane);
        }

        private async void DeletePath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPath(sender, out var path))
            {
                return;
            }

            await DeletePathAsync(path, _activePane);
        }

        private async Task DeletePathAsync(string path, PaneViewModel pane)
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var confirmed = await ConfirmAsync("刪除項目", $"確定要刪除「{name}」嗎？");
            if (!confirmed)
            {
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else
                {
                    return;
                }

                RefreshPane(pane);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("刪除失敗", ex.Message);
            }
        }

        private async void CreateFolderLeft_Click(object sender, RoutedEventArgs e)
        {
            await CreateFolderAsync(LeftPane);
        }

        private async void CreateFolderRight_Click(object sender, RoutedEventArgs e)
        {
            await CreateFolderAsync(RightPane);
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPath(sender, out var path))
            {
                return;
            }

            var package = new DataPackage();
            package.SetText(path);
            Clipboard.SetContent(package);
        }

        private void SwapPanes_Click(object sender, RoutedEventArgs e)
        {
            var leftPath = LeftPane.CurrentPath;
            var rightPath = RightPane.CurrentPath;

            LeftPane.NavigateTo(rightPath);
            RightPane.NavigateTo(leftPath);
        }

        private async Task CopyOrMoveToPaneAsync(object sender, PaneViewModel sourcePane, PaneViewModel targetPane, bool move)
        {
            ActivatePane(sourcePane);

            if (!TryGetPath(sender, out var path))
            {
                return;
            }

            await CopyOrMovePathAsync(path, sourcePane, targetPane, move);
        }

        private async Task CopyOrMovePathAsync(string path, PaneViewModel sourcePane, PaneViewModel targetPane, bool move)
        {
            try
            {
                var destinationPath = BuildDestinationPath(targetPane.CurrentPath, path, move);
                ExecuteNativeTransfer(path, destinationPath, move);

                RefreshPane(sourcePane);
                RefreshPane(targetPane);
                targetPane.SelectedItem = targetPane.Items.FirstOrDefault(item => PathEquals(item.FullPath, destinationPath));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(move ? "搬移失敗" : "複製失敗", ex.Message);
            }
        }

        private static bool TryGetPath(object sender, out string path)
        {
            if (sender is MenuFlyoutItem { Tag: string itemPath } && !string.IsNullOrWhiteSpace(itemPath))
            {
                path = itemPath;
                return true;
            }

            path = string.Empty;
            return false;
        }

        private static bool TryGetPathGroup(object sender, out PathGroup group)
        {
            if (sender is MenuFlyoutItem { Tag: PathGroup taggedGroup })
            {
                group = taggedGroup;
                return true;
            }

            if (sender is FrameworkElement { DataContext: PathGroup dataContextGroup })
            {
                group = dataContextGroup;
                return true;
            }

            group = null!;
            return false;
        }

        private static bool TryGetGroupedPathItem(object sender, out GroupedPathItem item)
        {
            if (sender is MenuFlyoutItem { Tag: GroupedPathItem taggedItem })
            {
                item = taggedItem;
                return true;
            }

            if (sender is FrameworkElement { DataContext: GroupedPathItem dataContextItem })
            {
                item = dataContextItem;
                return true;
            }

            item = null!;
            return false;
        }

        private void OpenInOtherPane(PaneViewModel pane, string path)
        {
            ActivatePane(pane);

            if (Directory.Exists(path))
            {
                OpenInPane(pane, path);
                return;
            }

            if (!File.Exists(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                return;
            }

            OpenInPane(pane, parent);
            pane.SelectedItem = pane.Items.FirstOrDefault(item => PathEquals(item.FullPath, path));
        }

        private static void OpenPath(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }

        private async Task CreateFolderAsync(PaneViewModel pane)
        {
            ActivatePane(pane);

            var folderName = await PromptForTextAsync("新增資料夾", "輸入資料夾名稱", "New Folder");
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            try
            {
                var targetPath = EnsureUniquePath(Path.Combine(pane.CurrentPath, folderName));
                Directory.CreateDirectory(targetPath);
                RefreshPane(pane);
                pane.SelectedItem = pane.Items.FirstOrDefault(item => PathEquals(item.FullPath, targetPath));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("新增資料夾失敗", ex.Message);
            }
        }

        private static string BuildDestinationPath(string targetDirectory, string sourcePath, bool move)
        {
            var fileName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.Combine(targetDirectory, fileName);
        }

        private static void EnsureDestinationDoesNotExist(string path)
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                throw new IOException($"目的地已存在: {path}");
            }
        }

        private async Task TriggerRenameAsync()
        {
            var pane = _activePane;
            var selectedEntries = GetSelectedEntriesInDisplayOrder(pane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            if (selectedEntries.Count == 1)
            {
                await RenameSinglePathAsync(selectedEntries[0].FullPath, pane);
                return;
            }

            await BatchRenameSelectedAsync(pane, selectedEntries);
        }

        private async Task RenameSinglePathAsync(string path, PaneViewModel pane)
        {
            var currentName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var newName = await PromptForTextAsync("重新命名", "輸入新的名稱", currentName);
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, currentName, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var targetPath = Path.Combine(Path.GetDirectoryName(path) ?? pane.CurrentPath, newName);
                EnsureDestinationDoesNotExist(targetPath);

                if (Directory.Exists(path))
                {
                    Directory.Move(path, targetPath);
                }
                else if (File.Exists(path))
                {
                    File.Move(path, targetPath);
                }
                else
                {
                    return;
                }

                RefreshPane(pane);
                pane.SelectedItem = pane.Items.FirstOrDefault(item => PathEquals(item.FullPath, targetPath));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("重新命名失敗", ex.Message);
            }
        }

        private async Task BatchRenameSelectedAsync(PaneViewModel pane, IReadOnlyList<FileEntry> selectedEntries)
        {
            var defaultBaseName = BuildBatchRenameDefaultBaseName(selectedEntries);
            var baseName = await PromptForTextAsync(
                "批次重新命名",
                $"已選取 {selectedEntries.Count} 個項目，輸入新的基底名稱，系統會自動編號。",
                defaultBaseName);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return;
            }

            var digits = Math.Max(2, selectedEntries.Count.ToString(CultureInfo.InvariantCulture).Length);
            var selectedPathSet = new HashSet<string>(
                selectedEntries.Select(entry => NormalizePath(entry.FullPath)),
                StringComparer.OrdinalIgnoreCase);
            var finalPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var renamePlan = new List<(FileEntry Entry, string TempPath, string FinalPath)>();

            for (var index = 0; index < selectedEntries.Count; index++)
            {
                var entry = selectedEntries[index];
                var suffix = (index + 1).ToString($"D{digits}", CultureInfo.InvariantCulture);
                var extension = entry.IsDirectory ? string.Empty : Path.GetExtension(entry.Name);
                var finalName = $"{baseName} {suffix}{extension}";
                var parentPath = Path.GetDirectoryName(entry.FullPath) ?? pane.CurrentPath;
                var finalPath = Path.Combine(parentPath, finalName);

                if (!finalPathSet.Add(finalPath))
                {
                    await ShowMessageAsync("批次重新命名失敗", $"產生了重複名稱：{finalName}");
                    return;
                }

                if (!selectedPathSet.Contains(NormalizePath(finalPath)) && (Directory.Exists(finalPath) || File.Exists(finalPath)))
                {
                    await ShowMessageAsync("批次重新命名失敗", $"目標已存在：{finalName}");
                    return;
                }

                renamePlan.Add((entry, BuildTemporaryRenamePath(entry.FullPath), finalPath));
            }

            try
            {
                foreach (var step in renamePlan)
                {
                    MovePath(step.Entry.FullPath, step.TempPath, step.Entry.IsDirectory);
                }

                foreach (var step in renamePlan)
                {
                    MovePath(step.TempPath, step.FinalPath, step.Entry.IsDirectory);
                }

                RefreshPane(pane);
                RestorePaneSelection(pane, renamePlan.Select(step => step.FinalPath).ToList());
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("批次重新命名失敗", ex.Message);
            }
        }

        private IReadOnlyList<FileEntry> GetSelectedEntriesInDisplayOrder(PaneViewModel pane)
        {
            var selectedPathSet = new HashSet<string>(
                GetSelectedEntries(pane).Select(entry => NormalizePath(entry.FullPath)),
                StringComparer.OrdinalIgnoreCase);

            return pane.Items
                .Where(item => selectedPathSet.Contains(NormalizePath(item.FullPath)))
                .ToList();
        }

        private static string BuildBatchRenameDefaultBaseName(IReadOnlyList<FileEntry> selectedEntries)
        {
            if (selectedEntries.Count == 0)
            {
                return "New Name";
            }

            var firstName = selectedEntries[0].IsDirectory
                ? selectedEntries[0].Name
                : Path.GetFileNameWithoutExtension(selectedEntries[0].Name);

            return string.IsNullOrWhiteSpace(firstName) ? "New Name" : firstName;
        }

        private static string BuildTemporaryRenamePath(string originalPath)
        {
            var parentPath = Path.GetDirectoryName(originalPath) ?? string.Empty;
            var name = Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.Combine(parentPath, $".nuone-rename-{Guid.NewGuid():N}-{name}");
        }

        private static void MovePath(string sourcePath, string destinationPath, bool isDirectory)
        {
            if (isDirectory)
            {
                Directory.Move(sourcePath, destinationPath);
                return;
            }

            File.Move(sourcePath, destinationPath);
        }

        private void RestorePaneSelection(PaneViewModel pane, IReadOnlyList<string> selectedPaths)
        {
            var listView = ReferenceEquals(pane, LeftPane) ? LeftPaneListView : RightPaneListView;
            listView.SelectedItems.Clear();

            foreach (var entry in pane.Items.Where(item => selectedPaths.Any(path => PathEquals(path, item.FullPath))))
            {
                listView.SelectedItems.Add(entry);
            }

            SyncPaneSelectionFromListView(pane, listView);
            ApplySelectionVisuals(listView);
        }

        private void ApplySelectionVisuals(ListView listView)
        {
            foreach (var item in listView.Items)
            {
                if (listView.ContainerFromItem(item) is ListViewItem listViewItem)
                {
                    ApplySelectionVisualToContainer(listViewItem);
                }
            }
        }

        private void ApplySelectionVisualToContainer(ListViewItem listViewItem)
        {
            if (listViewItem.ContentTemplateRoot is not Border border)
            {
                return;
            }

            var isSelected = listViewItem.IsSelected;
            border.Background = isSelected ? SelectedItemBackgroundBrush : UnselectedItemBackgroundBrush;
            border.BorderBrush = isSelected ? SelectedItemBorderBrush : UnselectedItemBorderBrush;
            border.BorderThickness = isSelected ? SelectedItemBorderThickness : UnselectedItemBorderThickness;
        }

        private static string EnsureUniquePath(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return path;
            }

            var parent = Path.GetDirectoryName(path) ?? string.Empty;
            var extension = Path.GetExtension(path);
            var baseName = string.IsNullOrEmpty(extension)
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(path);

            for (var index = 2; index < 10_000; index++)
            {
                var candidateName = string.IsNullOrEmpty(extension)
                    ? $"{baseName} ({index})"
                    : $"{baseName} ({index}){extension}";
                var candidatePath = Path.Combine(parent, candidateName);
                if (!Directory.Exists(candidatePath) && !File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            throw new IOException("找不到可用的目的地名稱。");
        }

        private static void ExecuteNativeTransfer(string sourcePath, string destinationPath, bool move)
        {
            if (Directory.Exists(sourcePath))
            {
                if (move)
                {
                    FileSystem.MoveDirectory(sourcePath, destinationPath, UIOption.AllDialogs, UICancelOption.ThrowException);
                }
                else
                {
                    FileSystem.CopyDirectory(sourcePath, destinationPath, UIOption.AllDialogs, UICancelOption.ThrowException);
                }

                return;
            }

            if (!File.Exists(sourcePath))
            {
                return;
            }

            if (move)
            {
                FileSystem.MoveFile(sourcePath, destinationPath, UIOption.AllDialogs, UICancelOption.ThrowException);
            }
            else
            {
                FileSystem.CopyFile(sourcePath, destinationPath, UIOption.AllDialogs, UICancelOption.ThrowException);
            }
        }

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

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
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

            await dialog.ShowAsync();
        }

        private async Task ShowSettingsDialogAsync()
        {
            _isSettingsDialogOpen = true;

            var editingCopyKey = _shortcutSettings.CopyToOtherPaneKey;
            var editingMoveKey = _shortcutSettings.MoveToOtherPaneKey;
            var editingNavigateUpKey = _shortcutSettings.NavigateUpKey;
            var captureTarget = ShortcutCaptureTarget.None;

            var copyShortcutTextBox = CreateShortcutTextBox(editingCopyKey);
            var moveShortcutTextBox = CreateShortcutTextBox(editingMoveKey);
            var navigateUpShortcutTextBox = CreateShortcutTextBox(editingNavigateUpKey);
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
                    await ShowMessageAsync("快捷鍵重複", "三個動作不能使用相同的快捷鍵。");
                    return;
                }

                _shortcutSettings = new ShortcutSettings
                {
                    CopyToOtherPaneKey = editingCopyKey,
                    MoveToOtherPaneKey = editingMoveKey,
                    NavigateUpKey = editingNavigateUpKey,
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

        private void RootLayout_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled || _isSettingsDialogOpen)
            {
                return;
            }

            if (_settingsCaptureTarget != ShortcutCaptureTarget.None)
            {
                e.Handled = true;

                if (e.Key is Windows.System.VirtualKey.Tab
                    or Windows.System.VirtualKey.LeftShift
                    or Windows.System.VirtualKey.RightShift
                    or Windows.System.VirtualKey.LeftControl
                    or Windows.System.VirtualKey.RightControl
                    or Windows.System.VirtualKey.LeftMenu
                    or Windows.System.VirtualKey.RightMenu)
                {
                    return;
                }

                var capturedKey = NormalizeCapturedKey(e.Key);
                switch (_settingsCaptureTarget)
                {
                    case ShortcutCaptureTarget.CopyToOtherPane:
                        _editingShortcutSettings.CopyToOtherPaneKey = capturedKey;
                        break;
                    case ShortcutCaptureTarget.MoveToOtherPane:
                        _editingShortcutSettings.MoveToOtherPaneKey = capturedKey;
                        break;
                    case ShortcutCaptureTarget.NavigateUp:
                        _editingShortcutSettings.NavigateUpKey = capturedKey;
                        break;
                    case ShortcutCaptureTarget.Delete:
                        _editingShortcutSettings.DeleteKey = capturedKey;
                        break;
                }

                _settingsCaptureTarget = ShortcutCaptureTarget.None;
                SyncShortcutText();
                CaptureHintTextBlock.Text = "已擷取按鍵，按儲存即可套用。";
                return;
            }

            if (_activeSection != AppSection.FileManager)
            {
                return;
            }

            if (e.Key == _shortcutSettings.CopyToOtherPaneKey)
            {
                e.Handled = true;
                _ = CopySelectedToOtherPaneAsync();
                return;
            }

            if (e.Key == _shortcutSettings.MoveToOtherPaneKey)
            {
                e.Handled = true;
                _ = MoveSelectedToOtherPaneAsync();
                return;
            }

            if (e.Key == _shortcutSettings.NavigateUpKey || IsBreakAlias(_shortcutSettings.NavigateUpKey, e.Key))
            {
                e.Handled = true;
                NavigateUp(_activePane);
                return;
            }

            if (e.Key == _shortcutSettings.DeleteKey)
            {
                e.Handled = true;
                _ = DeleteSelectedAsync();
                return;
            }

            if (e.Key == Windows.System.VirtualKey.F2)
            {
                e.Handled = true;
                _ = TriggerRenameAsync();
            }
        }

        private async Task CopySelectedToOtherPaneAsync()
        {
            var sourcePane = _activePane;
            var targetPane = ReferenceEquals(sourcePane, LeftPane) ? RightPane : LeftPane;
            var selectedEntries = GetSelectedEntries(sourcePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            foreach (var selectedEntry in selectedEntries)
            {
                await CopyOrMovePathAsync(selectedEntry.FullPath, sourcePane, targetPane, move: false);
            }
        }

        private async Task MoveSelectedToOtherPaneAsync()
        {
            var sourcePane = _activePane;
            var targetPane = ReferenceEquals(sourcePane, LeftPane) ? RightPane : LeftPane;
            var selectedEntries = GetSelectedEntries(sourcePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            foreach (var selectedEntry in selectedEntries.ToList())
            {
                await CopyOrMovePathAsync(selectedEntry.FullPath, sourcePane, targetPane, move: true);
            }
        }

        private async Task DeleteSelectedAsync()
        {
            var selectedEntries = GetSelectedEntries(_activePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            if (selectedEntries.Count == 1)
            {
                await DeletePathAsync(selectedEntries[0].FullPath, _activePane);
                return;
            }

            var confirmed = await ConfirmAsync("刪除項目", $"確定要刪除這 {selectedEntries.Count} 個項目嗎？");
            if (!confirmed)
            {
                return;
            }

            foreach (var entry in selectedEntries.ToList())
            {
                try
                {
                    if (Directory.Exists(entry.FullPath))
                    {
                        Directory.Delete(entry.FullPath, recursive: true);
                    }
                    else if (File.Exists(entry.FullPath))
                    {
                        File.Delete(entry.FullPath);
                    }
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync("刪除失敗", $"{entry.Name}\n{ex.Message}");
                    break;
                }
            }

            RefreshPane(_activePane);
        }

        private void QueueSelectionFlyout(PaneViewModel pane, FrameworkElement target, string path)
        {
            _pendingFlyoutPane = pane;
            _pendingFlyoutTarget = target;
            _pendingFlyoutPath = path;
            _selectionFlyoutTimer.Stop();
            _selectionFlyoutTimer.Start();
        }

        private void CancelPendingFlyout()
        {
            _selectionFlyoutTimer.Stop();
            _pendingFlyoutPane = null;
            _pendingFlyoutTarget = null;
            _pendingFlyoutPath = null;
        }

        private void SelectionFlyoutTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();

            if (_pendingFlyoutPane is null || _pendingFlyoutTarget is null || string.IsNullOrWhiteSpace(_pendingFlyoutPath))
            {
                CancelPendingFlyout();
                return;
            }

            if (_pendingFlyoutPane.SelectedItem is null || !PathEquals(_pendingFlyoutPane.SelectedItem.FullPath, _pendingFlyoutPath))
            {
                CancelPendingFlyout();
                return;
            }

            if (FlyoutBase.GetAttachedFlyout(_pendingFlyoutTarget) is FlyoutBase flyout)
            {
                flyout.ShowAt(_pendingFlyoutTarget);
            }

            CancelPendingFlyout();
        }

        private void TryAddGroup()
        {
            var title = NewGroupNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            if (CustomGroups.Any(group => string.Equals(group.Title, title, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            CustomGroups.Add(new PathGroup(title));
            SaveCustomGroupsSafe();
            HideAddGroupEditor();
        }

        private void HideAddGroupEditor()
        {
            NewGroupNameTextBox.Text = string.Empty;
            AddGroupEditor.Visibility = Visibility.Collapsed;
        }

        private void LoadCustomGroups()
        {
            CustomGroups.Clear();

            var groups = LoadGroupConfigs();
            foreach (var group in groups.Where(static group => !string.IsNullOrWhiteSpace(group.Title)))
            {
                var pathGroup = new PathGroup(group.Title);
                foreach (var item in group.Items.Where(static item => !string.IsNullOrWhiteSpace(item.Path)))
                {
                    pathGroup.Items.Add(new GroupedPathItem
                    {
                        Title = string.IsNullOrWhiteSpace(item.Title) || PathEquals(item.Title, item.Path)
                            ? GetDisplayName(item.Path)
                            : item.Title,
                        Path = item.Path,
                        ParentGroup = pathGroup,
                    });
                }

                CustomGroups.Add(pathGroup);
            }
        }

        private static List<PathGroupConfig> LoadGroupConfigs()
        {
            try
            {
                if (File.Exists(SettingsConfigPath))
                {
                    var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsConfigPath), JsonOptions);
                    if (settings?.Groups is { Count: > 0 })
                    {
                        return settings.Groups;
                    }
                }

                if (File.Exists(LegacyGroupsConfigPath))
                {
                    return JsonSerializer.Deserialize<List<PathGroupConfig>>(File.ReadAllText(LegacyGroupsConfigPath), JsonOptions) ?? new();
                }
            }
            catch
            {
            }

            return new List<PathGroupConfig>();
        }

        private void LoadShortcutSettings()
        {
            _shortcutSettings = ShortcutSettings.CreateDefault();

            if (!File.Exists(SettingsConfigPath))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(SettingsConfigPath));
                document.RootElement.TryGetProperty("copyToOtherPaneKey", out var copyProperty);
                if (copyProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.CopyToOtherPaneKey), out copyProperty);
                }

                document.RootElement.TryGetProperty("moveToOtherPaneKey", out var moveProperty);
                if (moveProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.MoveToOtherPaneKey), out moveProperty);
                }

                document.RootElement.TryGetProperty("navigateUpKey", out var navigateUpProperty);
                if (navigateUpProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.NavigateUpKey), out navigateUpProperty);
                }

                document.RootElement.TryGetProperty("deleteKey", out var deleteProperty);
                if (deleteProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.DeleteKey), out deleteProperty);
                }

                document.RootElement.TryGetProperty("showHiddenSystemItems", out var showHiddenSystemItemsProperty);
                if (showHiddenSystemItemsProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.ShowHiddenSystemItems), out showHiddenSystemItemsProperty);
                }

                _shortcutSettings = new ShortcutSettings
                {
                    CopyToOtherPaneKey = ReadShortcutKey(copyProperty, ShortcutSettings.DefaultCopyToOtherPaneKey),
                    MoveToOtherPaneKey = ReadShortcutKey(moveProperty, ShortcutSettings.DefaultMoveToOtherPaneKey),
                    NavigateUpKey = ReadShortcutKey(navigateUpProperty, ShortcutSettings.DefaultNavigateUpKey),
                    DeleteKey = ReadShortcutKey(deleteProperty, ShortcutSettings.DefaultDeleteKey),
                    ShowHiddenSystemItems = ReadBooleanSetting(showHiddenSystemItemsProperty, ShortcutSettings.DefaultShowHiddenSystemItems),
                };
            }
            catch
            {
                _shortcutSettings = ShortcutSettings.CreateDefault();
            }
        }

        private void SaveCustomGroups()
        {
            Directory.CreateDirectory(ConfigDirectoryPath);
            SaveAppSettings();
        }

        private async void SaveCustomGroupsSafe()
        {
            try
            {
                SaveCustomGroups();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("儲存分組失敗", ex.Message);
            }
        }

        private void SaveShortcutSettings()
        {
            Directory.CreateDirectory(ConfigDirectoryPath);
            SaveAppSettings();
        }

        private void SaveAppSettings()
        {
            var settings = new ShortcutSettingsConfig
            {
                CopyToOtherPaneKey = _shortcutSettings.CopyToOtherPaneKey,
                MoveToOtherPaneKey = _shortcutSettings.MoveToOtherPaneKey,
                NavigateUpKey = _shortcutSettings.NavigateUpKey,
                DeleteKey = _shortcutSettings.DeleteKey,
                ShowHiddenSystemItems = _shortcutSettings.ShowHiddenSystemItems,
                LeftPanePath = LeftPane.CurrentPath,
                RightPanePath = RightPane.CurrentPath,
                Groups = BuildGroupConfigs(),
            };

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsConfigPath, json);
        }

        private List<PathGroupConfig> BuildGroupConfigs()
        {
            return CustomGroups
                .Select(group => new PathGroupConfig
                {
                    Title = group.Title,
                    Items = group.Items
                        .Select(item => new GroupedPathItemConfig
                        {
                            Title = item.Title,
                            Path = item.Path,
                        })
                        .ToList(),
                })
                .ToList();
        }

        private async void SaveShortcutSettingsSafe()
        {
            try
            {
                SaveShortcutSettings();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("儲存設定失敗", ex.Message);
            }
        }

        private void SavePanePathsSafe()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LeftPane.CurrentPath) && string.IsNullOrWhiteSpace(RightPane.CurrentPath))
                {
                    return;
                }

                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveAppSettings();
            }
            catch
            {
            }
        }

        private static (string LeftPath, string RightPath) LoadSavedPanePaths()
        {
            try
            {
                if (!File.Exists(SettingsConfigPath))
                {
                    return default;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(SettingsConfigPath));
                var root = document.RootElement;

                var leftPath = ReadStringSetting(root, "leftPanePath", nameof(ShortcutSettingsConfig.LeftPanePath));
                var rightPath = ReadStringSetting(root, "rightPanePath", nameof(ShortcutSettingsConfig.RightPanePath));
                return (leftPath, rightPath);
            }
            catch
            {
                return default;
            }
        }

        private static bool AddPathToGroup(PathGroup group, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            if (group.Items.Any(item => PathEquals(item.Path, path)))
            {
                return false;
            }

            group.Items.Add(new GroupedPathItem
            {
                Title = GetDisplayName(path),
                Path = path,
                ParentGroup = group,
            });

            return true;
        }

        private static string GetDisplayName(string path)
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return path;
            }

            var segments = trimmed
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                return trimmed;
            }

            return segments[^1];
        }

        private static Windows.System.VirtualKey NormalizeCapturedKey(Windows.System.VirtualKey key)
        {
            return key == Windows.System.VirtualKey.Cancel
                ? Windows.System.VirtualKey.Pause
                : key;
        }

        private static bool IsBreakAlias(Windows.System.VirtualKey configuredKey, Windows.System.VirtualKey actualKey)
        {
            return configuredKey == Windows.System.VirtualKey.Pause && actualKey == Windows.System.VirtualKey.Cancel;
        }

        private static Windows.System.VirtualKey ReadShortcutKey(JsonElement property, Windows.System.VirtualKey fallback)
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue) && Enum.IsDefined(typeof(Windows.System.VirtualKey), numericValue))
            {
                return (Windows.System.VirtualKey)numericValue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var rawValue = property.GetString();
                if (Enum.TryParse<Windows.System.VirtualKey>(rawValue, true, out var enumValue))
                {
                    return NormalizeCapturedKey(enumValue);
                }

                if (string.Equals(rawValue, "Pause / Break", StringComparison.OrdinalIgnoreCase))
                {
                    return Windows.System.VirtualKey.Pause;
                }
            }

            return fallback;
        }

        private static bool ReadBooleanSetting(JsonElement property, bool fallback)
        {
            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
                _ => fallback,
            };
        }

        private static string ReadStringSetting(JsonElement root, string camelCaseName, string propertyName)
        {
            if (root.TryGetProperty(camelCaseName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string FormatShortcutKey(Windows.System.VirtualKey key)
        {
            return key switch
            {
                Windows.System.VirtualKey.Number0 => "0",
                Windows.System.VirtualKey.Number1 => "1",
                Windows.System.VirtualKey.Number2 => "2",
                Windows.System.VirtualKey.Number3 => "3",
                Windows.System.VirtualKey.Number4 => "4",
                Windows.System.VirtualKey.Number5 => "5",
                Windows.System.VirtualKey.Number6 => "6",
                Windows.System.VirtualKey.Number7 => "7",
                Windows.System.VirtualKey.Number8 => "8",
                Windows.System.VirtualKey.Number9 => "9",
                Windows.System.VirtualKey.Pause => "Pause / Break",
                Windows.System.VirtualKey.Control => "Ctrl",
                Windows.System.VirtualKey.LeftControl => "Left Ctrl",
                Windows.System.VirtualKey.RightControl => "Right Ctrl",
                Windows.System.VirtualKey.Shift => "Shift",
                Windows.System.VirtualKey.LeftShift => "Left Shift",
                Windows.System.VirtualKey.RightShift => "Right Shift",
                Windows.System.VirtualKey.Menu => "Alt",
                Windows.System.VirtualKey.LeftMenu => "Left Alt",
                Windows.System.VirtualKey.RightMenu => "Right Alt",
                _ => key.ToString(),
            };
        }

        private static T? FindDataContext<T>(DependencyObject? source)
            where T : class
        {
            var current = source;
            while (current is not null)
            {
                if (current is FrameworkElement { DataContext: T dataContext })
                {
                    return dataContext;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        internal static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var index = 0;

            while (value >= 1024 && index < units.Length - 1)
            {
                value /= 1024;
                index++;
            }

            return $"{value:0.#} {units[index]}";
        }
    }

    public sealed class PaneViewModel : ObservableObject
    {
        private readonly Stack<string> _history = new();
        private string _currentPath = string.Empty;
        private string _editablePath = string.Empty;
        private string _statusText = "尚未載入";
        private string _summaryText = string.Empty;
        private string _selectionText = string.Empty;
        private int _selectedCount;
        private bool _showHiddenSystemItems;
        private FileEntry? _selectedItem;

        public PaneViewModel(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public ObservableCollection<FileEntry> Items { get; } = new();

        public string CurrentPath
        {
            get => _currentPath;
            private set => SetProperty(ref _currentPath, value);
        }

        public string EditablePath
        {
            get => _editablePath;
            set => SetProperty(ref _editablePath, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string SummaryText
        {
            get => _summaryText;
            private set => SetProperty(ref _summaryText, value);
        }

        public string SelectionText
        {
            get => _selectionText;
            private set => SetProperty(ref _selectionText, value);
        }

        public int SelectedCount
        {
            get => _selectedCount;
            private set => SetProperty(ref _selectedCount, value);
        }

        public bool ShowHiddenSystemItems
        {
            get => _showHiddenSystemItems;
            set => SetProperty(ref _showHiddenSystemItems, value);
        }

        public FileEntry? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    if (SelectedCount <= 1)
                    {
                        SelectionText = value is null ? "未選取" : value.Name;
                    }
                }
            }
        }

        public void UpdateSelection(IReadOnlyList<FileEntry> selectedItems)
        {
            var selectedPaths = new HashSet<string>(
                selectedItems.Select(item => MainWindow.NormalizePath(item.FullPath)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in Items)
            {
                item.IsSelected = selectedPaths.Contains(MainWindow.NormalizePath(item.FullPath));
            }

            SelectedCount = selectedItems.Count;

            var primary = selectedItems.FirstOrDefault();
            _selectedItem = primary;
            OnPropertyChanged(nameof(SelectedItem));

            SelectionText = selectedItems.Count switch
            {
                0 => "未選取",
                1 => primary?.Name ?? "未選取",
                _ => $"{selectedItems.Count} 個已選取",
            };
        }

        public void NavigateTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            Load(path);
        }

        public void Refresh()
        {
            if (!string.IsNullOrWhiteSpace(CurrentPath))
            {
                Load(CurrentPath, rememberCurrent: false);
            }
        }

        public void NavigateUp()
        {
            if (string.IsNullOrWhiteSpace(CurrentPath))
            {
                return;
            }

            var parent = Directory.GetParent(CurrentPath);
            if (parent is not null)
            {
                NavigateTo(parent.FullName);
            }
            else
            {
                Load(CurrentPath, rememberCurrent: false);
            }
        }

        public void GoBack()
        {
            while (_history.Count > 0)
            {
                var previous = _history.Pop();
                if (Directory.Exists(previous))
                {
                    Load(previous, rememberCurrent: false);
                    return;
                }
            }
        }

        private void Load(string path, bool rememberCurrent = true)
        {
            try
            {
                var directory = new DirectoryInfo(path);
                if (!directory.Exists)
                {
                    StatusText = "路徑不存在";
                    return;
                }

                if (rememberCurrent && !string.IsNullOrWhiteSpace(CurrentPath) && !MainWindow.PathEquals(CurrentPath, path))
                {
                    _history.Push(CurrentPath);
                }

                CurrentPath = directory.FullName;
                EditablePath = CurrentPath;
                Items.Clear();

                foreach (var folder in SafeEnumerateDirectories(directory, ShowHiddenSystemItems))
                {
                    Items.Add(FileEntry.FromDirectory(folder));
                }

                foreach (var file in SafeEnumerateFiles(directory, ShowHiddenSystemItems))
                {
                    Items.Add(FileEntry.FromFile(file));
                }

                var defaultSelection = Items.FirstOrDefault();
                UpdateSelection(defaultSelection is null ? Array.Empty<FileEntry>() : new[] { defaultSelection });
                StatusText = $"{Items.Count} 個項目";
                SummaryText = $"{Items.Count(static item => item.IsDirectory)} 資料夾 / {Items.Count(static item => !item.IsDirectory)} 檔案";
            }
            catch (Exception ex)
            {
                Items.Clear();
                UpdateSelection(Array.Empty<FileEntry>());
                CurrentPath = path;
                EditablePath = path;
                StatusText = "無法載入";
                SummaryText = ex.Message;
            }
        }

        private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo directory, bool showHiddenSystemItems)
        {
            try
            {
                return directory
                    .EnumerateDirectories()
                    .Where(item => showHiddenSystemItems || !ShouldHideFileSystemInfo(item))
                    .OrderBy(static item => item.Name)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<DirectoryInfo>();
            }
        }

        private static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo directory, bool showHiddenSystemItems)
        {
            try
            {
                return directory
                    .EnumerateFiles()
                    .Where(item => showHiddenSystemItems || !ShouldHideFileSystemInfo(item))
                    .OrderBy(static item => item.Name)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<FileInfo>();
            }
        }

        private static bool ShouldHideFileSystemInfo(FileSystemInfo item)
        {
            var attributes = item.Attributes;
            return attributes.HasFlag(FileAttributes.Hidden)
                || attributes.HasFlag(FileAttributes.System)
                || attributes.HasFlag(FileAttributes.ReparsePoint);
        }
    }

    public enum AppSection
    {
        FileManager,
        Settings,
    }

    public enum SettingsSection
    {
        General,
        Appearance,
        Shortcuts,
    }

    public enum ShortcutCaptureTarget
    {
        None,
        CopyToOtherPane,
        MoveToOtherPane,
        NavigateUp,
        Delete,
    }

    public sealed class ShortcutSettings
    {
        public const Windows.System.VirtualKey DefaultCopyToOtherPaneKey = Windows.System.VirtualKey.F5;
        public const Windows.System.VirtualKey DefaultMoveToOtherPaneKey = Windows.System.VirtualKey.F6;
        public const Windows.System.VirtualKey DefaultNavigateUpKey = Windows.System.VirtualKey.Pause;
        public const Windows.System.VirtualKey DefaultDeleteKey = Windows.System.VirtualKey.Delete;
        public const bool DefaultShowHiddenSystemItems = false;

        public Windows.System.VirtualKey CopyToOtherPaneKey { get; set; } = DefaultCopyToOtherPaneKey;

        public Windows.System.VirtualKey MoveToOtherPaneKey { get; set; } = DefaultMoveToOtherPaneKey;

        public Windows.System.VirtualKey NavigateUpKey { get; set; } = DefaultNavigateUpKey;

        public Windows.System.VirtualKey DeleteKey { get; set; } = DefaultDeleteKey;

        public bool ShowHiddenSystemItems { get; set; } = DefaultShowHiddenSystemItems;

        public static ShortcutSettings CreateDefault()
        {
            return new ShortcutSettings();
        }
    }

    public sealed class ShortcutSettingsConfig
    {
        public Windows.System.VirtualKey CopyToOtherPaneKey { get; set; } = ShortcutSettings.DefaultCopyToOtherPaneKey;

        public Windows.System.VirtualKey MoveToOtherPaneKey { get; set; } = ShortcutSettings.DefaultMoveToOtherPaneKey;

        public Windows.System.VirtualKey NavigateUpKey { get; set; } = ShortcutSettings.DefaultNavigateUpKey;

        public Windows.System.VirtualKey DeleteKey { get; set; } = ShortcutSettings.DefaultDeleteKey;

        public bool ShowHiddenSystemItems { get; set; } = ShortcutSettings.DefaultShowHiddenSystemItems;

        public string LeftPanePath { get; set; } = string.Empty;

        public string RightPanePath { get; set; } = string.Empty;

        public List<PathGroupConfig> Groups { get; set; } = new();
    }

    public sealed class PaneDirectoryWatcher : IDisposable
    {
        private readonly PaneViewModel _pane;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Action<PaneViewModel> _refreshAction;
        private readonly DispatcherQueueTimer _debounceTimer;
        private FileSystemWatcher? _watcher;
        private string _watchedPath = string.Empty;
        private bool _isDisposed;

        public PaneDirectoryWatcher(
            PaneViewModel pane,
            DispatcherQueue dispatcherQueue,
            Action<PaneViewModel> refreshAction,
            TimeSpan debounceInterval)
        {
            _pane = pane;
            _dispatcherQueue = dispatcherQueue;
            _refreshAction = refreshAction;

            _debounceTimer = dispatcherQueue.CreateTimer();
            _debounceTimer.Interval = debounceInterval;
            _debounceTimer.IsRepeating = false;
            _debounceTimer.Tick += DebounceTimer_Tick;
        }

        public void Watch(string? path)
        {
            if (_isDisposed)
            {
                return;
            }

            var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
            if (string.Equals(_watchedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            StopWatching();

            if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
            {
                _watchedPath = string.Empty;
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(normalizedPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.CreationTime
                        | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += Watcher_Changed;
                _watcher.Created += Watcher_Changed;
                _watcher.Deleted += Watcher_Changed;
                _watcher.Renamed += Watcher_Renamed;
                _watcher.Error += Watcher_Error;
                _watchedPath = normalizedPath;
            }
            catch
            {
                StopWatching();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _debounceTimer.Stop();
            _debounceTimer.Tick -= DebounceTimer_Tick;
            StopWatching();
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            ScheduleRefresh();
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            ScheduleRefresh();
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            var currentPath = _watchedPath;
            _dispatcherQueue.TryEnqueue(() => Watch(currentPath));
        }

        private void ScheduleRefresh()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed)
                {
                    return;
                }

                _debounceTimer.Stop();
                _debounceTimer.Start();
            });
        }

        private void DebounceTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();

            if (_isDisposed || string.IsNullOrWhiteSpace(_pane.CurrentPath))
            {
                return;
            }

            if (!string.Equals(_watchedPath, Path.GetFullPath(_pane.CurrentPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _refreshAction(_pane);
        }

        private void StopWatching()
        {
            _debounceTimer.Stop();

            if (_watcher is null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= Watcher_Changed;
            _watcher.Created -= Watcher_Changed;
            _watcher.Deleted -= Watcher_Changed;
            _watcher.Renamed -= Watcher_Renamed;
            _watcher.Error -= Watcher_Error;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    internal static partial class ShellContextMenuHost
    {
        private const uint ShellMenuFirstId = 1;
        private const uint ShellMenuLastId = 0x7FFF;
        private const uint TpmReturnCommand = 0x0100;
        private const uint TpmRightButton = 0x0002;
        private const int WmNull = 0x0000;
        private const int CmIcMaskUnicode = 0x00004000;
        private const int CmIcMaskPtInvoke = 0x20000000;
        private const int SwShownormal = 1;
        private static readonly Guid IidShellFolder = typeof(IShellFolder).GUID;
        private static readonly Guid IidContextMenu = typeof(IContextMenu).GUID;
        private static readonly SUBCLASSPROC MenuSubclassProc = MenuWindowSubclassProc;
        private static ShellContextMenuSession? _activeSession;

        public static void ShowForPaths(
            nint windowHandle,
            double rasterizationScale,
            double x,
            double y,
            string parentFolderPath,
            IReadOnlyList<string> selectedPaths)
        {
            if (windowHandle == nint.Zero || string.IsNullOrWhiteSpace(parentFolderPath) || selectedPaths.Count == 0)
            {
                return;
            }

            var screenPoint = new POINT(
                (int)Math.Round(x * rasterizationScale, MidpointRounding.AwayFromZero),
                (int)Math.Round(y * rasterizationScale, MidpointRounding.AwayFromZero));
            ClientToScreen(windowHandle, ref screenPoint);

            using var session = ShellContextMenuSession.Create(windowHandle, parentFolderPath, selectedPaths);
            _activeSession = session;

            SetForegroundWindow(windowHandle);
            if (!SetWindowSubclass(windowHandle, MenuSubclassProc, 1, nint.Zero))
            {
                _activeSession = null;
                return;
            }

            try
            {
                var selectedCommand = TrackPopupMenuEx(
                    session.MenuHandle,
                    TpmReturnCommand | TpmRightButton,
                    screenPoint.X,
                    screenPoint.Y,
                    windowHandle,
                    nint.Zero);

                if (selectedCommand >= ShellMenuFirstId && selectedCommand <= ShellMenuLastId)
                {
                    session.InvokeCommand(selectedCommand, screenPoint);
                }
            }
            finally
            {
                RemoveWindowSubclass(windowHandle, MenuSubclassProc, 1);
                PostMessage(windowHandle, WmNull, nint.Zero, nint.Zero);
                _activeSession = null;
            }
        }

        private static nint MenuWindowSubclassProc(
            nint hWnd,
            uint msg,
            nint wParam,
            nint lParam,
            nuint uIdSubclass,
            nint dwRefData)
        {
            var session = _activeSession;
            if (session is not null)
            {
                var result = session.HandleMenuMessage(msg, wParam, lParam);
                if (result.Handled)
                {
                    return result.Result;
                }
            }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        private sealed class ShellContextMenuSession : IDisposable
        {
            private readonly nint _windowHandle;
            private readonly IShellFolder _parentFolder;
            private readonly IntPtr[] _childPidls;
            private readonly nint _parentPidl;
            private readonly IContextMenu _contextMenu;
            private readonly IContextMenu2? _contextMenu2;
            private readonly IContextMenu3? _contextMenu3;
            private bool _disposed;

            private ShellContextMenuSession(
                nint windowHandle,
                IShellFolder parentFolder,
                nint parentPidl,
                IntPtr[] childPidls,
                IContextMenu contextMenu,
                IContextMenu2? contextMenu2,
                IContextMenu3? contextMenu3,
                nint menuHandle)
            {
                _windowHandle = windowHandle;
                _parentFolder = parentFolder;
                _parentPidl = parentPidl;
                _childPidls = childPidls;
                _contextMenu = contextMenu;
                _contextMenu2 = contextMenu2;
                _contextMenu3 = contextMenu3;
                MenuHandle = menuHandle;
            }

            public nint MenuHandle { get; }

            public static ShellContextMenuSession Create(nint windowHandle, string parentFolderPath, IReadOnlyList<string> selectedPaths)
            {
                var desktopFolder = default(IShellFolder);
                var parentFolder = default(IShellFolder);
                var parentPidl = nint.Zero;
                var childPidls = new IntPtr[selectedPaths.Count];
                var menuHandle = nint.Zero;
                nint contextMenuPtr = nint.Zero;

                try
                {
                    ThrowIfFailed(SHGetDesktopFolder(out desktopFolder));

                    uint attributes = 0;
                    var shellFolderGuid = IidShellFolder;
                    var contextMenuGuid = IidContextMenu;
                    ThrowIfFailed(SHParseDisplayName(parentFolderPath, nint.Zero, out parentPidl, 0, out attributes));

                    ThrowIfFailed(desktopFolder.BindToObject(parentPidl, nint.Zero, ref shellFolderGuid, out var parentFolderPtr));
                    parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(parentFolderPtr);
                    Marshal.Release(parentFolderPtr);

                    for (var index = 0; index < selectedPaths.Count; index++)
                    {
                        var displayName = Path.GetFileName(selectedPaths[index].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        attributes = 0;
                        ThrowIfFailed(parentFolder.ParseDisplayName(
                            windowHandle,
                            nint.Zero,
                            displayName,
                            out _,
                            out childPidls[index],
                            ref attributes));
                    }

                    ThrowIfFailed(parentFolder.GetUIObjectOf(
                        windowHandle,
                        (uint)childPidls.Length,
                        childPidls,
                        ref contextMenuGuid,
                        nint.Zero,
                        out contextMenuPtr));

                    var contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
                    var contextMenu2 = contextMenu as IContextMenu2;
                    var contextMenu3 = contextMenu as IContextMenu3;
                    Marshal.Release(contextMenuPtr);
                    contextMenuPtr = nint.Zero;

                    menuHandle = CreatePopupMenu();
                    if (menuHandle == nint.Zero)
                    {
                        throw new InvalidOperationException("無法建立 Shell 選單。");
                    }

                    ThrowIfFailed(contextMenu.QueryContextMenu(menuHandle, 0, ShellMenuFirstId, ShellMenuLastId, 0));

                    return new ShellContextMenuSession(
                        windowHandle,
                        parentFolder,
                        parentPidl,
                        childPidls,
                        contextMenu,
                        contextMenu2,
                        contextMenu3,
                        menuHandle);
                }
                catch
                {
                    if (menuHandle != nint.Zero)
                    {
                        DestroyMenu(menuHandle);
                    }

                    foreach (var pidl in childPidls.Where(pidl => pidl != nint.Zero))
                    {
                        CoTaskMemFree(pidl);
                    }

                if (contextMenuPtr != nint.Zero)
                {
                    Marshal.Release(contextMenuPtr);
                }

                    if (parentFolder is not null)
                    {
                        Marshal.ReleaseComObject(parentFolder);
                    }

                    if (parentPidl != nint.Zero)
                    {
                        CoTaskMemFree(parentPidl);
                    }

                    throw;
                }
                finally
                {
                    if (desktopFolder is not null)
                    {
                        Marshal.ReleaseComObject(desktopFolder);
                    }
                }
            }

            public ShellMenuMessageResult HandleMenuMessage(uint msg, nint wParam, nint lParam)
            {
                if (_contextMenu3 is not null)
                {
                    var hr = _contextMenu3.HandleMenuMsg2(msg, wParam, lParam, out var result);
                    if (hr == 0)
                    {
                        return new ShellMenuMessageResult(true, result);
                    }
                }

                if (_contextMenu2 is not null)
                {
                    var hr = _contextMenu2.HandleMenuMsg(msg, wParam, lParam);
                    if (hr == 0)
                    {
                        return new ShellMenuMessageResult(true, nint.Zero);
                    }
                }

                return default;
            }

            public void InvokeCommand(uint selectedCommand, POINT screenPoint)
            {
                var commandOffset = unchecked((nint)(selectedCommand - ShellMenuFirstId));
                var invokeInfo = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CmIcMaskUnicode | CmIcMaskPtInvoke,
                    hwnd = _windowHandle,
                    lpVerb = commandOffset,
                    lpVerbW = commandOffset,
                    nShow = SwShownormal,
                    ptInvoke = screenPoint,
                };

                ThrowIfFailed(_contextMenu.InvokeCommand(ref invokeInfo));
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (MenuHandle != nint.Zero)
                {
                    DestroyMenu(MenuHandle);
                }

                foreach (var pidl in _childPidls.Where(pidl => pidl != nint.Zero))
                {
                    CoTaskMemFree(pidl);
                }

                if (_parentPidl != nint.Zero)
                {
                    CoTaskMemFree(_parentPidl);
                }

                Marshal.ReleaseComObject(_contextMenu);
                Marshal.ReleaseComObject(_parentFolder);
            }
        }

        private readonly record struct ShellMenuMessageResult(bool Handled, nint Result);

        [ComImport]
        [Guid("000214E6-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            [PreserveSig]
            int ParseDisplayName(
                nint hwnd,
                nint pbc,
                [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
                out uint pchEaten,
                out nint ppidl,
                ref uint pdwAttributes);

            [PreserveSig]
            int EnumObjects(nint hwnd, int grfFlags, out nint ppenumIDList);

            [PreserveSig]
            int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);

            [PreserveSig]
            int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);

            [PreserveSig]
            int CompareIDs(nint lParam, nint pidl1, nint pidl2);

            [PreserveSig]
            int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);

            [PreserveSig]
            int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] apidl, ref uint rgfInOut);

            [PreserveSig]
            int GetUIObjectOf(
                nint hwndOwner,
                uint cidl,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] apidl,
                ref Guid riid,
                nint rgfReserved,
                out nint ppv);

            [PreserveSig]
            int GetDisplayNameOf(nint pidl, uint uFlags, out STRRET pName);

            [PreserveSig]
            int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out nint ppidlOut);
        }

        [ComImport]
        [Guid("000214E4-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu
        {
            [PreserveSig]
            int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

            [PreserveSig]
            int GetCommandString(nint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);
        }

        [ComImport]
        [Guid("000214F4-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu2
        {
            [PreserveSig]
            int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

            [PreserveSig]
            int GetCommandString(nint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);

            [PreserveSig]
            int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
        }

        [ComImport]
        [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu3
        {
            [PreserveSig]
            int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

            [PreserveSig]
            int GetCommandString(nint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);

            [PreserveSig]
            int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);

            [PreserveSig]
            int HandleMenuMsg2(uint uMsg, nint wParam, nint lParam, out nint plResult);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CMINVOKECOMMANDINFOEX
        {
            public int cbSize;
            public int fMask;
            public nint hwnd;
            public nint lpVerb;
            public nint lpParameters;
            public nint lpDirectory;
            public int nShow;
            public int dwHotKey;
            public nint hIcon;
            public nint lpTitle;
            public nint lpVerbW;
            public nint lpParametersW;
            public nint lpDirectoryW;
            public nint lpTitleW;
            public POINT ptInvoke;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STRRET
        {
            public uint uType;
            public nint pOleStr;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        private delegate nint SUBCLASSPROC(
            nint hWnd,
            uint uMsg,
            nint wParam,
            nint lParam,
            nuint uIdSubclass,
            nint dwRefData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(
            string pszName,
            nint pbc,
            out nint ppidl,
            uint sfgaoIn,
            out uint psfgaoOut);

        [DllImport("shell32.dll")]
        private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(nint pv);

        [DllImport("user32.dll")]
        private static extern nint CreatePopupMenu();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(nint hMenu);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(nint hmenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(
            nint hWnd,
            SUBCLASSPROC pfnSubclass,
            nuint uIdSubclass,
            nint dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(
            nint hWnd,
            SUBCLASSPROC pfnSubclass,
            nuint uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

        private static void ThrowIfFailed(int hr)
        {
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
        }
    }

    public sealed class FileEntry : ObservableObject
    {
        private static readonly SolidColorBrush UnselectedBackgroundBrush = new(Colors.Transparent);
        private static readonly SolidColorBrush SelectedBackgroundBrush = new(ColorHelper.FromArgb(255, 91, 20, 126));
        private static readonly SolidColorBrush UnselectedBorderBrush = new(Colors.Transparent);
        private static readonly SolidColorBrush SelectedBorderBrush = new(ColorHelper.FromArgb(255, 140, 60, 188));
        private static readonly Thickness UnselectedBorderThickness = new(0);
        private static readonly Thickness SelectedBorderThickness = new(1);
        private bool _isSelected;

        public string Name { get; set; } = string.Empty;

        public string FullPath { get; set; } = string.Empty;

        public bool IsDirectory { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string ModifiedText { get; set; } = string.Empty;

        public string SizeText { get; set; } = string.Empty;

        public string Glyph { get; set; } = "\uE8B7";

        public SolidColorBrush AccentColor { get; set; } = new(Colors.Gold);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    OnPropertyChanged(nameof(ItemBackground));
                    OnPropertyChanged(nameof(ItemBorderBrush));
                    OnPropertyChanged(nameof(ItemBorderThickness));
                }
            }
        }

        public Brush ItemBackground => IsSelected ? SelectedBackgroundBrush : UnselectedBackgroundBrush;

        public Brush ItemBorderBrush => IsSelected ? SelectedBorderBrush : UnselectedBorderBrush;

        public Thickness ItemBorderThickness => IsSelected ? SelectedBorderThickness : UnselectedBorderThickness;

        public static FileEntry FromDirectory(DirectoryInfo directory)
        {
            var visual = FileVisual.ForDirectory();

            return new FileEntry
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                Kind = "資料夾",
                ModifiedText = directory.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                SizeText = "--",
                Glyph = visual.Glyph,
                AccentColor = visual.Brush,
            };
        }

        public static FileEntry FromFile(FileInfo file)
        {
            var visual = FileVisual.ForFile(file.Extension);

            return new FileEntry
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                Kind = string.IsNullOrWhiteSpace(file.Extension) ? "檔案" : file.Extension.TrimStart('.').ToUpperInvariant(),
                ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                SizeText = MainWindow.FormatSize(file.Length),
                Glyph = visual.Glyph,
                AccentColor = visual.Brush,
            };
        }
    }

    public sealed class FileVisual
    {
        public string Glyph { get; init; } = "\uE8A5";

        public SolidColorBrush Brush { get; init; } = CreateBrush(255, 116, 216, 155);

        public static FileVisual ForDirectory()
        {
            return new FileVisual
            {
                Glyph = "\uE8B7",
                Brush = CreateBrush(255, 255, 212, 106),
            };
        }

        public static FileVisual ForFile(string extension)
        {
            var normalized = extension.Trim().ToLowerInvariant();

            return normalized switch
            {
                ".js" or ".ts" or ".jsx" or ".tsx" or ".mjs" or ".cjs" => new FileVisual
                {
                    Glyph = "\uE943",
                    Brush = CreateBrush(255, 255, 214, 10),
                },
                ".json" or ".yaml" or ".yml" or ".toml" or ".xml" or ".config" => new FileVisual
                {
                    Glyph = "\uE943",
                    Brush = CreateBrush(255, 114, 201, 255),
                },
                ".md" or ".txt" or ".log" => new FileVisual
                {
                    Glyph = "\uE8A5",
                    Brush = CreateBrush(255, 196, 188, 255),
                },
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" or ".ico" => new FileVisual
                {
                    Glyph = "\uEB9F",
                    Brush = CreateBrush(255, 255, 136, 102),
                },
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => new FileVisual
                {
                    Glyph = "\uE7B8",
                    Brush = CreateBrush(255, 255, 160, 122),
                },
                ".pdf" => new FileVisual
                {
                    Glyph = "\uEA90",
                    Brush = CreateBrush(255, 255, 99, 99),
                },
                ".doc" or ".docx" or ".rtf" => new FileVisual
                {
                    Glyph = "\uE8A5",
                    Brush = CreateBrush(255, 87, 156, 255),
                },
                ".xls" or ".xlsx" or ".csv" => new FileVisual
                {
                    Glyph = "\uE9D2",
                    Brush = CreateBrush(255, 96, 211, 148),
                },
                ".ppt" or ".pptx" => new FileVisual
                {
                    Glyph = "\uECA6",
                    Brush = CreateBrush(255, 255, 140, 78),
                },
                ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".sh" => new FileVisual
                {
                    Glyph = "\uE756",
                    Brush = CreateBrush(255, 255, 123, 172),
                },
                ".cs" or ".csproj" or ".sln" or ".vb" => new FileVisual
                {
                    Glyph = "\uE943",
                    Brush = CreateBrush(255, 191, 76, 255),
                },
                ".env" or ".ini" => new FileVisual
                {
                    Glyph = "\uE713",
                    Brush = CreateBrush(255, 255, 196, 87),
                },
                _ => new FileVisual
                {
                    Glyph = "\uE8A5",
                    Brush = CreateBrush(255, 116, 216, 155),
                },
            };
        }

        private static SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b)
        {
            return new SolidColorBrush(ColorHelper.FromArgb(a, r, g, b));
        }
    }

    public sealed class DriveShortcut
    {
        public string Name { get; set; } = string.Empty;

        public string RootPath { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public double UsagePercent { get; set; }
    }

    public sealed class PathGroup : ObservableObject
    {
        private string _title;

        public PathGroup(string title)
        {
            _title = title;
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public ObservableCollection<GroupedPathItem> Items { get; } = new();
    }

    public sealed class GroupedPathItem : ObservableObject
    {
        private string _title = string.Empty;
        private string _path = string.Empty;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Path
        {
            get => _path;
            set => SetProperty(ref _path, value);
        }

        [JsonIgnore]
        public PathGroup? ParentGroup { get; set; }
    }

    public sealed class PathGroupConfig
    {
        public string Title { get; set; } = string.Empty;

        public List<GroupedPathItemConfig> Items { get; set; } = new();
    }

    public sealed class GroupedPathItemConfig
    {
        public string Title { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;
    }

    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
