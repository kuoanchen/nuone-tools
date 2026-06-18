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
        private AutomationJobType _jobType = AutomationJobType.FileBackup;
        private string _sourcePath = string.Empty;
        private string _destinationPath = string.Empty;
        private BackupAutomationMode _mode = BackupAutomationMode.Copy;
        private string _excludedFolderNamesText = string.Empty;
        private string _logDirectoryPath = string.Empty;
        private string _mongoToolPath = @"C:\Program Files\MongoDB\Tools\100\bin\mongodump.exe";
        private string _mongoConnectionString = string.Empty;
        private string _mongoDatabaseName = string.Empty;
        private bool _mongoUseGzip = true;
        private bool _mongoUseArchive = true;
        private int _mongoRetentionCount = 7;
        private string _mongoRetentionCountText = "7";
        private AutomationScheduleType _scheduleType = AutomationScheduleType.Interval;
        private int _intervalMinutes = 60;
        private string _intervalMinutesText = "60";
        private string _scheduleTimeText = "03:00";
        private int _weeklyDaysMask = 62;
        private bool _runMissedOnStartup;
        private bool _isEnabled = true;
        private bool _isRunning;
        private string _lastRunText = "尚未執行";
        private string _lastResultText = "等待排程";
        private string _nextRunText = "尚未排程";

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public AutomationJobType JobType
        {
            get => _jobType;
            set
            {
                if (SetProperty(ref _jobType, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(JobTypeText));
                    OnPropertyChanged(nameof(ModeDescription));
                    OnPropertyChanged(nameof(UsesFilePaths));
                    OnPropertyChanged(nameof(UsesMongoSettings));
                    OnPropertyChanged(nameof(SourcePathVisibility));
                    OnPropertyChanged(nameof(FileModeRowVisibility));
                    OnPropertyChanged(nameof(MongoSettingsVisibility));
                    OnPropertyChanged(nameof(WarningText));
                }
            }
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
                    OnPropertyChanged(nameof(JobTypeText));
                }
            }
        }

        public string ExcludedFolderNamesText
        {
            get => _excludedFolderNamesText;
            set => SetProperty(ref _excludedFolderNamesText, value);
        }

        public string LogDirectoryPath
        {
            get => _logDirectoryPath;
            set => SetProperty(ref _logDirectoryPath, value);
        }

        public string MongoToolPath
        {
            get => _mongoToolPath;
            set => SetProperty(ref _mongoToolPath, value);
        }

        public string MongoConnectionString
        {
            get => _mongoConnectionString;
            set => SetProperty(ref _mongoConnectionString, value);
        }

        public string MongoDatabaseName
        {
            get => _mongoDatabaseName;
            set => SetProperty(ref _mongoDatabaseName, value);
        }

        public bool MongoUseGzip
        {
            get => _mongoUseGzip;
            set => SetProperty(ref _mongoUseGzip, value);
        }

        public bool MongoUseArchive
        {
            get => _mongoUseArchive;
            set => SetProperty(ref _mongoUseArchive, value);
        }

        public int MongoRetentionCount
        {
            get => _mongoRetentionCount;
            set => SetProperty(ref _mongoRetentionCount, value);
        }

        public string MongoRetentionCountText
        {
            get => _mongoRetentionCountText;
            set => SetProperty(ref _mongoRetentionCountText, value);
        }

        public int IntervalMinutes
        {
            get => _intervalMinutes;
            set
            {
                if (SetProperty(ref _intervalMinutes, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ScheduleDescription));
                }
            }
        }

        public string IntervalMinutesText
        {
            get => _intervalMinutesText;
            set => SetProperty(ref _intervalMinutesText, value);
        }

        public AutomationScheduleType ScheduleType
        {
            get => _scheduleType;
            set
            {
                if (SetProperty(ref _scheduleType, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ScheduleDescription));
                    OnPropertyChanged(nameof(IntervalScheduleVisibility));
                    OnPropertyChanged(nameof(ScheduledTimeVisibility));
                    OnPropertyChanged(nameof(WeeklyDaysVisibility));
                    OnPropertyChanged(nameof(IsIntervalSchedule));
                    OnPropertyChanged(nameof(IsDailySchedule));
                    OnPropertyChanged(nameof(IsWeeklySchedule));
                }
            }
        }

        public string ScheduleTimeText
        {
            get => _scheduleTimeText;
            set
            {
                if (SetProperty(ref _scheduleTimeText, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ScheduleDescription));
                }
            }
        }

        public int WeeklyDaysMask
        {
            get => _weeklyDaysMask;
            set
            {
                if (SetProperty(ref _weeklyDaysMask, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ScheduleDescription));
                    OnPropertyChanged(nameof(WeeklyMondaySelected));
                    OnPropertyChanged(nameof(WeeklyTuesdaySelected));
                    OnPropertyChanged(nameof(WeeklyWednesdaySelected));
                    OnPropertyChanged(nameof(WeeklyThursdaySelected));
                    OnPropertyChanged(nameof(WeeklyFridaySelected));
                    OnPropertyChanged(nameof(WeeklySaturdaySelected));
                    OnPropertyChanged(nameof(WeeklySundaySelected));
                }
            }
        }

        public bool RunMissedOnStartup
        {
            get => _runMissedOnStartup;
            set => SetProperty(ref _runMissedOnStartup, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StateText));
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
                    OnPropertyChanged(nameof(StateText));
                    OnPropertyChanged(nameof(RunningIndicatorVisibility));
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

        public string NextRunText
        {
            get => _nextRunText;
            set => SetProperty(ref _nextRunText, value);
        }

        public string ModeDescription => JobType == AutomationJobType.MongoBackup
            ? "MongoDB 備份：依排程呼叫 mongodump，輸出 archive 或資料夾，並依保留份數清理舊備份。"
            : Mode == BackupAutomationMode.Mirror
                ? "同步鏡像：以來源 watcher 即時觸發同步，並依間隔做全量校正；目的地會刪除多餘項目。"
                : "備份複製：只覆蓋同名檔案，保留目的地既有的其他項目。";

        public string StatusText => IsRunning
            ? JobType == AutomationJobType.MongoBackup
                ? "目前正在執行 MongoDB 備份"
                : (Mode == BackupAutomationMode.Mirror ? "目前正在執行背景同步" : "目前正在執行背景備份")
            : JobType == AutomationJobType.MongoBackup
                ? $"{(IsEnabled ? "已啟用" : "已停用")} / MongoDB 備份 / {ScheduleDescription}"
                : Mode == BackupAutomationMode.Mirror
                    ? $"{(IsEnabled ? "已啟用" : "已停用")} / 同步鏡像 / 即時監聽 + {ScheduleDescription}"
                    : $"{(IsEnabled ? "已啟用" : "已停用")} / 備份複製 / {ScheduleDescription}";

        public string JobTypeText => JobType == AutomationJobType.MongoBackup
            ? "MongoDB 備份"
            : Mode == BackupAutomationMode.Mirror
                ? "同步鏡像"
                : "檔案備份";

        public string StateText => IsRunning ? "執行中" : IsEnabled ? "已啟用" : "已停用";

        public Visibility RunningIndicatorVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

        public string ScheduleDescription => ScheduleType switch
        {
            AutomationScheduleType.Daily => $"每天 {ScheduleTimeText}",
            AutomationScheduleType.Weekly => $"每週 {GetWeeklyDaysSummary()} {ScheduleTimeText}",
            _ => $"每 {IntervalMinutes} 分鐘",
        };

        public bool UsesFilePaths => JobType == AutomationJobType.FileBackup;

        public bool UsesMongoSettings => JobType == AutomationJobType.MongoBackup;

        public Visibility SourcePathVisibility => UsesFilePaths ? Visibility.Visible : Visibility.Collapsed;

        public Visibility FileModeRowVisibility => UsesFilePaths ? Visibility.Visible : Visibility.Collapsed;

        public Visibility MongoSettingsVisibility => UsesMongoSettings ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IntervalScheduleVisibility => ScheduleType == AutomationScheduleType.Interval ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ScheduledTimeVisibility => ScheduleType == AutomationScheduleType.Interval ? Visibility.Collapsed : Visibility.Visible;

        public Visibility WeeklyDaysVisibility => ScheduleType == AutomationScheduleType.Weekly ? Visibility.Visible : Visibility.Collapsed;

        public bool IsIntervalSchedule => ScheduleType == AutomationScheduleType.Interval;

        public bool IsDailySchedule => ScheduleType == AutomationScheduleType.Daily;

        public bool IsWeeklySchedule => ScheduleType == AutomationScheduleType.Weekly;

        public string WarningText => JobType == AutomationJobType.MongoBackup
            ? "MongoDB 備份會呼叫 mongodump，建議使用 archive + gzip，並設定保留份數避免目的地持續膨脹。"
            : "同步鏡像會讓目的地維持和來源一致，會刪除目的地多出來的檔案與資料夾。";

        public bool WeeklyMondaySelected
        {
            get => IsWeekdaySelected(DayOfWeek.Monday);
            set => SetWeekdaySelected(DayOfWeek.Monday, value);
        }

        public bool WeeklyTuesdaySelected
        {
            get => IsWeekdaySelected(DayOfWeek.Tuesday);
            set => SetWeekdaySelected(DayOfWeek.Tuesday, value);
        }

        public bool WeeklyWednesdaySelected
        {
            get => IsWeekdaySelected(DayOfWeek.Wednesday);
            set => SetWeekdaySelected(DayOfWeek.Wednesday, value);
        }

        public bool WeeklyThursdaySelected
        {
            get => IsWeekdaySelected(DayOfWeek.Thursday);
            set => SetWeekdaySelected(DayOfWeek.Thursday, value);
        }

        public bool WeeklyFridaySelected
        {
            get => IsWeekdaySelected(DayOfWeek.Friday);
            set => SetWeekdaySelected(DayOfWeek.Friday, value);
        }

        public bool WeeklySaturdaySelected
        {
            get => IsWeekdaySelected(DayOfWeek.Saturday);
            set => SetWeekdaySelected(DayOfWeek.Saturday, value);
        }

        public bool WeeklySundaySelected
        {
            get => IsWeekdaySelected(DayOfWeek.Sunday);
            set => SetWeekdaySelected(DayOfWeek.Sunday, value);
        }

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
            OnPropertyChanged(nameof(ScheduleDescription));
        }

        public void SyncMongoRetentionText()
        {
            MongoRetentionCountText = MongoRetentionCount.ToString(CultureInfo.InvariantCulture);
        }

        public bool IsWeekdaySelected(DayOfWeek dayOfWeek)
        {
            return (WeeklyDaysMask & GetWeekdayMask(dayOfWeek)) != 0;
        }

        public void SetWeekdaySelected(DayOfWeek dayOfWeek, bool isSelected)
        {
            var dayMask = GetWeekdayMask(dayOfWeek);
            var nextMask = isSelected
                ? WeeklyDaysMask | dayMask
                : WeeklyDaysMask & ~dayMask;
            WeeklyDaysMask = nextMask == 0 ? dayMask : nextMask;
        }

        private string GetWeeklyDaysSummary()
        {
            var selectedDays = new List<string>();
            if (WeeklyMondaySelected) selectedDays.Add("一");
            if (WeeklyTuesdaySelected) selectedDays.Add("二");
            if (WeeklyWednesdaySelected) selectedDays.Add("三");
            if (WeeklyThursdaySelected) selectedDays.Add("四");
            if (WeeklyFridaySelected) selectedDays.Add("五");
            if (WeeklySaturdaySelected) selectedDays.Add("六");
            if (WeeklySundaySelected) selectedDays.Add("日");

            return selectedDays.Count == 0 ? "一" : string.Join("、", selectedDays);
        }

        private static int GetWeekdayMask(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => 1 << 1,
                DayOfWeek.Tuesday => 1 << 2,
                DayOfWeek.Wednesday => 1 << 3,
                DayOfWeek.Thursday => 1 << 4,
                DayOfWeek.Friday => 1 << 5,
                DayOfWeek.Saturday => 1 << 6,
                DayOfWeek.Sunday => 1 << 0,
                _ => 0,
            };
        }
    }

    public sealed class AutoExtractProfile : ObservableObject
    {
        private string _name = string.Empty;
        private string _watchPath = string.Empty;
        private string _extractorPath = string.Empty;
        private string _extensionFilter = ".zip, .rar, .7z";
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

        public string ExtensionFilter
        {
            get => _extensionFilter;
            set
            {
                if (SetProperty(ref _extensionFilter, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(TriggerText));
                }
            }
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
                    OnPropertyChanged(nameof(StateText));
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
                    OnPropertyChanged(nameof(StateText));
                    OnPropertyChanged(nameof(RunningIndicatorVisibility));
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
                ? $"已啟用 / 即時監看 {MainWindow.GetAutoExtractExtensionSummary(ExtensionFilter)}"
                : "已停用";

        public string StateText => IsRunning ? "執行中" : IsEnabled ? "已啟用" : "已停用";

        public Visibility RunningIndicatorVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

        public string TriggerText => $"監看 {MainWindow.GetAutoExtractExtensionSummary(ExtensionFilter)}";

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
