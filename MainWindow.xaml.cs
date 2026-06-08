using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace nuone_tools
{
    public sealed partial class MainWindow : Window
    {
        private PaneViewModel _activePane;

        public ObservableCollection<DriveShortcut> Drives { get; } = new();

        public ObservableCollection<QuickAccessItem> QuickAccessItems { get; } = new();

        public ObservableCollection<StringEntry> RecentLocations { get; } = new();

        public PaneViewModel LeftPane { get; } = new("左側");

        public PaneViewModel RightPane { get; } = new("右側");

        public MainWindow()
        {
            InitializeComponent();

            _activePane = LeftPane;
            ExtendsContentIntoTitleBar = true;

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1680, 980));

            SeedSidebar();
            LoadDriveCards();

            var leftDefault = ResolveInitialLeftPath();
            var rightDefault = ResolveInitialRightPath(leftDefault);

            LeftPane.NavigateTo(leftDefault);
            RightPane.NavigateTo(rightDefault);
            PushRecent(leftDefault);
            PushRecent(rightDefault);
        }

        private void SeedSidebar()
        {
            QuickAccessItems.Clear();
            RecentLocations.Clear();

            AddQuickAccess("桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "\uE8B7");
            AddQuickAccess("文件", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\uE8A5");
            AddQuickAccess("下載", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "\uE896");
            AddQuickAccess("工作區", Environment.CurrentDirectory, "\uE8F1");
            AddQuickAccess("使用者目錄", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "\uE77B");
        }

        private void AddQuickAccess(string title, string path, string glyph)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            QuickAccessItems.Add(new QuickAccessItem
            {
                Title = title,
                Caption = path,
                Path = path,
                Glyph = glyph,
            });
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
            if (Directory.Exists(Environment.CurrentDirectory))
            {
                return Environment.CurrentDirectory;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private string ResolveInitialRightPath(string leftPath)
        {
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

        private static string NormalizePath(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void OpenInPane(PaneViewModel pane, string path)
        {
            pane.NavigateTo(path);
            _activePane = pane;
            PushRecent(path);
        }

        private void PushRecent(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var existing = RecentLocations.FirstOrDefault(entry => PathEquals(entry.Value, path));
            if (existing is not null)
            {
                RecentLocations.Remove(existing);
            }

            RecentLocations.Insert(0, new StringEntry { Value = path });

            while (RecentLocations.Count > 6)
            {
                RecentLocations.RemoveAt(RecentLocations.Count - 1);
            }
        }

        private void RefreshPane(PaneViewModel pane)
        {
            pane.Refresh();
            LoadDriveCards();
        }

        private void NavigateUp(PaneViewModel pane)
        {
            pane.NavigateUp();
            PushRecent(pane.CurrentPath);
        }

        private void NavigateBack(PaneViewModel pane)
        {
            pane.GoBack();
            PushRecent(pane.CurrentPath);
        }

        private void ActivatePane(PaneViewModel pane)
        {
            _activePane = pane;
        }

        private void OpenSelectedInExplorer()
        {
            var entry = _activePane.SelectedItem;
            if (entry is null)
            {
                return;
            }

            var target = entry.FullPath;
            if (Directory.Exists(target) || File.Exists(target))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true,
                });
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

        private void DriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path })
            {
                OpenInPane(_activePane, path);
            }
        }

        private void QuickAccess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path })
            {
                OpenInPane(_activePane, path);
            }
        }

        private void LeftPane_ItemClick(object sender, ItemClickEventArgs e)
        {
            ActivatePane(LeftPane);
            HandleItemClick(LeftPane, e.ClickedItem as FileEntry);
        }

        private void RightPane_ItemClick(object sender, ItemClickEventArgs e)
        {
            ActivatePane(RightPane);
            HandleItemClick(RightPane, e.ClickedItem as FileEntry);
        }

        private void HandleItemClick(PaneViewModel pane, FileEntry? entry)
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

        private void OpenSelected_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedInExplorer();
        }

        private void SwapPanes_Click(object sender, RoutedEventArgs e)
        {
            var leftPath = LeftPane.CurrentPath;
            var rightPath = RightPane.CurrentPath;

            LeftPane.NavigateTo(rightPath);
            RightPane.NavigateTo(leftPath);

            PushRecent(LeftPane.CurrentPath);
            PushRecent(RightPane.CurrentPath);
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
        private string _statusText = "尚未載入";
        private string _summaryText = string.Empty;
        private string _selectionText = string.Empty;
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

        public FileEntry? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    SelectionText = value is null ? "未選取" : value.Name;
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
                Items.Clear();

                foreach (var folder in SafeEnumerateDirectories(directory))
                {
                    Items.Add(FileEntry.FromDirectory(folder));
                }

                foreach (var file in SafeEnumerateFiles(directory))
                {
                    Items.Add(FileEntry.FromFile(file));
                }

                SelectedItem = Items.FirstOrDefault();
                StatusText = $"{Items.Count} 個項目";
                SummaryText = $"{Items.Count(static item => item.IsDirectory)} 資料夾 / {Items.Count(static item => !item.IsDirectory)} 檔案";
            }
            catch (Exception ex)
            {
                Items.Clear();
                SelectedItem = null;
                CurrentPath = path;
                StatusText = "無法載入";
                SummaryText = ex.Message;
            }
        }

        private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo directory)
        {
            try
            {
                return directory.EnumerateDirectories().OrderBy(static item => item.Name).ToArray();
            }
            catch
            {
                return Array.Empty<DirectoryInfo>();
            }
        }

        private static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo directory)
        {
            try
            {
                return directory.EnumerateFiles().OrderBy(static item => item.Name).ToArray();
            }
            catch
            {
                return Array.Empty<FileInfo>();
            }
        }
    }

    public sealed class FileEntry
    {
        public string Name { get; set; } = string.Empty;

        public string FullPath { get; set; } = string.Empty;

        public bool IsDirectory { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string ModifiedText { get; set; } = string.Empty;

        public string SizeText { get; set; } = string.Empty;

        public string Glyph { get; set; } = "\uE8B7";

        public SolidColorBrush AccentColor { get; set; } = new(Colors.Gold);

        public static FileEntry FromDirectory(DirectoryInfo directory)
        {
            return new FileEntry
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                Kind = "資料夾",
                ModifiedText = directory.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                SizeText = "--",
                Glyph = "\uE8B7",
                AccentColor = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 212, 106)),
            };
        }

        public static FileEntry FromFile(FileInfo file)
        {
            return new FileEntry
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                Kind = string.IsNullOrWhiteSpace(file.Extension) ? "檔案" : file.Extension.TrimStart('.').ToUpperInvariant(),
                ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                SizeText = MainWindow.FormatSize(file.Length),
                Glyph = "\uE8A5",
                AccentColor = new SolidColorBrush(ColorHelper.FromArgb(255, 116, 216, 155)),
            };
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

    public sealed class QuickAccessItem
    {
        public string Title { get; set; } = string.Empty;

        public string Caption { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string Glyph { get; set; } = string.Empty;
    }

    public sealed class StringEntry
    {
        public string Value { get; set; } = string.Empty;
    }

    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
