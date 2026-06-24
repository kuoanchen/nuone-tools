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
    public sealed class PaneTabItem : ObservableObject
    {
        private string _title = "新分頁";
        private string _path = string.Empty;
        private string _iconGlyph = "\uE8B7";
        private bool _isSelected;
        private double _tabWidth = 132;
        private Brush _backgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 34, 28, 41));
        private Brush _borderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 58, 49, 70));
        private Brush _foregroundBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 206, 197, 223));

        internal Stack<string> History { get; } = new();

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

        public string IconGlyph
        {
            get => _iconGlyph;
            set => SetProperty(ref _iconGlyph, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public double TabWidth
        {
            get => _tabWidth;
            set => SetProperty(ref _tabWidth, value);
        }

        public Brush BackgroundBrush
        {
            get => _backgroundBrush;
            set => SetProperty(ref _backgroundBrush, value);
        }

        public Brush BorderBrush
        {
            get => _borderBrush;
            set => SetProperty(ref _borderBrush, value);
        }

        public Brush ForegroundBrush
        {
            get => _foregroundBrush;
            set => SetProperty(ref _foregroundBrush, value);
        }
    }

    public sealed class PaneViewModel : ObservableObject
    {
        private readonly List<FileEntry> _allItems = new();
        private readonly Brush _selectedTabBackgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 91, 20, 126));
        private readonly Brush _selectedTabBorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 140, 60, 188));
        private readonly Brush _selectedTabForegroundBrush = new SolidColorBrush(Colors.White);
        private readonly Brush _unselectedTabBackgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 34, 28, 41));
        private readonly Brush _unselectedTabBorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 58, 49, 70));
        private readonly Brush _unselectedTabForegroundBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 206, 197, 223));
        private readonly List<FileEntry> _selectedEntries = new();
        private string _currentPath = string.Empty;
        private string _editablePath = string.Empty;
        private string _statusText = "尚未載入";
        private string _summaryText = string.Empty;
        private string _selectionText = string.Empty;
        private int _selectedCount;
        private bool _showHiddenSystemItems;
        private FileEntry? _selectedItem;
        private string _filterQuery = string.Empty;
        private string _filterModeText = string.Empty;
        private bool _isLoading;
        private string _loadingText = "載入中...";
        private int _loadRequestVersion;
        private int _overlayRequestVersion;
        private PaneTabItem? _selectedTab;
        private double _lastTabStripAvailableWidth = 520;

        public PaneViewModel(string name)
        {
            Name = name;
            var initialTab = new PaneTabItem();
            Tabs.Add(initialTab);
            ApplyTabSelection(initialTab);
        }

        public string Name { get; }

        public ObservableCollection<FileEntry> Items { get; } = new();

        public ObservableCollection<PaneTabItem> Tabs { get; } = new();

        public PaneTabItem? SelectedTab
        {
            get => _selectedTab;
            private set => SetProperty(ref _selectedTab, value);
        }

        public string FilterQuery
        {
            get => _filterQuery;
            private set => SetProperty(ref _filterQuery, value);
        }

        public string FilterModeText
        {
            get => _filterModeText;
            private set => SetProperty(ref _filterModeText, value);
        }

        public Visibility FilterVisibility => string.IsNullOrWhiteSpace(FilterQuery)
            ? Visibility.Collapsed
            : Visibility.Visible;

        public bool HasActiveFilter => !string.IsNullOrWhiteSpace(FilterQuery);

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public Visibility LoadingVisibility => IsLoading
            ? Visibility.Visible
            : Visibility.Collapsed;

        public string LoadingText
        {
            get => _loadingText;
            private set => SetProperty(ref _loadingText, value);
        }

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

        public void UpdateSelectionText(string text)
        {
            SelectionText = text;
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

            foreach (var item in EnumerateDisplayEntries())
            {
                item.IsSelected = selectedPaths.Contains(MainWindow.NormalizePath(item.FullPath));
            }

            _selectedEntries.Clear();
            _selectedEntries.AddRange(selectedItems);
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

            MainWindow.AppendDebugLog(
                "pane-selection-debug.log",
                $"Pane.UpdateSelection pane={Name} currentPath={CurrentPath} selectedCount={selectedItems.Count} primary={primary?.FullPath ?? "<null>"}");
        }

        public IReadOnlyList<FileEntry> GetTrackedSelectedEntries()
        {
            return _selectedEntries.ToArray();
        }

        public IEnumerable<FileEntry> EnumerateDisplayEntries()
        {
            foreach (var item in Items)
            {
                foreach (var nested in item.EnumerateTreeEntries())
                {
                    yield return nested;
                }
            }
        }

        public void NavigateTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            Load(path);
        }

        public void LoadTabPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            Load(path, rememberCurrent: false);
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

            if (MainWindow.IsSshPath(CurrentPath))
            {
                NavigateTo(MainWindow.GetSshParentPath(CurrentPath));
                return;
            }

            if (MainWindow.IsWslVirtualRootPath(CurrentPath))
            {
                Load(CurrentPath, rememberCurrent: false);
                return;
            }

            if (MainWindow.IsWslPath(CurrentPath))
            {
                var trimmedPath = MainWindow.NormalizePath(CurrentPath);
                var segments = trimmedPath[2..]
                    .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length == 2)
                {
                    NavigateTo($@"\\{segments[0]}");
                    return;
                }
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
            var history = SelectedTab?.History;
            if (history is null)
            {
                return;
            }

            while (history.Count > 0)
            {
                var previous = history.Pop();
                if (Directory.Exists(previous) ||
                    MainWindow.IsSshPath(previous) ||
                    MainWindow.TryEnumerateUncServerShares(previous, out _))
                {
                    Load(previous, rememberCurrent: false);
                    return;
                }
            }
        }

        public PaneTabItem AddTab(string initialPath)
        {
            var tab = new PaneTabItem
            {
                Path = NormalizeTabPath(initialPath),
            };
            tab.Title = BuildTabTitle(tab.Path);
            Tabs.Add(tab);
            ApplyTabSelection(tab);
            UpdateTabLayout(_lastTabStripAvailableWidth);
            return tab;
        }

        public string? ActivateTab(PaneTabItem? tab)
        {
            if (tab is null || !Tabs.Contains(tab))
            {
                return null;
            }

            ApplyTabSelection(tab);
            EditablePath = string.IsNullOrWhiteSpace(tab.Path) ? CurrentPath : tab.Path;
            if (string.IsNullOrWhiteSpace(tab.Path) || MainWindow.PathEquals(CurrentPath, tab.Path))
            {
                return null;
            }

            return tab.Path;
        }

        public bool CloseTab(PaneTabItem? tab, out string? pathToLoad)
        {
            pathToLoad = null;
            if (tab is null || !Tabs.Contains(tab) || Tabs.Count <= 1)
            {
                return false;
            }

            var closingIndex = Tabs.IndexOf(tab);
            var wasSelected = ReferenceEquals(SelectedTab, tab);
            Tabs.Remove(tab);
            UpdateTabLayout(_lastTabStripAvailableWidth);

            if (!wasSelected)
            {
                return true;
            }

            var nextIndex = Math.Min(closingIndex, Tabs.Count - 1);
            var nextTab = Tabs[nextIndex];
            ApplyTabSelection(nextTab);
            pathToLoad = string.IsNullOrWhiteSpace(nextTab.Path) ? null : nextTab.Path;
            return true;
        }

        public void AppendFilterCharacter(char character)
        {
            FilterQuery += character;
            ApplyFilter();
        }

        public void RemoveLastFilterCharacter()
        {
            if (string.IsNullOrEmpty(FilterQuery))
            {
                return;
            }

            FilterQuery = FilterQuery[..^1];
            ApplyFilter();
        }

        public void ClearFilter()
        {
            if (string.IsNullOrEmpty(FilterQuery))
            {
                return;
            }

            FilterQuery = string.Empty;
            ApplyFilter();
        }

        public int BeginLoad(string path, string loadingText)
        {
            _loadRequestVersion++;
            _overlayRequestVersion++;
            EditablePath = path;
            LoadingText = loadingText;
            IsLoading = true;
            OnPropertyChanged(nameof(LoadingVisibility));
            MainWindow.AppendSshDebugLog(
                $"Pane.BeginLoad pane={Name} path={path} requestVersion={_loadRequestVersion} loadingText={loadingText}");
            return _loadRequestVersion;
        }

        public void ApplyLoadedEntries(string path, IReadOnlyList<FileEntry> entries, bool rememberCurrent, int requestVersion)
        {
            if (requestVersion != _loadRequestVersion)
            {
                MainWindow.AppendSshDebugLog(
                    $"Pane.ApplyLoadedEntries skipped pane={Name} path={path} requestVersion={requestVersion} currentVersion={_loadRequestVersion}");
                return;
            }

            if (rememberCurrent && !string.IsNullOrWhiteSpace(CurrentPath) && !MainWindow.PathEquals(CurrentPath, path))
            {
                SelectedTab?.History.Push(CurrentPath);
            }

            CurrentPath = path;
            EditablePath = CurrentPath;
            SyncSelectedTabWithCurrentPath();
            _allItems.Clear();
            _allItems.AddRange(entries);
            FilterQuery = string.Empty;
            ApplyFilter();
            IsLoading = false;
            OnPropertyChanged(nameof(LoadingVisibility));
            MainWindow.AppendSshDebugLog(
                $"Pane.ApplyLoadedEntries applied pane={Name} path={path} requestVersion={requestVersion} entries={entries.Count} rememberCurrent={rememberCurrent}");
        }

        public void ApplyLoadError(string path, string message, int requestVersion)
        {
            if (requestVersion != _loadRequestVersion)
            {
                MainWindow.AppendSshDebugLog(
                    $"Pane.ApplyLoadError skipped pane={Name} path={path} requestVersion={requestVersion} currentVersion={_loadRequestVersion} message={message}");
                return;
            }

            _allItems.Clear();
            Items.Clear();
            FilterQuery = string.Empty;
            FilterModeText = string.Empty;
            OnPropertyChanged(nameof(FilterVisibility));
            UpdateSelection(Array.Empty<FileEntry>());
            CurrentPath = path;
            EditablePath = path;
            SyncSelectedTabWithCurrentPath();
            StatusText = "無法載入";
            SummaryText = message;
            IsLoading = false;
            OnPropertyChanged(nameof(LoadingVisibility));
            MainWindow.AppendSshDebugLog(
                $"Pane.ApplyLoadError applied pane={Name} path={path} requestVersion={requestVersion} message={message}");
        }

        public int BeginOverlay(string loadingText)
        {
            _overlayRequestVersion++;
            LoadingText = loadingText;
            IsLoading = true;
            OnPropertyChanged(nameof(LoadingVisibility));
            MainWindow.AppendSshDebugLog(
                $"Pane.BeginOverlay pane={Name} overlayVersion={_overlayRequestVersion} loadingText={loadingText}");
            return _overlayRequestVersion;
        }

        public void EndOverlay(int overlayVersion)
        {
            if (overlayVersion != _overlayRequestVersion)
            {
                MainWindow.AppendSshDebugLog(
                    $"Pane.EndOverlay skipped pane={Name} overlayVersion={overlayVersion} currentOverlayVersion={_overlayRequestVersion}");
                return;
            }

            IsLoading = false;
            OnPropertyChanged(nameof(LoadingVisibility));
            MainWindow.AppendSshDebugLog(
                $"Pane.EndOverlay applied pane={Name} overlayVersion={overlayVersion}");
        }

        private void Load(string path, bool rememberCurrent = true)
        {
            _loadRequestVersion++;
            IsLoading = false;
            OnPropertyChanged(nameof(LoadingVisibility));
            MainWindow.AppendSshDebugLog(
                $"Pane.Load start pane={Name} path={path} rememberCurrent={rememberCurrent} version={_loadRequestVersion}");

            try
            {
                if (MainWindow.IsSshPath(path))
                {
                    LoadSshDirectory(path, rememberCurrent);
                    return;
                }

                if (MainWindow.TryEnumerateWslDistributions(path, out var distributionPaths))
                {
                    LoadWslRoot(path, distributionPaths, rememberCurrent);
                    return;
                }

                if (MainWindow.TryEnumerateUncServerShares(path, out var sharePaths))
                {
                    LoadUncServerRoot(path, sharePaths, rememberCurrent);
                    return;
                }

                var directory = new DirectoryInfo(path);
                if (!directory.Exists)
                {
                    StatusText = "路徑不存在";
                    return;
                }

                if (rememberCurrent && !string.IsNullOrWhiteSpace(CurrentPath) && !MainWindow.PathEquals(CurrentPath, path))
                {
                    SelectedTab?.History.Push(CurrentPath);
                }

                CurrentPath = directory.FullName;
                EditablePath = CurrentPath;
                SyncSelectedTabWithCurrentPath();
                _allItems.Clear();

                foreach (var folder in SafeEnumerateDirectories(directory, ShowHiddenSystemItems))
                {
                    _allItems.Add(FileEntry.FromDirectory(folder));
                }

                foreach (var file in SafeEnumerateFiles(directory, ShowHiddenSystemItems))
                {
                    _allItems.Add(FileEntry.FromFile(file));
                }

                FilterQuery = string.Empty;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                _allItems.Clear();
                Items.Clear();
                FilterQuery = string.Empty;
                FilterModeText = string.Empty;
                OnPropertyChanged(nameof(FilterVisibility));
                UpdateSelection(Array.Empty<FileEntry>());
                CurrentPath = path;
                EditablePath = path;
                StatusText = "無法載入";
                SummaryText = ex.Message;
                MainWindow.AppendSshDebugLog(
                    $"Pane.Load exception pane={Name} path={path} version={_loadRequestVersion} error={ex}");
            }
        }

        private void LoadSshDirectory(string path, bool rememberCurrent)
        {
            if (!MainWindow.TryLoadSshDirectory(path, out var normalizedPath, out var entries, out var errorMessage))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorMessage)
                    ? "無法載入遠端 Linux 目錄。"
                    : errorMessage);
            }

            ApplyLoadedEntries(normalizedPath, entries, rememberCurrent, _loadRequestVersion);
        }

        private void LoadWslRoot(string path, IReadOnlyList<string> distributionPaths, bool rememberCurrent)
        {
            if (rememberCurrent && !string.IsNullOrWhiteSpace(CurrentPath) && !MainWindow.PathEquals(CurrentPath, path))
            {
                SelectedTab?.History.Push(CurrentPath);
            }

            CurrentPath = MainWindow.NormalizePath(path);
            EditablePath = CurrentPath;
            SyncSelectedTabWithCurrentPath();
            _allItems.Clear();

            foreach (var distributionPath in distributionPaths)
            {
                _allItems.Add(FileEntry.FromNetworkShare(distributionPath));
            }

            FilterQuery = string.Empty;
            ApplyFilter();
        }

        private void LoadUncServerRoot(string path, IReadOnlyList<string> sharePaths, bool rememberCurrent)
        {
            if (rememberCurrent && !string.IsNullOrWhiteSpace(CurrentPath) && !MainWindow.PathEquals(CurrentPath, path))
            {
                SelectedTab?.History.Push(CurrentPath);
            }

            CurrentPath = MainWindow.NormalizePath(path);
            EditablePath = CurrentPath;
            SyncSelectedTabWithCurrentPath();
            _allItems.Clear();

            foreach (var sharePath in sharePaths)
            {
                _allItems.Add(FileEntry.FromNetworkShare(sharePath));
            }

            FilterQuery = string.Empty;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = FilterQuery.Trim();
            IEnumerable<FileEntry> source = _allItems;
            IEnumerable<FileEntry> filtered = source;

            if (!string.IsNullOrWhiteSpace(query))
            {
                var startsWithMatches = source
                    .Where(item => item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (startsWithMatches.Count > 0)
                {
                    filtered = startsWithMatches;
                    FilterModeText = $"開頭為：{query}";
                }
                else
                {
                    filtered = source
                        .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    FilterModeText = $"包含：{query}";
                }
            }
            else
            {
                FilterModeText = string.Empty;
            }

            ReplaceVisibleItems(filtered);
            OnPropertyChanged(nameof(FilterVisibility));

            var defaultSelection = Items.FirstOrDefault();
            MainWindow.AppendDebugLog(
                "pane-selection-debug.log",
                $"Pane.ApplyFilter pane={Name} currentPath={CurrentPath} visibleItems={Items.Count} allItems={_allItems.Count} filter={query} defaultSelection={defaultSelection?.FullPath ?? "<null>"}");
            UpdateSelection(defaultSelection is null ? Array.Empty<FileEntry>() : new[] { defaultSelection });
            UpdateSummaryTexts();
        }

        private void ReplaceVisibleItems(IEnumerable<FileEntry> entries)
        {
            Items.Clear();
            foreach (var entry in entries)
            {
                Items.Add(entry);
            }
        }

        private void UpdateSummaryTexts()
        {
            StatusText = $"{Items.Count} 個項目";

            if (_allItems.Count > 0 && Items.All(item => item.Kind == "分享"))
            {
                SummaryText = $"{Items.Count} 個分享";
                return;
            }

            SummaryText = $"{Items.Count(static item => item.IsDirectory)} 資料夾 / {Items.Count(static item => !item.IsDirectory)} 檔案";
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
            return attributes.HasFlag(System.IO.FileAttributes.Hidden)
                || attributes.HasFlag(System.IO.FileAttributes.System)
                || attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
        }

        private void ApplyTabSelection(PaneTabItem selectedTab)
        {
            SelectedTab = selectedTab;
            foreach (var tab in Tabs)
            {
                var isSelected = ReferenceEquals(tab, selectedTab);
                tab.IsSelected = isSelected;
                tab.BackgroundBrush = isSelected ? _selectedTabBackgroundBrush : _unselectedTabBackgroundBrush;
                tab.BorderBrush = isSelected ? _selectedTabBorderBrush : _unselectedTabBorderBrush;
                tab.ForegroundBrush = isSelected ? _selectedTabForegroundBrush : _unselectedTabForegroundBrush;
            }
        }

        public void UpdateTabLayout(double availableWidth)
        {
            if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
            {
                return;
            }

            _lastTabStripAvailableWidth = availableWidth;
            var count = Math.Max(Tabs.Count, 1);
            const double itemGap = 4;
            const double maxTabWidth = 132;
            const double minTabWidth = 28;
            var computedWidth = Math.Floor((availableWidth - ((count - 1) * itemGap)) / count);
            var tabWidth = Math.Max(minTabWidth, Math.Min(maxTabWidth, computedWidth));

            foreach (var tab in Tabs)
            {
                tab.TabWidth = tabWidth;
            }
        }

        private void SyncSelectedTabWithCurrentPath()
        {
            if (SelectedTab is null)
            {
                return;
            }

            SelectedTab.Path = NormalizeTabPath(CurrentPath);
            SelectedTab.Title = BuildTabTitle(CurrentPath);
            SelectedTab.IconGlyph = BuildTabIconGlyph(CurrentPath);
        }

        private static string NormalizeTabPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        }

        private static string BuildTabTitle(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "新分頁";
            }

            if (MainWindow.TryParseSshPath(path, out var connection, out var remotePath))
            {
                var sshName = Path.GetFileName(remotePath.TrimEnd('/'));
                return string.IsNullOrWhiteSpace(sshName) ? connection : sshName;
            }

            var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return normalized;
        }

        private static string BuildTabIconGlyph(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "\uE8B7";
            }

            if (MainWindow.IsSshPath(path))
            {
                return "\uE128";
            }

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return "\uE968";
            }

            return "\uE7C3";
        }
    }
}
