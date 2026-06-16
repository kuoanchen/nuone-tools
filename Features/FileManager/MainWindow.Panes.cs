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
        private void Pane_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(PaneViewModel.CurrentPath), StringComparison.Ordinal))
            {
                if (ReferenceEquals(sender, LeftPane))
                {
                    _leftPaneWatcher.Watch(LeftPane.CurrentPath);
                }
                else if (ReferenceEquals(sender, RightPane))
                {
                    _rightPaneWatcher.Watch(RightPane.CurrentPath);
                }
            }

            if (string.Equals(e.PropertyName, nameof(PaneViewModel.CurrentPath), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.StatusText), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.SummaryText), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.SelectionText), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.SelectedCount), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.SelectedItem), StringComparison.Ordinal))
            {
                UpdateSharedStatusBar();
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

        private void ApplyInitialWindowPlacement()
        {
            var savedPlacement = LoadSavedWindowPlacement();
            if (savedPlacement is not null &&
                savedPlacement.Width > 0 &&
                savedPlacement.Height > 0)
            {
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    savedPlacement.X,
                    savedPlacement.Y,
                    savedPlacement.Width,
                    savedPlacement.Height));
                return;
            }

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1680, 980));
        }

        private void ConfigureTitleBarInsets()
        {
            TopCommandBarBorder.Margin = new Thickness(0);
        }

        private void SeedSidebar()
        {
        }

        private void LoadDriveCards()
        {
            Drives.Clear();

            foreach (var drive in DriveInfo.GetDrives().Where(static d => d.IsReady))
            {
                var rootPath = NormalizeDriveRootPath(drive.RootDirectory.FullName);
                if (_hiddenDrivePaths.Contains(rootPath))
                {
                    continue;
                }

                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var used = Math.Max(total - free, 0);
                var usage = total == 0 ? 0 : Math.Round((double)used / total * 100, 1);
                var remainingPercent = total == 0 ? 0 : Math.Round((double)free / total * 100, 1);

                Drives.Add(new DriveShortcut
                {
                    Name = $"{drive.Name.TrimEnd(Path.DirectorySeparatorChar)}",
                    RootPath = rootPath,
                    Summary = $"{FormatDriveSizeInGb(free)}GB / {FormatDriveSizeInGb(total)}GB",
                    UsagePercent = usage,
                });
            }
        }

        private static string FormatDriveSizeInGb(long bytes)
        {
            var gib = bytes / 1024d / 1024d / 1024d;
            return Math.Round(gib, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string NormalizeDriveRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var root = Path.GetPathRoot(path.Trim()) ?? path.Trim();
            return root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
        }

        private string ResolveInitialLeftPath()
        {
            var savedPaths = LoadSavedPanePaths();
            if (!string.IsNullOrWhiteSpace(savedPaths.LeftPath) && IsNavigableDirectoryPath(savedPaths.LeftPath))
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
            if (!string.IsNullOrWhiteSpace(savedPaths.RightPath) && IsNavigableDirectoryPath(savedPaths.RightPath))
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

        internal static bool IsNavigableDirectoryPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return Directory.Exists(path) || TryEnumerateUncServerShares(path, out _);
        }

        internal static bool TryEnumerateUncServerShares(string? path, out IReadOnlyList<string> sharePaths)
        {
            sharePaths = Array.Empty<string>();
            if (!TryGetUncServerName(path, out var serverName))
            {
                return false;
            }

            sharePaths = EnumerateUncServerShares(serverName);
            return sharePaths.Count > 0;
        }

        private static bool TryGetUncServerName(string? path, out string serverName)
        {
            serverName = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var trimmed = path.Trim();
            if (!trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            var normalized = NormalizePath(trimmed);
            if (normalized.Length <= 2)
            {
                return false;
            }

            var segments = normalized[2..]
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length != 1)
            {
                return false;
            }

            serverName = segments[0];
            return !string.IsNullOrWhiteSpace(serverName);
        }

        private static IReadOnlyList<string> EnumerateUncServerShares(string serverName)
        {
            var results = new List<string>();
            nint buffer = nint.Zero;

            try
            {
                var resumeHandle = 0;
                var status = NetShareEnum(
                    $@"\\{serverName}",
                    1,
                    out buffer,
                    -1,
                    out var entriesRead,
                    out _,
                    ref resumeHandle);

                if (status != 0 || buffer == nint.Zero || entriesRead <= 0)
                {
                    return results;
                }

                var current = buffer;
                var itemSize = Marshal.SizeOf<SHARE_INFO_1>();
                for (var index = 0; index < entriesRead; index++)
                {
                    var info = Marshal.PtrToStructure<SHARE_INFO_1>(current);
                    current += itemSize;

                    if (string.IsNullOrWhiteSpace(info.shi1_netname))
                    {
                        continue;
                    }

                    if (info.shi1_type != ShareType.DiskTree)
                    {
                        continue;
                    }

                    results.Add($@"\\{serverName}\{info.shi1_netname}");
                }

                return results
                    .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
            finally
            {
                if (buffer != nint.Zero)
                {
                    NetApiBufferFree(buffer);
                }
            }
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

        private void RefreshPaneAfterLocalChange(PaneViewModel pane)
        {
            GetPaneWatcher(pane).SuppressRefreshFor(PaneWatcherSuppressInterval);
            RefreshPane(pane);
        }

        private PaneDirectoryWatcher GetPaneWatcher(PaneViewModel pane)
        {
            return ReferenceEquals(pane, LeftPane)
                ? _leftPaneWatcher
                : _rightPaneWatcher;
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

            if (!IsNavigableDirectoryPath(requestedPath))
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
            UpdateSharedStatusBar();
        }

        private void UpdateActivePaneVisuals()
        {
            var isLeftActive = ReferenceEquals(_activePane, LeftPane);

            ApplyPaneVisualState(
                LeftPaneBorder,
                LeftPathTextBox,
                isLeftActive);

            ApplyPaneVisualState(
                RightPaneBorder,
                RightPathTextBox,
                !isLeftActive);
        }

        private void ApplyPaneVisualState(
            Border paneBorder,
            TextBox pathTextBox,
            bool isActive)
        {
            var activeBorder = ParseColor("#BF4CFF");
            var inactiveBorder = GetBrushColor("PanelStrokeBrush", "#3A3146");
            var activeFill = GetBrushColor("InputAltBrush", "#231E2B");
            var inactiveFill = GetBrushColor("InputBrush", "#1B1621");

            paneBorder.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBorder);
            paneBorder.BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
            pathTextBox.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBorder);
            pathTextBox.Background = new SolidColorBrush(isActive ? activeFill : inactiveFill);
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

        internal void RefreshAll_Click(object sender, RoutedEventArgs e)
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

        internal async void LeftPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await NavigateToEditablePathAsync(LeftPane, (sender as TextBox)?.Text);
            }
        }

        internal async void RightPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await NavigateToEditablePathAsync(RightPane, (sender as TextBox)?.Text);
            }
        }

        internal void LeftPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        internal void RightPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        internal void LeftPaneContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        internal void RightPaneContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        internal void LeftPaneList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        internal void LeftPaneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncPaneSelectionFromListView(LeftPane, LeftPaneListView);
            ApplySelectionVisuals(LeftPaneListView);
            ScheduleSelectionSizeUpdate(LeftPane);
        }

        internal void RightPaneList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        internal void RightPaneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncPaneSelectionFromListView(RightPane, RightPaneListView);
            ApplySelectionVisuals(RightPaneListView);
            ScheduleSelectionSizeUpdate(RightPane);
        }

        internal void PaneListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem listViewItem)
            {
                ApplySelectionVisualToContainer(listViewItem);
            }
        }

        internal void ClearPaneFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: PaneViewModel pane })
            {
                pane.ClearFilter();
                SyncPaneFilterSelection(pane);
            }
        }

        internal void DriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path })
            {
                OpenInPane(_activePane, path);
            }
        }

        internal void DriveShortcut_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: DriveShortcut drive } element)
            {
                return;
            }

            e.Handled = true;

            var flyout = new MenuFlyout();
            var hideItem = new MenuFlyoutItem
            {
                Text = $"隱藏 {drive.Name}",
                Tag = drive.RootPath,
            };
            hideItem.Click += HideDriveShortcut_Click;
            flyout.Items.Add(hideItem);
            flyout.ShowAt(element);
        }

        private void HideDriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: string rootPath })
            {
                return;
            }

            var normalizedRootPath = NormalizeDriveRootPath(rootPath);
            if (string.IsNullOrWhiteSpace(normalizedRootPath))
            {
                return;
            }

            _hiddenDrivePaths.Add(normalizedRootPath);
            SaveShortcutSettingsSafe();
            LoadDriveCards();
            UpdateDriveSectionMenuState();
        }

        internal void DriveRestoreFlyout_Opening(object sender, object e)
        {
            if (sender is not MenuFlyout flyout)
            {
                return;
            }

            flyout.Items.Clear();

            var hiddenDrives = _hiddenDrivePaths
                .Select(path => new
                {
                    Name = path.TrimEnd(Path.DirectorySeparatorChar),
                    RootPath = path,
                })
                .OrderBy(static drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hiddenDrives.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = "沒有已隱藏的磁碟機",
                    IsEnabled = false,
                });
                return;
            }

            foreach (var drive in hiddenDrives)
            {
                var restoreItem = new MenuFlyoutItem
                {
                    Text = $"恢復 {drive.Name}",
                    Tag = drive.RootPath,
                };
                restoreItem.Click += RestoreDriveShortcut_Click;
                flyout.Items.Add(restoreItem);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());
            var restoreAllItem = new MenuFlyoutItem { Text = "恢復全部磁碟機" };
            restoreAllItem.Click += RestoreAllDriveShortcuts_Click;
            flyout.Items.Add(restoreAllItem);
        }

        private void RestoreDriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: string rootPath })
            {
                return;
            }

            _hiddenDrivePaths.Remove(NormalizeDriveRootPath(rootPath));
            SaveShortcutSettingsSafe();
            LoadDriveCards();
            UpdateDriveSectionMenuState();
        }

        private void RestoreAllDriveShortcuts_Click(object sender, RoutedEventArgs e)
        {
            if (_hiddenDrivePaths.Count == 0)
            {
                return;
            }

            _hiddenDrivePaths.Clear();
            SaveShortcutSettingsSafe();
            LoadDriveCards();
            UpdateDriveSectionMenuState();
        }

        private void UpdateDriveSectionMenuState()
        {
            if (DriveSectionMenuButton is not null)
            {
                DriveSectionMenuButton.Opacity = _hiddenDrivePaths.Count > 0 ? 1 : 0.82;
            }
        }

        private void RefreshSelectionSizeDisplays()
        {
            ScheduleSelectionSizeUpdate(LeftPane, immediate: true);
            ScheduleSelectionSizeUpdate(RightPane, immediate: true);
        }

        private void ScheduleSelectionSizeUpdate(PaneViewModel pane, bool immediate = false)
        {
            var selectedEntries = GetSelectedEntries(pane);
            pane.UpdateSelectionText(BuildSelectionSummary(selectedEntries));

            var timer = ReferenceEquals(pane, LeftPane) ? _leftSelectionSizeTimer : _rightSelectionSizeTimer;
            var cancellationTokenSource = ReferenceEquals(pane, LeftPane)
                ? Interlocked.Exchange(ref _leftSelectionSizeCts, null)
                : Interlocked.Exchange(ref _rightSelectionSizeCts, null);

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();

            timer.Stop();
            if (immediate)
            {
                _ = UpdateSelectionSizeAsync(pane);
                return;
            }

            timer.Start();
        }

        private void LeftSelectionSizeTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            _ = UpdateSelectionSizeAsync(LeftPane);
        }

        private void RightSelectionSizeTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            _ = UpdateSelectionSizeAsync(RightPane);
        }
    }
}
