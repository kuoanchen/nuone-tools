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
    public sealed class PaneViewModel : ObservableObject
    {
        private readonly Stack<string> _history = new();
        private readonly List<FileEntry> _allItems = new();
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

        public PaneViewModel(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public ObservableCollection<FileEntry> Items { get; } = new();

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
                if (Directory.Exists(previous) || MainWindow.TryEnumerateUncServerShares(previous, out _))
                {
                    Load(previous, rememberCurrent: false);
                    return;
                }
            }
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

        private void Load(string path, bool rememberCurrent = true)
        {
            try
            {
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
                    _history.Push(CurrentPath);
                }

                CurrentPath = directory.FullName;
                EditablePath = CurrentPath;
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
            }
        }

        private void LoadUncServerRoot(string path, IReadOnlyList<string> sharePaths, bool rememberCurrent)
        {
            if (rememberCurrent && !string.IsNullOrWhiteSpace(CurrentPath) && !MainWindow.PathEquals(CurrentPath, path))
            {
                _history.Push(CurrentPath);
            }

            CurrentPath = MainWindow.NormalizePath(path);
            EditablePath = CurrentPath;
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
    }
}
