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
    public sealed class BackupAutomationProfile : ObservableObject
    {
        private string _name = string.Empty;
        private string _sourcePath = string.Empty;
        private string _destinationPath = string.Empty;
        private BackupAutomationMode _mode = BackupAutomationMode.Copy;
        private int _intervalMinutes = 60;
        private string _intervalMinutesText = "60";
        private bool _isEnabled = true;
        private bool _isRunning;
        private string _lastRunText = "尚未執行";
        private string _lastResultText = "等待排程";

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        public string DestinationPath
        {
            get => _destinationPath;
            set => SetProperty(ref _destinationPath, value);
        }

        public BackupAutomationMode Mode
        {
            get => _mode;
            set
            {
                if (SetProperty(ref _mode, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ModeDescription));
                }
            }
        }

        public int IntervalMinutes
        {
            get => _intervalMinutes;
            set
            {
                if (SetProperty(ref _intervalMinutes, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string IntervalMinutesText
        {
            get => _intervalMinutesText;
            set => SetProperty(ref _intervalMinutesText, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanStop));
                    OnPropertyChanged(nameof(StartButtonVisibility));
                    OnPropertyChanged(nameof(StopButtonVisibility));
                }
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(CanEdit));
                    OnPropertyChanged(nameof(CanToggleEnabled));
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanRunNow));
                    OnPropertyChanged(nameof(CanStop));
                    OnPropertyChanged(nameof(CanDelete));
                    OnPropertyChanged(nameof(CardOpacity));
                    OnPropertyChanged(nameof(StartButtonVisibility));
                    OnPropertyChanged(nameof(RunButtonVisibility));
                    OnPropertyChanged(nameof(StopButtonVisibility));
                    OnPropertyChanged(nameof(DeleteButtonVisibility));
                }
            }
        }

        public string LastRunText
        {
            get => _lastRunText;
            set => SetProperty(ref _lastRunText, value);
        }

        public string LastResultText
        {
            get => _lastResultText;
            set => SetProperty(ref _lastResultText, value);
        }

        public string ModeDescription => Mode == BackupAutomationMode.Mirror
            ? "同步鏡像：以來源 watcher 即時觸發同步，並依間隔做全量校正；目的地會刪除多餘項目。"
            : "備份複製：只覆蓋同名檔案，保留目的地既有的其他項目。";

        public string StatusText => IsRunning
            ? (Mode == BackupAutomationMode.Mirror ? "目前正在執行背景同步" : "目前正在執行背景備份")
            : Mode == BackupAutomationMode.Mirror
                ? $"{(IsEnabled ? "已啟用" : "已停用")} / 同步鏡像 / 即時監聽 + 每 {IntervalMinutes} 分鐘校正"
                : $"{(IsEnabled ? "已啟用" : "已停用")} / 備份複製 / 每 {IntervalMinutes} 分鐘";

        public bool CanEdit => !IsRunning;

        public bool CanToggleEnabled => !IsRunning;

        public bool CanStart => !IsRunning && !IsEnabled;

        public bool CanRunNow => !IsRunning;

        public bool CanStop => IsRunning || IsEnabled;

        public bool CanDelete => !IsRunning;

        public double CardOpacity => IsRunning ? 0.78 : 1d;

        public Visibility StartButtonVisibility => !IsRunning && !IsEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RunButtonVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;

        public Visibility StopButtonVisibility => IsRunning || IsEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DeleteButtonVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;

        public void SyncIntervalText()
        {
            IntervalMinutesText = IntervalMinutes.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public sealed class AutoExtractProfile : ObservableObject
    {
        private string _name = string.Empty;
        private string _watchPath = string.Empty;
        private string _extractorPath = string.Empty;
        private string _pendingPasswordText = string.Empty;
        private bool _isEnabled = true;
        private bool _isRunning;
        private string _lastRunText = "尚未執行";
        private string _lastResultText = "監看待命";

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string WatchPath
        {
            get => _watchPath;
            set => SetProperty(ref _watchPath, value);
        }

        public string ExtractorPath
        {
            get => _extractorPath;
            set => SetProperty(ref _extractorPath, value);
        }

        public string PendingPasswordText
        {
            get => _pendingPasswordText;
            set => SetProperty(ref _pendingPasswordText, value);
        }

        public ObservableCollection<AutoExtractPasswordItem> Passwords { get; } = new();

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(CanEdit));
                    OnPropertyChanged(nameof(CanToggleEnabled));
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanRunNow));
                    OnPropertyChanged(nameof(CanStop));
                    OnPropertyChanged(nameof(CanDelete));
                    OnPropertyChanged(nameof(StartButtonVisibility));
                    OnPropertyChanged(nameof(StopButtonVisibility));
                }
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(CanEdit));
                    OnPropertyChanged(nameof(CanToggleEnabled));
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanRunNow));
                    OnPropertyChanged(nameof(CanStop));
                    OnPropertyChanged(nameof(CanDelete));
                    OnPropertyChanged(nameof(CardOpacity));
                    OnPropertyChanged(nameof(StartButtonVisibility));
                    OnPropertyChanged(nameof(RunButtonVisibility));
                    OnPropertyChanged(nameof(StopButtonVisibility));
                    OnPropertyChanged(nameof(DeleteButtonVisibility));
                }
            }
        }

        public string LastRunText
        {
            get => _lastRunText;
            set => SetProperty(ref _lastRunText, value);
        }

        public string LastResultText
        {
            get => _lastResultText;
            set => SetProperty(ref _lastResultText, value);
        }

        public string StatusText => IsRunning
            ? "目前正在執行背景解壓"
            : IsEnabled
                ? "已啟用 / 即時監看 .zip .rar .7z"
                : "已停用";

        public bool CanEdit => !IsRunning;

        public bool CanToggleEnabled => !IsRunning;

        public bool CanStart => !IsRunning && !IsEnabled;

        public bool CanRunNow => !IsRunning;

        public bool CanStop => IsRunning || IsEnabled;

        public bool CanDelete => !IsRunning;

        public double CardOpacity => IsRunning ? 0.78 : 1d;

        public Visibility StartButtonVisibility => !IsRunning && !IsEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RunButtonVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;

        public Visibility StopButtonVisibility => IsRunning || IsEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DeleteButtonVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed class AutoExtractExecutionResult
    {
        public string StatusText { get; set; } = string.Empty;

        public bool ShouldShowPasswordMismatchDialog { get; set; }

        public string PasswordMismatchDialogMessage { get; set; } = string.Empty;

        public static AutoExtractExecutionResult Create(
            string statusText,
            bool shouldShowPasswordMismatchDialog = false,
            string passwordMismatchDialogMessage = "")
        {
            return new AutoExtractExecutionResult
            {
                StatusText = statusText,
                ShouldShowPasswordMismatchDialog = shouldShowPasswordMismatchDialog,
                PasswordMismatchDialogMessage = passwordMismatchDialogMessage,
            };
        }
    }

    public sealed class AutoExtractPasswordItem : ObservableObject
    {
        private string _value = string.Empty;

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        [JsonIgnore]
        public AutoExtractProfile? ParentProfile { get; set; }
    }
}
