using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace nuone_tools
{
    public sealed partial class MainWindow : Window
    {
        private const uint FO_MOVE = 0x0001;
        private const uint FO_COPY = 0x0002;
        private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
        private const string AutomationOwnerMutexName = @"Local\nuone-tools-automation-owner";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public nint hwnd;
            public uint wFunc;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;

            public ushort fFlags;

            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;

            public nint hNameMappings;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszProgressTitle;
        }

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        private static readonly SolidColorBrush SelectedItemBackgroundBrush = new(ColorHelper.FromArgb(255, 91, 20, 126));
        private static readonly SolidColorBrush SelectedItemBorderBrush = new(ColorHelper.FromArgb(255, 140, 60, 188));
        private static readonly SolidColorBrush UnselectedItemBackgroundBrush = new(Colors.Transparent);
        private static readonly SolidColorBrush UnselectedItemBorderBrush = new(Colors.Transparent);
        private static readonly Thickness SelectedItemBorderThickness = new(1);
        private static readonly Thickness UnselectedItemBorderThickness = new(1);
        private static readonly string ConfigDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nuone-tools",
            "config");
        internal static readonly string DefaultLogDirectoryPath = Path.Combine(ConfigDirectoryPath, "logs");
        private static readonly string LegacyGroupsConfigPath = Path.Combine(ConfigDirectoryPath, "groups.json");
        private static readonly string SettingsSyncConfigPath = Path.Combine(ConfigDirectoryPath, "settings-sync.json");
        private static readonly string SettingsLocalConfigPath = Path.Combine(ConfigDirectoryPath, "settings-local.json");
        private static readonly string LocalNotificationHistoryPath = Path.Combine(ConfigDirectoryPath, "local-notification-history.json");
        private static readonly string SyncNotificationHistoryPath = Path.Combine(ConfigDirectoryPath, "sync-notification-history.json");
        private static readonly HttpClient SharedHttpClient = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static string _currentLogDirectoryPath = DefaultLogDirectoryPath;
        private static readonly TimeSpan PaneWatcherDebounceInterval = TimeSpan.FromMilliseconds(450);
        private static readonly TimeSpan PaneWatcherSuppressInterval = TimeSpan.FromMilliseconds(1200);
        private static readonly TimeSpan SelectionSizeDebounceInterval = TimeSpan.FromMilliseconds(180);
        private static readonly TimeSpan AutomationWatcherDebounceInterval = TimeSpan.FromMilliseconds(900);
        private static readonly HashSet<string> SupportedArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".rar",
            ".7z",
        };
        private PaneViewModel _activePane;
        private readonly DispatcherQueueTimer _selectionFlyoutTimer;
        private readonly DispatcherQueueTimer _leftSelectionSizeTimer;
        private readonly DispatcherQueueTimer _rightSelectionSizeTimer;
        private readonly PaneDirectoryWatcher _leftPaneWatcher;
        private readonly PaneDirectoryWatcher _rightPaneWatcher;
        private FrameworkElement? _pendingFlyoutTarget;
        private PaneViewModel? _pendingFlyoutPane;
        private string? _pendingFlyoutPath;
        private ShortcutSettings _shortcutSettings = ShortcutSettings.CreateDefault();
        private ShortcutSettings _editingShortcutSettings = ShortcutSettings.CreateDefault();
        private AccountSettingsState _accountSettings = AccountSettingsState.CreateDefault();
        private FileBunkerSettingsState _fileBunkerSettings = FileBunkerSettingsState.CreateDefault();
        private LoggingSettingsState _loggingSettings = LoggingSettingsState.CreateDefault();
        private AppUpdateState _appUpdateState = AppUpdateState.CreateDefault();
        private string _lastLocalBackupText = "尚未備份";
        private AppSection _activeSection = AppSection.FileManager;
        private SettingsSection _activeSettingsSection = SettingsSection.General;
        private ShortcutCaptureTarget _settingsCaptureTarget = ShortcutCaptureTarget.None;
        private bool _isSettingsDialogOpen;
        private bool _isUpdatingSettingsUi;
        private bool _isUpdatingAccountUi;
        private bool _isUpdatingFileBunkerUi;
        private bool _isUpdatingLoggingUi;
        private bool _isClosingForAppUpdate;
        private bool _isAccountLoginRunning;
        private bool _isAccountReloginFieldsVisible;
        private CancellationTokenSource? _leftSelectionSizeCts;
        private CancellationTokenSource? _rightSelectionSizeCts;
        private readonly Dictionary<Guid, System.Timers.Timer> _automationTimers = new();
        private readonly Dictionary<Guid, BackupAutomationSourceWatcher> _automationWatchers = new();
        private readonly Dictionary<Guid, CancellationTokenSource> _automationCancellationTokens = new();
        private readonly HashSet<Guid> _runningAutomationIds = new();
        private readonly Dictionary<Guid, AutoExtractProfileWatcher> _autoExtractWatchers = new();
        private readonly Dictionary<Guid, CancellationTokenSource> _autoExtractCancellationTokens = new();
        private readonly HashSet<Guid> _runningAutoExtractIds = new();
        private Mutex? _automationOwnerMutex;
        private bool _isAutomationExecutionOwner;
        private DispatcherQueueTimer? _automationOwnershipRetryTimer;
        private readonly object _backgroundWorkLock = new();
        private readonly object _notificationHistoryLock = new();
        private readonly object _syncSettingsUploadLock = new();
        private readonly object _localSettingsUploadLock = new();
        private readonly Dictionary<Guid, BackgroundWorkState> _backgroundWorks = new();
        private readonly List<BackgroundWorkRecord> _backgroundWorkRecords = new();
        private readonly List<NotificationHistoryRecord> _localNotificationHistory = new();
        private readonly List<NotificationHistoryRecord> _syncNotificationHistory = new();
        private readonly HashSet<string> _hiddenDrivePaths = new(StringComparer.OrdinalIgnoreCase);
        private bool _isSyncSettingsUploadWorkerRunning;
        private bool _isApplyingRemoteSyncSettings;
        private string? _pendingSyncSettingsUploadJson;
        private bool _isLocalSettingsUploadWorkerRunning;
        private bool _isApplyingRemoteLocalSettings;
        private string? _pendingLocalSettingsUploadJson;
        private TerminalTabSession? _selectedTerminalTab;
        private int _nextTerminalTabNumber = 1;

        internal static string CurrentLogDirectoryPath => NormalizeLogDirectoryPath(_currentLogDirectoryPath);

        internal static string NormalizeLogDirectoryPath(string? path)
        {
            var candidate = string.IsNullOrWhiteSpace(path) ? DefaultLogDirectoryPath : path.Trim();

            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
            }
            catch
            {
                return DefaultLogDirectoryPath;
            }
        }

        internal static string ResolveConfiguredLogDirectoryPath()
        {
            try
            {
                if (!File.Exists(SettingsLocalConfigPath))
                {
                    return DefaultLogDirectoryPath;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(SettingsLocalConfigPath));
                return TryReadConfiguredLogDirectoryPath(document.RootElement, out var logDirectoryPath)
                    ? NormalizeLogDirectoryPath(logDirectoryPath)
                    : DefaultLogDirectoryPath;
            }
            catch
            {
                return DefaultLogDirectoryPath;
            }
        }

        internal static void ApplyConfiguredLogDirectoryPath(string? path)
        {
            _currentLogDirectoryPath = NormalizeLogDirectoryPath(path);
            AppLogging.Configure(_currentLogDirectoryPath);
        }

        private static bool TryReadConfiguredLogDirectoryPath(JsonElement root, out string? logDirectoryPath)
        {
            logDirectoryPath = null;

            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetSettingsProperty(root, "logging", nameof(LocalSettingsConfig.Logging), out var loggingProperty) ||
                loggingProperty.ValueKind != JsonValueKind.Object ||
                !TryGetSettingsProperty(loggingProperty, "logDirectoryPath", nameof(LoggingSettingsConfig.LogDirectoryPath), out var logDirectoryPathProperty) ||
                logDirectoryPathProperty.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            logDirectoryPath = logDirectoryPathProperty.GetString();
            return !string.IsNullOrWhiteSpace(logDirectoryPath);
        }

        private static bool TryGetSettingsProperty(JsonElement element, string camelName, string pascalName, out JsonElement value)
        {
            if (element.TryGetProperty(camelName, out value) ||
                element.TryGetProperty(pascalName, out value))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, camelName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, pascalName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        public string AppVersionText { get; } = GetAppVersionText();

        public ObservableCollection<DriveShortcut> Drives { get; } = new();

        public ObservableCollection<PathGroup> CustomGroups { get; } = new();

        public ObservableCollection<BackupAutomationProfile> BackupAutomations { get; } = new();

        public ObservableCollection<AutoExtractProfile> AutoExtractProfiles { get; } = new();

        public ObservableCollection<ToolbarCommandItem> ToolbarCommands { get; } = new();

        public ObservableCollection<TerminalTabSession> TerminalTabs { get; } = new();

        public PaneViewModel LeftPane { get; } = new("左側");

        public PaneViewModel RightPane { get; } = new("右側");

        public MainWindow()
        {
            AppLogging.Information("MainWindow constructor start");
            InitializeComponent();
            AppLogging.Information("MainWindow InitializeComponent completed");

            FileManagerPage.Owner = this;
            AutomationPage.Owner = this;
            SettingsPage.Owner = this;
            TerminalPage.Owner = this;

            _activePane = LeftPane;
            _leftPaneWatcher = new PaneDirectoryWatcher(LeftPane, DispatcherQueue, RefreshPane, PaneWatcherDebounceInterval);
            _rightPaneWatcher = new PaneDirectoryWatcher(RightPane, DispatcherQueue, RefreshPane, PaneWatcherDebounceInterval);
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TopTitleBar);
            TrySetWindowIcon();

            ApplyInitialWindowPlacement();
            ConfigureTitleBarInsets();
            RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootLayout_KeyDown), true);

            SeedSidebar();
            LoadCustomGroups();
            LoadShortcutSettings();
            LoadAccountSettings();
            LoadFileBunkerSettings();
            LoadLoggingSettings();
            WindowsNotificationService.Initialize();
            LoadBackupAutomations();
            LoadAutoExtractProfiles();
            LoadToolbarCommands();
            LoadNotificationHistories();
            InitializeAutomationExecutionOwnership();
            ApplyThemePreference();
            ApplySettingsToPanes();
            LoadDriveCards();
            UpdateDriveSectionMenuState();
            ResetEditableShortcutSettings();
            UpdateAccountSettingsUi();
            UpdateFileBunkerSettingsUi();
            UpdateLoggingSettingsUi();
            UpdateAppUpdateUi();
            RescheduleBackupAutomations();
            RescheduleAutoExtractProfiles();

            var leftDefault = ResolveInitialLeftPath();
            var rightDefault = ResolveInitialRightPath(leftDefault);

            LeftPane.PropertyChanged += Pane_PropertyChanged;
            RightPane.PropertyChanged += Pane_PropertyChanged;
            Activated += MainWindow_Activated;
            Closed += MainWindow_Closed;

            OpenInPane(LeftPane, leftDefault);
            OpenInPane(RightPane, rightDefault);
            _activePane = LeftPane;
            UpdateActivePaneVisuals();
            UpdateAppSectionVisuals();
            UpdateSettingsSectionVisuals();
            UpdateTerminalUi();
            UpdateSharedStatusBar();

            _selectionFlyoutTimer = DispatcherQueue.CreateTimer();
            _selectionFlyoutTimer.Interval = TimeSpan.FromMilliseconds(225);
            _selectionFlyoutTimer.IsRepeating = false;
            _selectionFlyoutTimer.Tick += SelectionFlyoutTimer_Tick;

            _leftSelectionSizeTimer = DispatcherQueue.CreateTimer();
            _leftSelectionSizeTimer.Interval = SelectionSizeDebounceInterval;
            _leftSelectionSizeTimer.IsRepeating = false;
            _leftSelectionSizeTimer.Tick += LeftSelectionSizeTimer_Tick;

            _rightSelectionSizeTimer = DispatcherQueue.CreateTimer();
            _rightSelectionSizeTimer.Interval = SelectionSizeDebounceInterval;
            _rightSelectionSizeTimer.IsRepeating = false;
            _rightSelectionSizeTimer.Tick += RightSelectionSizeTimer_Tick;

            _terminalCursorTimer = DispatcherQueue.CreateTimer();
            InitializeTerminalCursorTimer();

            RunFireAndForget(InitializeRemoteSyncSettingsAsync(), "startup remote settings sync");
            RunFireAndForget(CheckForUpdatesAsync(isAutomatic: true), "startup app update check");
            AppLogging.Information(
                "MainWindow constructor completed ActiveSection={ActiveSection} LeftPath={LeftPath} RightPath={RightPath}",
                _activeSection,
                LeftPane.CurrentPath,
                RightPane.CurrentPath);
        }

        private static string GetAppVersionText()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            if (version is null)
            {
                return "1.0.0";
            }

            if (version.Build >= 0 && version.Revision >= 0)
            {
                return $"{version.Build}.{version.Revision}";
            }

            if (version.Build >= 0)
            {
                return version.Build.ToString(CultureInfo.InvariantCulture);
            }

            return $"{version.Major}.{version.Minor}";
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            AppLogging.Information("MainWindow closing");
            SavePanePathsSafe();
            StopAllAutomationTimers();
            StopAllAutomationWatchers();
            StopAllAutoExtractWatchers();
            CancelAllAutoExtractOperations();
            _leftSelectionSizeTimer.Stop();
            _leftSelectionSizeTimer.Tick -= LeftSelectionSizeTimer_Tick;
            _rightSelectionSizeTimer.Stop();
            _rightSelectionSizeTimer.Tick -= RightSelectionSizeTimer_Tick;
            _terminalCursorTimer?.Stop();
            if (_terminalCursorTimer is not null)
            {
                _terminalCursorTimer.Tick -= TerminalCursorTimer_Tick;
            }
            _leftSelectionSizeCts?.Cancel();
            _leftSelectionSizeCts?.Dispose();
            _rightSelectionSizeCts?.Cancel();
            _rightSelectionSizeCts?.Dispose();
            StopAllTerminalProcesses();
            LeftPane.PropertyChanged -= Pane_PropertyChanged;
            RightPane.PropertyChanged -= Pane_PropertyChanged;
            if (_automationOwnershipRetryTimer is not null)
            {
                _automationOwnershipRetryTimer.Stop();
                _automationOwnershipRetryTimer.Tick -= AutomationOwnershipRetryTimer_Tick;
                _automationOwnershipRetryTimer = null;
            }
            ReleaseAutomationExecutionOwnership();
            Activated -= MainWindow_Activated;
            Closed -= MainWindow_Closed;
            _leftPaneWatcher.Dispose();
            _rightPaneWatcher.Dispose();
            WindowsNotificationService.Uninitialize();
            AppLogging.Flush();
        }

        internal bool IsAutomationExecutionOwner => _isAutomationExecutionOwner;

        internal bool EnsureAutomationExecutionOwner(string actionDescription)
        {
            if (TryPromoteToAutomationExecutionOwner())
            {
                return true;
            }

            RunFireAndForget(
                ShowMessageAsync(
                    "自動化由另一個視窗執行中",
                    $"目前只有持有自動化執行權的 nuone-tools 視窗可以{actionDescription}。"),
                "automation ownership required");
            return false;
        }

        private void InitializeAutomationExecutionOwnership()
        {
            TryAcquireAutomationExecutionOwnership();
            _automationOwnershipRetryTimer = DispatcherQueue.CreateTimer();
            _automationOwnershipRetryTimer.Interval = TimeSpan.FromSeconds(5);
            _automationOwnershipRetryTimer.IsRepeating = true;
            _automationOwnershipRetryTimer.Tick += AutomationOwnershipRetryTimer_Tick;
            UpdateAutomationOwnershipRetryState();
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated || _isAutomationExecutionOwner)
            {
                return;
            }

            if (TryPromoteToAutomationExecutionOwner())
            {
                UpdateSharedStatusBar();
            }
        }

        private void AutomationOwnershipRetryTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (_isAutomationExecutionOwner)
            {
                sender.Stop();
                return;
            }

            if (TryPromoteToAutomationExecutionOwner())
            {
                UpdateSharedStatusBar();
            }
        }

        private bool TryPromoteToAutomationExecutionOwner()
        {
            var acquired = TryAcquireAutomationExecutionOwnership();
            if (acquired)
            {
                RescheduleBackupAutomations();
                RescheduleAutoExtractProfiles();
            }

            UpdateAutomationOwnershipRetryState();
            return acquired;
        }

        private bool TryAcquireAutomationExecutionOwnership()
        {
            if (_isAutomationExecutionOwner)
            {
                return true;
            }

            try
            {
                var mutex = new Mutex(initiallyOwned: false, AutomationOwnerMutexName);
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(0, false);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    mutex.Dispose();
                    return false;
                }

                _automationOwnerMutex = mutex;
                _isAutomationExecutionOwner = true;
                AppLogging.Information("Automation execution ownership acquired.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogging.Error(ex, "Automation execution ownership acquisition failed.");
                return false;
            }
        }

        private void ReleaseAutomationExecutionOwnership()
        {
            var mutex = _automationOwnerMutex;
            _automationOwnerMutex = null;
            if (mutex is null)
            {
                _isAutomationExecutionOwner = false;
                return;
            }

            try
            {
                if (_isAutomationExecutionOwner)
                {
                    mutex.ReleaseMutex();
                }
            }
            catch (ApplicationException)
            {
            }
            finally
            {
                mutex.Dispose();
                _isAutomationExecutionOwner = false;
            }
        }

        private void UpdateAutomationOwnershipRetryState()
        {
            if (_automationOwnershipRetryTimer is null)
            {
                return;
            }

            if (_isAutomationExecutionOwner)
            {
                _automationOwnershipRetryTimer.Stop();
                return;
            }

            _automationOwnershipRetryTimer.Stop();
            _automationOwnershipRetryTimer.Start();
        }

        private Task EnqueueOnUiAsync(Action action)
        {
            var completion = new TaskCompletionSource<object?>();
            var queued = DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.SetResult(null);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            if (!queued)
            {
                completion.SetException(new InvalidOperationException("UI dispatcher queue rejected the action."));
            }

            return completion.Task;
        }

        internal static void LogBoundaryException(Exception ex, string boundary)
        {
            try
            {
                AppLogging.Error(
                    ex,
                    "Unhandled boundary exception Boundary={Boundary} Type={ExceptionType} HResult=0x{HResult:X8}",
                    boundary,
                    ex.GetType().FullName ?? ex.GetType().Name,
                    ex.HResult);
            }
            catch
            {
            }
        }

        private static void RunSafely(Action action, string boundary)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, boundary);
            }
        }

        private void TryEnqueueUi(Action action, string boundary)
        {
            if (!DispatcherQueue.TryEnqueue(() => RunSafely(action, boundary)))
            {
                AppLogging.Warning("UI dispatcher queue rejected action Boundary={Boundary}", boundary);
            }
        }

        private void RunFireAndForget(Task task, string boundary)
        {
            _ = ObserveFireAndForgetAsync(task, boundary);
        }

        private static async Task ObserveFireAndForgetAsync(Task task, string boundary)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, boundary);
            }
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

        private static string FormatCreateFolderShortcutKey(Windows.System.VirtualKey key)
        {
            return $"Ctrl + Shift + {FormatShortcutKey(key)}";
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

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetShareEnum(
            string servername,
            int level,
            out nint bufptr,
            int prefmaxlen,
            out int entriesread,
            out int totalentries,
            ref int resume_handle);

        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(nint buffer);
    }
}
