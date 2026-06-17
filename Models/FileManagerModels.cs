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
    public sealed class BatchRenamePreviewItem
    {
        private static readonly SolidColorBrush SuccessBrush = new(ColorHelper.FromArgb(255, 116, 216, 155));
        private static readonly SolidColorBrush ErrorBrush = new(ColorHelper.FromArgb(255, 255, 117, 117));

        public BatchRenamePreviewItem(FileEntry entry, int number, string newName, string statusText, bool canApply)
        {
            Entry = entry;
            Number = number.ToString(CultureInfo.InvariantCulture);
            OriginalName = entry.Name;
            NewName = newName;
            StatusText = statusText;
            CanApply = canApply;
            StatusBrush = canApply ? SuccessBrush : ErrorBrush;
        }

        public FileEntry Entry { get; }

        public string Number { get; }

        public string OriginalName { get; }

        public string NewName { get; }

        public string StatusText { get; }

        public bool CanApply { get; }

        public SolidColorBrush StatusBrush { get; }
    }

    public sealed class FileEntry : ObservableObject
    {
        private static readonly SolidColorBrush UnselectedBackgroundBrush = new(Colors.Transparent);
        private static readonly SolidColorBrush SelectedBackgroundBrush = new(ColorHelper.FromArgb(255, 91, 20, 126));
        private static readonly SolidColorBrush UnselectedBorderBrush = new(Colors.Transparent);
        private static readonly SolidColorBrush SelectedBorderBrush = new(ColorHelper.FromArgb(255, 140, 60, 188));
        private static readonly Thickness UnselectedBorderThickness = new(1);
        private static readonly Thickness SelectedBorderThickness = new(1);
        private bool _isSelected;
        private ImageSource? _iconImageSource;
        private Task? _iconLoadTask;

        public string Name { get; set; } = string.Empty;

        public string FullPath { get; set; } = string.Empty;

        public bool IsDirectory { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string ModifiedText { get; set; } = string.Empty;

        public string SizeText { get; set; } = string.Empty;

        public string Glyph { get; set; } = "\uE8B7";

        public SolidColorBrush AccentColor { get; set; } = new(Colors.Gold);

        public ImageSource? IconImageSource
        {
            get => _iconImageSource;
            private set
            {
                if (SetProperty(ref _iconImageSource, value))
                {
                    OnPropertyChanged(nameof(ImageIconVisibility));
                    OnPropertyChanged(nameof(GlyphIconVisibility));
                }
            }
        }

        public Visibility ImageIconVisibility => IconImageSource is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        public Visibility GlyphIconVisibility => IconImageSource is null
            ? Visibility.Visible
            : Visibility.Collapsed;

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

        public static FileEntry FromNetworkShare(string sharePath)
        {
            var visual = FileVisual.ForDirectory();
            var isWslDistribution = MainWindow.IsWslDistributionPath(sharePath);

            return new FileEntry
            {
                Name = MainWindow.GetDisplayName(sharePath),
                FullPath = sharePath,
                IsDirectory = true,
                Kind = isWslDistribution ? "Linux" : "分享",
                ModifiedText = "--",
                SizeText = "--",
                Glyph = visual.Glyph,
                AccentColor = visual.Brush,
            };
        }

        public static FileEntry FromRemoteEntry(
            string name,
            string fullPath,
            bool isDirectory,
            string modifiedText,
            long? sizeBytes)
        {
            var visual = isDirectory
                ? FileVisual.ForDirectory()
                : FileVisual.ForFile(Path.GetExtension(name));

            return new FileEntry
            {
                Name = name,
                FullPath = fullPath,
                IsDirectory = isDirectory,
                Kind = isDirectory
                    ? "Linux"
                    : (string.IsNullOrWhiteSpace(Path.GetExtension(name))
                        ? "檔案"
                        : Path.GetExtension(name).TrimStart('.').ToUpperInvariant()),
                ModifiedText = string.IsNullOrWhiteSpace(modifiedText) ? "--" : modifiedText,
                SizeText = isDirectory
                    ? "--"
                    : (sizeBytes.HasValue ? MainWindow.FormatSize(sizeBytes.Value) : "--"),
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

        public Task EnsureShellIconAsync()
        {
            if (IsDirectory || string.IsNullOrWhiteSpace(FullPath))
            {
                return Task.CompletedTask;
            }

            return _iconLoadTask ??= LoadShellIconAsync();
        }

        private async Task LoadShellIconAsync()
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(FullPath);
                using var thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.ListView,
                    32,
                    ThumbnailOptions.UseCurrentScale);

                if (thumbnail is null || thumbnail.Size == 0)
                {
                    return;
                }

                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(thumbnail);
                IconImageSource = bitmap;
            }
            catch
            {
                // Keep the extension-based glyph when Windows cannot provide an icon.
            }
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

        public double UsagePercent { get; set; }
    }

    public sealed class ToolbarCommandItem : ObservableObject
    {
        public const string DefaultGlyph = "\uE756";

        private string _title = string.Empty;
        private string _command = string.Empty;
        private string _iconPath = string.Empty;
        private string _iconGlyph = DefaultGlyph;
        private string _nodeDockerUser = string.Empty;
        private string _nodeDockerHost = string.Empty;
        private string _nodeDockerRemoteDirectory = string.Empty;
        private NodeDockerLaunchMode _nodeDockerLaunchMode = NodeDockerLaunchMode.ExternalWindow;
        private TerminalShellKind _terminalShellKind = TerminalShellKind.PowerShell;
        private ToolbarWorkingDirectoryMode _terminalWorkingDirectoryMode = ToolbarWorkingDirectoryMode.ActivePane;
        private string _terminalCustomWorkingDirectory = string.Empty;
        private string _terminalLaunchArguments = string.Empty;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Command
        {
            get => _command;
            set => SetProperty(ref _command, value);
        }

        public string IconPath
        {
            get => _iconPath;
            set
            {
                if (SetProperty(ref _iconPath, value))
                {
                    OnPropertyChanged(nameof(HasImageIcon));
                    OnPropertyChanged(nameof(IconImageSource));
                    OnPropertyChanged(nameof(ImageIconVisibility));
                    OnPropertyChanged(nameof(GlyphIconVisibility));
                    OnPropertyChanged(nameof(IconSummary));
                }
            }
        }

        public string IconGlyph
        {
            get => _iconGlyph;
            set
            {
                if (SetProperty(ref _iconGlyph, value))
                {
                    OnPropertyChanged(nameof(DisplayGlyph));
                    OnPropertyChanged(nameof(GlyphIconVisibility));
                    OnPropertyChanged(nameof(IconSummary));
                }
            }
        }

        public string NodeDockerUser
        {
            get => _nodeDockerUser;
            set => SetProperty(ref _nodeDockerUser, value);
        }

        public string NodeDockerHost
        {
            get => _nodeDockerHost;
            set => SetProperty(ref _nodeDockerHost, value);
        }

        public string NodeDockerRemoteDirectory
        {
            get => _nodeDockerRemoteDirectory;
            set => SetProperty(ref _nodeDockerRemoteDirectory, value);
        }

        public NodeDockerLaunchMode NodeDockerLaunchMode
        {
            get => _nodeDockerLaunchMode;
            set => SetProperty(ref _nodeDockerLaunchMode, value);
        }

        public TerminalShellKind TerminalShellKind
        {
            get => _terminalShellKind;
            set => SetProperty(ref _terminalShellKind, value);
        }

        public ToolbarWorkingDirectoryMode TerminalWorkingDirectoryMode
        {
            get => _terminalWorkingDirectoryMode;
            set => SetProperty(ref _terminalWorkingDirectoryMode, value);
        }

        public string TerminalCustomWorkingDirectory
        {
            get => _terminalCustomWorkingDirectory;
            set => SetProperty(ref _terminalCustomWorkingDirectory, value);
        }

        public string TerminalLaunchArguments
        {
            get => _terminalLaunchArguments;
            set => SetProperty(ref _terminalLaunchArguments, value);
        }

        [JsonIgnore]
        public string DisplayGlyph => string.IsNullOrWhiteSpace(IconGlyph) ? DefaultGlyph : IconGlyph;

        [JsonIgnore]
        public bool HasImageIcon => CreateIconImageSource(IconPath) is not null || IsExecutableIconSource(IconPath);

        [JsonIgnore]
        public ImageSource? IconImageSource => CreateIconImageSource(IconPath);

        [JsonIgnore]
        public Visibility ImageIconVisibility => HasImageIcon ? Visibility.Visible : Visibility.Collapsed;

        [JsonIgnore]
        public Visibility GlyphIconVisibility => HasImageIcon ? Visibility.Collapsed : Visibility.Visible;

        [JsonIgnore]
        public string IconSummary => HasImageIcon
            ? $"圖示檔案：{IconPath}"
            : $"Glyph：{DisplayGlyph}";

        public static ImageSource? CreateIconImageSource(string? iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return null;
            }

            try
            {
                var normalizedPath = Path.GetFullPath(iconPath.Trim());
                if (!File.Exists(normalizedPath))
                {
                    return null;
                }

                var extension = Path.GetExtension(normalizedPath);
                var uri = new Uri(normalizedPath, UriKind.Absolute);

                if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
                {
                    return new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(uri);
                }

                if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase))
                {
                    return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
                }
            }
            catch
            {
            }

            return null;
        }

        public static bool IsExecutableIconSource(string? iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return false;
            }

            try
            {
                var extension = Path.GetExtension(iconPath.Trim());
                return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<ImageSource?> CreateShellIconImageSourceAsync(string? iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return null;
            }

            try
            {
                var normalizedPath = Path.GetFullPath(iconPath.Trim());
                if (!File.Exists(normalizedPath))
                {
                    return null;
                }

                var file = await StorageFile.GetFileFromPathAsync(normalizedPath);
                using var thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    64,
                    ThumbnailOptions.UseCurrentScale);

                if (thumbnail is null || thumbnail.Size == 0)
                {
                    return null;
                }

                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(thumbnail);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
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
}
