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
        private const string SyncSettingsMutexName = @"Local\nuone-tools-settings-sync";
        private const string LocalSettingsMutexName = @"Local\nuone-tools-settings-local";
        private const string NotificationHistoryMutexName = @"Local\nuone-tools-notification-history";

        private void LoadShortcutSettings()
        {
            _shortcutSettings = ShortcutSettings.CreateDefault();

            if (!File.Exists(SettingsSyncConfigPath))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(SettingsSyncConfigPath));
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

                document.RootElement.TryGetProperty("createFolderKey", out var createFolderProperty);
                if (createFolderProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.CreateFolderKey), out createFolderProperty);
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

                var hiddenDrivePaths = ReadStringListSetting(
                    document.RootElement,
                    "hiddenDrivePaths",
                    nameof(ShortcutSettingsConfig.HiddenDrivePaths));
                _hiddenDrivePaths.Clear();
                foreach (var path in hiddenDrivePaths.Select(NormalizeDriveRootPath).Where(static path => !string.IsNullOrWhiteSpace(path)))
                {
                    _hiddenDrivePaths.Add(path);
                }

                document.RootElement.TryGetProperty("themeMode", out var themeModeProperty);
                if (themeModeProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.ThemeMode), out themeModeProperty);
                }

                var showSelectedFileSizeProperty = ReadProperty(
                    document.RootElement,
                    "showSelectedFileSize",
                    nameof(ShortcutSettingsConfig.ShowSelectedFileSize));
                var showSelectedFolderSizeProperty = ReadProperty(
                    document.RootElement,
                    "showSelectedFolderSize",
                    nameof(ShortcutSettingsConfig.ShowSelectedFolderSize));
                var defaultTerminalShellKindProperty = ReadProperty(
                    document.RootElement,
                    "defaultTerminalShellKind",
                    nameof(ShortcutSettingsConfig.DefaultTerminalShellKind));
                var defaultTerminalWorkingDirectoryModeProperty = ReadProperty(
                    document.RootElement,
                    "defaultTerminalWorkingDirectoryMode",
                    nameof(ShortcutSettingsConfig.DefaultTerminalWorkingDirectoryMode));
                var defaultTerminalCustomWorkingDirectory = ReadStringSetting(
                    document.RootElement,
                    "defaultTerminalCustomWorkingDirectory",
                    nameof(ShortcutSettingsConfig.DefaultTerminalCustomWorkingDirectory));

                _shortcutSettings = new ShortcutSettings
                {
                    CopyToOtherPaneKey = ReadShortcutKey(copyProperty, ShortcutSettings.DefaultCopyToOtherPaneKey),
                    MoveToOtherPaneKey = ReadShortcutKey(moveProperty, ShortcutSettings.DefaultMoveToOtherPaneKey),
                    NavigateUpKey = ReadShortcutKey(navigateUpProperty, ShortcutSettings.DefaultNavigateUpKey),
                    CreateFolderKey = ReadShortcutKey(createFolderProperty, ShortcutSettings.DefaultCreateFolderKey),
                    DeleteKey = ReadShortcutKey(deleteProperty, ShortcutSettings.DefaultDeleteKey),
                    ThemeMode = ReadThemeMode(themeModeProperty, ShortcutSettings.DefaultThemeMode),
                    ShowSelectedFileSize = ReadBooleanSetting(showSelectedFileSizeProperty, ShortcutSettings.DefaultShowSelectedFileSize),
                    ShowSelectedFolderSize = ReadBooleanSetting(showSelectedFolderSizeProperty, ShortcutSettings.DefaultShowSelectedFolderSize),
                    ShowHiddenSystemItems = ReadBooleanSetting(showHiddenSystemItemsProperty, ShortcutSettings.DefaultShowHiddenSystemItems),
                    DefaultTerminalShellKind = ReadEnumSetting(defaultTerminalShellKindProperty, ShortcutSettings.DefaultTerminalShellKindValue),
                    DefaultTerminalWorkingDirectoryMode = ReadEnumSetting(defaultTerminalWorkingDirectoryModeProperty, ShortcutSettings.DefaultTerminalWorkingDirectoryModeValue),
                    DefaultTerminalCustomWorkingDirectory = defaultTerminalCustomWorkingDirectory,
                };
            }
            catch
            {
                _shortcutSettings = ShortcutSettings.CreateDefault();
                _hiddenDrivePaths.Clear();
            }
        }

        private void LoadAccountSettings()
        {
            _accountSettings = AccountSettingsState.CreateDefault();

            try
            {
                var localSettings = LoadLocalSettingsConfig();
                if (localSettings?.Account is not null)
                {
                    _accountSettings = BuildAccountSettingsState(localSettings.Account);
                    return;
                }
            }
            catch
            {
                _accountSettings = AccountSettingsState.CreateDefault();
            }
        }

        private static AccountSettingsState BuildAccountSettingsState(AccountSettingsConfig config)
        {
            return new AccountSettingsState
            {
                ApiBaseUrl = NormalizeApiBaseUrl(config.ApiBaseUrl),
                Email = config.Email?.Trim() ?? string.Empty,
                Token = config.Token ?? string.Empty,
                UserDisplayName = config.UserDisplayName ?? string.Empty,
                ServiceAccountsSummary = config.ServiceAccountsSummary ?? string.Empty,
                PayloadJson = config.PayloadJson ?? string.Empty,
                ServiceAccountsJson = config.ServiceAccountsJson ?? string.Empty,
                LastLoginText = string.IsNullOrWhiteSpace(config.LastLoginText) ? "尚未登入" : config.LastLoginText,
                LastStatusText = string.IsNullOrWhiteSpace(config.LastStatusText)
                    ? (string.IsNullOrWhiteSpace(config.Token) ? "尚未登入" : "已載入本機登入狀態")
                    : config.LastStatusText,
            };
        }

        private static LocalSettingsConfig? LoadLocalSettingsConfig()
        {
            if (!File.Exists(SettingsLocalConfigPath))
            {
                return null;
            }

            try
            {
                var rawJson = File.ReadAllText(SettingsLocalConfigPath);
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (root.TryGetProperty("account", out _) ||
                    root.TryGetProperty(nameof(LocalSettingsConfig.Account), out _) ||
                    root.TryGetProperty("backupAutomations", out _) ||
                    root.TryGetProperty(nameof(LocalSettingsConfig.BackupAutomations), out _) ||
                    root.TryGetProperty("autoExtractProfiles", out _) ||
                    root.TryGetProperty(nameof(LocalSettingsConfig.AutoExtractProfiles), out _) ||
                    root.TryGetProperty("groups", out _) ||
                    root.TryGetProperty(nameof(LocalSettingsConfig.Groups), out _) ||
                    root.TryGetProperty("leftPanePath", out _) ||
                    root.TryGetProperty(nameof(LocalSettingsConfig.LeftPanePath), out _) ||
                    root.TryGetProperty("rightPanePath", out _) ||
                    root.TryGetProperty(nameof(LocalSettingsConfig.RightPanePath), out _) ||
                    root.TryGetProperty("windowPlacement", out _) ||
                    root.TryGetProperty(nameof(LocalSettingsConfig.WindowPlacement), out _) ||
                    root.TryGetProperty("logging", out _) ||
                    root.TryGetProperty(nameof(LocalSettingsConfig.Logging), out _))
                {
                    return JsonSerializer.Deserialize<LocalSettingsConfig>(rawJson, JsonOptions);
                }

                var legacyAccount = JsonSerializer.Deserialize<AccountSettingsConfig>(rawJson, JsonOptions);
                return legacyAccount is null
                    ? null
                    : new LocalSettingsConfig { Account = legacyAccount };
            }
            catch
            {
                return null;
            }
        }

        private void LoadFileBunkerSettings()
        {
            _fileBunkerSettings = FileBunkerSettingsState.CreateDefault();

            try
            {
                if (!File.Exists(SettingsSyncConfigPath))
                {
                    return;
                }

                var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsSyncConfigPath), JsonOptions);
                if (settings?.FileBunker is null)
                {
                    return;
                }

                _fileBunkerSettings = new FileBunkerSettingsState
                {
                    InputEndpoint = settings.FileBunker.InputEndpoint?.Trim() ?? "https://filein.filebunker.com",
                    OutputEndpointBase = settings.FileBunker.OutputEndpointBase?.Trim() ?? "https://out.filebunker.com",
                    ApiKey = settings.FileBunker.ApiKey ?? string.Empty,
                    KeyLength = settings.FileBunker.KeyLength <= 0 ? 64 : settings.FileBunker.KeyLength,
                    ClientId = settings.FileBunker.ClientId?.Trim() ?? string.Empty,
                    DaysToExpiration = settings.FileBunker.DaysToExpiration <= 0 ? 3650 : settings.FileBunker.DaysToExpiration,
                    DaysToPurge = settings.FileBunker.DaysToPurge <= 0 ? 20 : settings.FileBunker.DaysToPurge,
                };
            }
            catch
            {
                _fileBunkerSettings = FileBunkerSettingsState.CreateDefault();
            }
        }

        private void LoadLoggingSettings()
        {
            var configuredLogDirectoryPath = ResolveConfiguredLogDirectoryPath();
            _loggingSettings = new LoggingSettingsState
            {
                LogDirectoryPath = configuredLogDirectoryPath,
            };
            _lastLocalBackupText = "尚未備份";

            try
            {
                var settings = LoadLocalSettingsConfig();
                if (!string.IsNullOrWhiteSpace(settings?.LastLocalBackupText))
                {
                    _lastLocalBackupText = settings.LastLocalBackupText;
                }
            }
            catch
            {
                _lastLocalBackupText = "尚未備份";
            }

            ApplyConfiguredLogDirectoryPath(_loggingSettings.LogDirectoryPath);
        }

        private void SaveCustomGroups()
        {
            Directory.CreateDirectory(ConfigDirectoryPath);
            SaveLocalSettingsSections(config =>
            {
                config.Groups = BuildGroupConfigs();
            });
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
            SaveSyncSettingsSections(config =>
            {
                config.CopyToOtherPaneKey = _shortcutSettings.CopyToOtherPaneKey;
                config.MoveToOtherPaneKey = _shortcutSettings.MoveToOtherPaneKey;
                config.NavigateUpKey = _shortcutSettings.NavigateUpKey;
                config.CreateFolderKey = _shortcutSettings.CreateFolderKey;
                config.DeleteKey = _shortcutSettings.DeleteKey;
                config.ThemeMode = _shortcutSettings.ThemeMode;
                config.ShowSelectedFileSize = _shortcutSettings.ShowSelectedFileSize;
                config.ShowSelectedFolderSize = _shortcutSettings.ShowSelectedFolderSize;
                config.ShowHiddenSystemItems = _shortcutSettings.ShowHiddenSystemItems;
                config.DefaultTerminalShellKind = _shortcutSettings.DefaultTerminalShellKind;
                config.DefaultTerminalWorkingDirectoryMode = _shortcutSettings.DefaultTerminalWorkingDirectoryMode;
                config.DefaultTerminalCustomWorkingDirectory = _shortcutSettings.DefaultTerminalCustomWorkingDirectory;
                config.HiddenDrivePaths = _hiddenDrivePaths
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                config.FileBunker = BuildFileBunkerSettingsConfig();
            });

            SaveLocalSettingsSections(config =>
            {
                config.Logging = BuildLoggingSettingsConfig();
            });
        }

        private async void SaveToolbarCommandsSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveSyncSettingsSections(config =>
                {
                    config.ToolbarCommands = BuildToolbarCommandConfigs();
                });
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("儲存工具列失敗", ex.Message);
            }
        }

        private void SaveAllSettingsFiles()
        {
            SaveAppSettings();
            SaveLocalSettings();
        }

        private void SaveSyncSettingsSections(Action<ShortcutSettingsConfig> update, bool queueUpload = true)
        {
            string? json = null;
            WithCrossProcessMutex(
                SyncSettingsMutexName,
                () =>
                {
                    var settings = LoadSyncSettingsConfigFromDisk();
                    update(settings);
                    json = JsonSerializer.Serialize(settings, JsonOptions);
                    WriteTextFileAtomically(SettingsSyncConfigPath, json);
                });

            if (queueUpload && !string.IsNullOrWhiteSpace(json))
            {
                QueueSyncSettingsUpload(json);
            }
        }

        private void SaveLocalSettingsSections(Action<LocalSettingsConfig> update)
        {
            WithCrossProcessMutex(
                LocalSettingsMutexName,
                () =>
                {
                    var settings = LoadLocalSettingsConfigFromDisk() ?? new LocalSettingsConfig();
                    update(settings);
                    WriteTextFileAtomically(SettingsLocalConfigPath, JsonSerializer.Serialize(settings, JsonOptions));
                });
        }

        private void SaveNotificationHistories(bool mergeExistingRecords)
        {
            Directory.CreateDirectory(ConfigDirectoryPath);

            List<NotificationHistoryRecord> localRecords;
            List<NotificationHistoryRecord> syncRecords;
            lock (_notificationHistoryLock)
            {
                localRecords = _localNotificationHistory.ToList();
                syncRecords = _syncNotificationHistory.ToList();
            }

            WithCrossProcessMutex(
                NotificationHistoryMutexName,
                () =>
                {
                    if (mergeExistingRecords)
                    {
                        localRecords = MergeNotificationHistoryRecords(
                            LoadNotificationHistoryFile(LocalNotificationHistoryPath, NotificationHistoryScope.LocalOnly),
                            localRecords);
                        syncRecords = MergeNotificationHistoryRecords(
                            LoadNotificationHistoryFile(SyncNotificationHistoryPath, NotificationHistoryScope.Sync),
                            syncRecords);
                    }

                    WriteTextFileAtomically(LocalNotificationHistoryPath, JsonSerializer.Serialize(localRecords, JsonOptions));
                    WriteTextFileAtomically(SyncNotificationHistoryPath, JsonSerializer.Serialize(syncRecords, JsonOptions));
                });
        }

        private static ShortcutSettingsConfig LoadSyncSettingsConfigFromDisk()
        {
            if (!File.Exists(SettingsSyncConfigPath))
            {
                return new ShortcutSettingsConfig();
            }

            try
            {
                return JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsSyncConfigPath), JsonOptions)
                    ?? new ShortcutSettingsConfig();
            }
            catch
            {
                return new ShortcutSettingsConfig();
            }
        }

        private static LocalSettingsConfig? LoadLocalSettingsConfigFromDisk()
        {
            return LoadLocalSettingsConfig();
        }

        private static List<NotificationHistoryRecord> MergeNotificationHistoryRecords(
            IEnumerable<NotificationHistoryRecord> existingRecords,
            IEnumerable<NotificationHistoryRecord> incomingRecords)
        {
            return existingRecords
                .Concat(incomingRecords)
                .GroupBy(static record => record.Id)
                .Select(static group => group
                    .OrderByDescending(static record => ParseNotificationHistorySortTimestamp(record.CreatedAtUtc))
                    .First())
                .OrderByDescending(static record => ParseNotificationHistorySortTimestamp(record.CreatedAtUtc))
                .Take(200)
                .ToList();
        }

        private static DateTime ParseNotificationHistorySortTimestamp(string? createdAtUtc)
        {
            return DateTime.TryParse(
                createdAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var timestamp)
                ? timestamp
                : DateTime.MinValue;
        }

        private static void WithCrossProcessMutex(string mutexName, Action action)
        {
            using var mutex = new Mutex(initiallyOwned: false, mutexName);
            var acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    throw new TimeoutException($"無法取得設定鎖：{mutexName}");
                }

                action();
            }
            finally
            {
                if (acquired)
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static void WriteTextFileAtomically(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.Combine(directory ?? ConfigDirectoryPath, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tempPath, content, Encoding.UTF8);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private void AddSyncOperationNotification(
            NotificationHistoryScope scope,
            string summary,
            string details)
        {
            AddNotificationHistoryRecord(scope, "同步", summary, details, showWindowsToast: false);
        }

        private void LoadToolbarCommands()
        {
            ToolbarCommands.Clear();

            try
            {
                if (!File.Exists(SettingsSyncConfigPath))
                {
                    return;
                }

                var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsSyncConfigPath), JsonOptions);
                if (settings?.ToolbarCommands is null)
                {
                    return;
                }

                foreach (var config in settings.ToolbarCommands)
                {
                    if (string.IsNullOrWhiteSpace(config.Title) || string.IsNullOrWhiteSpace(config.Command))
                    {
                        continue;
                    }

                    ToolbarCommands.Add(new ToolbarCommandItem
                    {
                        Id = config.Id == Guid.Empty ? Guid.NewGuid() : config.Id,
                        Title = config.Title,
                        Command = config.Command,
                        IconPath = config.IconPath,
                        IconGlyph = config.IconGlyph,
                        NodeDockerUser = config.NodeDockerUser,
                        NodeDockerHost = config.NodeDockerHost,
                        NodeDockerRemoteDirectory = config.NodeDockerRemoteDirectory,
                        NodeDockerLaunchMode = config.NodeDockerLaunchMode,
                        TerminalShellKind = config.TerminalShellKind,
                        TerminalWorkingDirectoryMode = config.TerminalWorkingDirectoryMode,
                        TerminalCustomWorkingDirectory = config.TerminalCustomWorkingDirectory,
                        TerminalLaunchArguments = config.TerminalLaunchArguments,
                    });
                }
            }
            catch
            {
            }

        }

        private void LoadNotificationHistories()
        {
            lock (_notificationHistoryLock)
            {
                _localNotificationHistory.Clear();
                _syncNotificationHistory.Clear();
                _localNotificationHistory.AddRange(LoadNotificationHistoryFile(LocalNotificationHistoryPath, NotificationHistoryScope.LocalOnly));
                _syncNotificationHistory.AddRange(LoadNotificationHistoryFile(SyncNotificationHistoryPath, NotificationHistoryScope.Sync));
            }
        }

        private static List<NotificationHistoryRecord> LoadNotificationHistoryFile(string path, NotificationHistoryScope scope)
        {
            if (!File.Exists(path))
            {
                return new List<NotificationHistoryRecord>();
            }

            try
            {
                var records = JsonSerializer.Deserialize<List<NotificationHistoryRecord>>(File.ReadAllText(path), JsonOptions);
                if (records is null)
                {
                    return new List<NotificationHistoryRecord>();
                }

                return records
                    .Where(static record => !string.IsNullOrWhiteSpace(record.Summary))
                    .Select(record => new NotificationHistoryRecord
                    {
                        Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                        Scope = record.Scope,
                        Category = record.Category?.Trim() ?? string.Empty,
                        Summary = record.Summary?.Trim() ?? string.Empty,
                        Details = record.Details ?? string.Empty,
                        CreatedAtUtc = string.IsNullOrWhiteSpace(record.CreatedAtUtc)
                            ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                            : record.CreatedAtUtc,
                        DeviceName = record.DeviceName?.Trim() ?? string.Empty,
                    })
                    .Select(record =>
                    {
                        if (record.Scope == NotificationHistoryScope.LocalOnly && scope == NotificationHistoryScope.Sync)
                        {
                            record.Scope = NotificationHistoryScope.Sync;
                        }
                        else if (record.Scope == NotificationHistoryScope.Sync && scope == NotificationHistoryScope.LocalOnly)
                        {
                            record.Scope = NotificationHistoryScope.LocalOnly;
                        }

                        return record;
                    })
                    .Take(200)
                    .ToList();
            }
            catch
            {
                return new List<NotificationHistoryRecord>();
            }
        }

        private async Task ExecuteToolbarCommandAsync(ToolbarCommandItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Command))
            {
                await ShowMessageAsync("無法執行工具列按鈕", "這個工具列按鈕尚未設定 command。");
                return;
            }

            if (IsBuiltInToolbarCommand(item.Command))
            {
                try
                {
                    await ExecuteBuiltInToolbarCommandAsync(item);
                }
                catch (Exception ex)
                {
                    LogBoundaryException(ex, "built-in toolbar command");
                    await ShowMessageAsync("執行工具列按鈕失敗", $"{item.Title}\n{ex.Message}");
                }

                return;
            }

            var currentPath = _activePane.CurrentPath?.Trim();
            if (string.IsNullOrWhiteSpace(currentPath) || !IsNavigableDirectoryPath(currentPath))
            {
                await ShowMessageAsync("無法執行工具列按鈕", "目前作用中的 Pane 路徑無效。");
                return;
            }

            var backgroundWorkId = BeginBackgroundWork($"執行 {item.Title} 中");

            try
            {
                await Task.Run(() => ExecuteToolbarCommandCore(item.Command, currentPath));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("執行工具列按鈕失敗", $"{item.Title}\n{ex.Message}");
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        private static void ExecuteToolbarCommandCore(string command, string workingPath)
        {
            var normalizedPath = NormalizePath(workingPath);
            var shellCommand = $"/d /c pushd \"{normalizedPath}\" && {command}";

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = shellCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                throw new InvalidOperationException("無法啟動命令列處理程序。");
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"命令結束代碼 {process.ExitCode}。");
            }
        }

        private void UpdateAccountSettingsUi()
        {
            if (AccountApiUrlTextBox is null ||
                AccountConnectionStatusCard is null ||
                AccountEmailTextBox is null ||
                AccountLoginFieldsPanel is null ||
                AccountPasswordBox is null ||
                AccountPayloadJsonTextBox is null ||
                LoginAccountButton is null ||
                AccountConnectionStatusTextBlock is null ||
                AccountUserTextBlock is null ||
                AccountServiceAccountsTextBlock is null ||
                AccountLastLoginTextBlock is null ||
                AccountTokenTextBlock is null)
            {
                return;
            }

            _isUpdatingAccountUi = true;
            try
            {
                AccountApiUrlTextBox.Text = NormalizeApiBaseUrl(_accountSettings.ApiBaseUrl);
                AccountEmailTextBox.Text = _accountSettings.Email;

                var connected = !string.IsNullOrWhiteSpace(_accountSettings.Token);
                var showLoginFields = _isAccountLoginRunning || !connected || _isAccountReloginFieldsVisible;

                AccountLoginFieldsPanel.Visibility = showLoginFields ? Visibility.Visible : Visibility.Collapsed;
                AccountConnectionStatusCard.Visibility = connected && !showLoginFields ? Visibility.Visible : Visibility.Collapsed;
                AccountApiUrlTextBox.IsEnabled = !_isAccountLoginRunning;
                AccountEmailTextBox.IsEnabled = !_isAccountLoginRunning;
                AccountPasswordBox.IsEnabled = !_isAccountLoginRunning;
                LoginAccountButton.IsEnabled = !_isAccountLoginRunning;
                LoginAccountButton.Content = _isAccountLoginRunning
                    ? "登入中..."
                    : connected && !showLoginFields
                        ? "重新登入"
                        : "登入";

                AccountConnectionStatusTextBlock.Text = connected
                    ? $"已連線 · {_accountSettings.LastStatusText}"
                    : _accountSettings.LastStatusText;
                AccountUserTextBlock.Text = string.IsNullOrWhiteSpace(_accountSettings.UserDisplayName)
                    ? _accountSettings.Email
                    : _accountSettings.UserDisplayName;
                AccountServiceAccountsTextBlock.Text = string.IsNullOrWhiteSpace(_accountSettings.ServiceAccountsSummary)
                    ? "尚未取得"
                    : _accountSettings.ServiceAccountsSummary;
                AccountLastLoginTextBlock.Text = _accountSettings.LastLoginText;
                AccountTokenTextBlock.Text = MaskToken(_accountSettings.Token);
                AccountPayloadJsonTextBox.Text = FormatJsonForDisplay(_accountSettings.PayloadJson, "尚未取得 login payload");
            }
            finally
            {
                _isUpdatingAccountUi = false;
            }
        }

        private static string NormalizeApiBaseUrl(string? rawValue)
        {
            var value = rawValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "https://api.nuone.cl";
            }

            return value.TrimEnd('/');
        }

        private static string BuildIdentityLoginUrl(string apiBaseUrl)
        {
            return $"{NormalizeApiBaseUrl(apiBaseUrl)}/identity/login";
        }

        private static string BuildIdentitySettingsSyncUrl(string apiBaseUrl)
        {
            return $"{NormalizeApiBaseUrl(apiBaseUrl)}/identity/settings/sync";
        }

        private static string BuildIdentitySettingsLocalUrl(string apiBaseUrl, string deviceName)
        {
            return $"{NormalizeApiBaseUrl(apiBaseUrl)}/identity/settings/local?device={Uri.EscapeDataString(deviceName)}";
        }

        private static string MaskToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "尚未登入";
            }

            var value = token.Trim();
            if (value.Length <= 20)
            {
                return value;
            }

            return $"{value[..10]} ... {value[^10..]}";
        }

        private static string FormatJsonForDisplay(string? rawJson, string fallbackText)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return fallbackText;
            }

            try
            {
                using var document = JsonDocument.Parse(rawJson);
                return JsonSerializer.Serialize(document.RootElement, JsonOptions);
            }
            catch
            {
                return rawJson.Trim();
            }
        }

        private async Task<AccountLoginResult> LoginToIdentityAsync(string apiBaseUrl, string email, string password)
        {
            var requestBody = new
            {
                email = email.Trim(),
                password,
                remember = true,
                clientApp = "nuone-tools",
                environment = "desktop",
                platform = "windows",
                host = NormalizeApiBaseUrl(apiBaseUrl),
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildIdentityLoginUrl(apiBaseUrl))
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("User-Agent", "nuone-tools/1.202606.1");
            request.Headers.TryAddWithoutValidation("x-client-app", "nuone-tools");
            request.Headers.TryAddWithoutValidation("x-environment", "desktop");

            using var response = await SharedHttpClient.SendAsync(request);
            var rawResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var apiErrorMessage = ExtractApiErrorMessage(rawResponse);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiErrorMessage)
                    ? $"登入失敗，HTTP {(int)response.StatusCode}"
                    : apiErrorMessage);
            }

            using var document = JsonDocument.Parse(rawResponse);
            var root = document.RootElement;
            var token = root.TryGetProperty("token", out var tokenProperty) && tokenProperty.ValueKind == JsonValueKind.String
                ? tokenProperty.GetString()?.Trim() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("登入成功，但 API 沒有回傳 token。");
            }

            var payloadJson = root.TryGetProperty("payload", out var payloadProperty)
                ? payloadProperty.GetRawText()
                : string.Empty;
            var serviceAccountsJson = root.TryGetProperty("serviceAccounts", out var serviceAccountsProperty)
                ? serviceAccountsProperty.GetRawText()
                : string.Empty;

            return new AccountLoginResult
            {
                Token = token,
                PayloadJson = payloadJson,
                ServiceAccountsJson = serviceAccountsJson,
                DisplayName = ExtractAccountDisplayName(root, email),
                ServiceAccountsSummary = SummarizeServiceAccounts(root),
            };
        }

        private static string ExtractApiErrorMessage(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(rawResponse);
                var root = document.RootElement;
                if (root.TryGetProperty("message", out var messageProperty) && messageProperty.ValueKind == JsonValueKind.String)
                {
                    return messageProperty.GetString()?.Trim() ?? string.Empty;
                }

                if (root.TryGetProperty("error", out var errorProperty) && errorProperty.ValueKind == JsonValueKind.String)
                {
                    return errorProperty.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch
            {
            }

            return rawResponse.Trim();
        }

        private sealed class AuthenticatedSettingsSyncContext
        {
            public string ApiBaseUrl { get; init; } = string.Empty;

            public string Token { get; init; } = string.Empty;

            public string ServiceAccountCode { get; init; } = string.Empty;
        }

        private static string GetLocalSettingsDeviceName()
        {
            var deviceName = Environment.MachineName?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(deviceName) ? "unknown-device" : deviceName;
        }

        private bool TryGetAuthenticatedSettingsSyncContext(out AuthenticatedSettingsSyncContext context)
        {
            var apiBaseUrl = NormalizeApiBaseUrl(_accountSettings.ApiBaseUrl);
            var token = _accountSettings.Token?.Trim() ?? string.Empty;
            var serviceAccountCode = ResolveServiceAccountCode("setting", "settings-sync-debug.log");

            if (string.IsNullOrWhiteSpace(apiBaseUrl) ||
                string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(serviceAccountCode))
            {
                AppendDebugLog(
                    "settings-sync-debug.log",
                    $"auth-context-invalid api={apiBaseUrl} tokenPresent={!string.IsNullOrWhiteSpace(token)} tokenPreview={MaskToken(token)} serviceAccountPresent={!string.IsNullOrWhiteSpace(serviceAccountCode)} email={_accountSettings.Email}");
                context = new AuthenticatedSettingsSyncContext();
                return false;
            }

            context = new AuthenticatedSettingsSyncContext
            {
                ApiBaseUrl = apiBaseUrl,
                Token = token,
                ServiceAccountCode = serviceAccountCode,
            };
            AppendDebugLog(
                "settings-sync-debug.log",
                $"auth-context-ready api={context.ApiBaseUrl} serviceAccount={context.ServiceAccountCode} tokenPreview={MaskToken(context.Token)} email={_accountSettings.Email}");
            return true;
        }

        private async Task InitializeRemoteSyncSettingsAsync()
        {
            try
            {
                await DownloadLatestSyncSettingsAsync("startup");
            }
            catch (Exception ex)
            {
                AppendDebugLog("settings-sync-debug.log", $"startup-sync-failed message={ex.Message} detail={ex}");
                AddSyncOperationNotification(NotificationHistoryScope.Sync, "同步設定下載失敗", $"startup：{ex.Message}");
            }
        }

        private async Task DownloadLatestSyncSettingsAsync(string reason)
        {
            if (!TryGetAuthenticatedSettingsSyncContext(out var context))
            {
                AppendDebugLog("settings-sync-debug.log", $"download-skip reason={reason} authenticated=false");
                return;
            }

            var url = BuildIdentitySettingsSyncUrl(context.ApiBaseUrl);
            AppendDebugLog(
                "settings-sync-debug.log",
                $"download-start reason={reason} url={url} serviceAccount={context.ServiceAccountCode} tokenPreview={MaskToken(context.Token)}");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {context.Token}");
            request.Headers.TryAddWithoutValidation("x-service-account", context.ServiceAccountCode);
            request.Headers.TryAddWithoutValidation("User-Agent", "nuone-tools/1.202606.1");
            request.Headers.TryAddWithoutValidation("x-client-app", "nuone-tools");

            using var response = await SharedHttpClient.SendAsync(request);
            var rawResponse = await response.Content.ReadAsStringAsync();
            AppendDebugLog(
                "settings-sync-debug.log",
                $"download-response reason={reason} status={(int)response.StatusCode} bodyPreview={TrimForDebugPreview(rawResponse)}");

            if (!response.IsSuccessStatusCode)
            {
                var apiErrorMessage = ExtractApiErrorMessage(rawResponse);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiErrorMessage)
                    ? $"同步設定下載失敗，HTTP {(int)response.StatusCode}"
                    : apiErrorMessage);
            }

            using var document = JsonDocument.Parse(rawResponse);
            if (!document.RootElement.TryGetProperty("document", out var documentProperty) ||
                documentProperty.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                AppendDebugLog("settings-sync-debug.log", $"download-empty reason={reason} action=upload-local");
                await UploadCurrentSyncSettingsAsBootstrapAsync(reason);
                return;
            }

            if (!documentProperty.TryGetProperty("settings", out var settingsProperty) ||
                settingsProperty.ValueKind != JsonValueKind.Object)
            {
                AppendDebugLog("settings-sync-debug.log", $"download-no-settings reason={reason}");
                return;
            }

            var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(settingsProperty.GetRawText(), JsonOptions);
            if (settings is null)
            {
                AppendDebugLog("settings-sync-debug.log", $"download-deserialize-null reason={reason}");
                return;
            }

            await ApplyDownloadedSyncSettingsAsync(settings, reason);
        }

        private async Task UploadCurrentSyncSettingsAsBootstrapAsync(string reason)
        {
            var settingsJson = await GetCurrentSyncSettingsJsonAsync();
            if (string.IsNullOrWhiteSpace(settingsJson))
            {
                AppendDebugLog("settings-sync-debug.log", $"bootstrap-upload-skip reason={reason} settingsJsonEmpty=true");
                return;
            }

            AppendDebugLog(
                "settings-sync-debug.log",
                $"bootstrap-upload-start reason={reason} settingsPreview={TrimForDebugPreview(settingsJson)}");

            try
            {
                await UploadSyncSettingsJsonAsync(settingsJson);
                AppendDebugLog("settings-sync-debug.log", $"bootstrap-upload-success reason={reason}");
                AddSyncOperationNotification(NotificationHistoryScope.Sync, "同步設定已建立", $"{reason}：遠端沒有資料，已上傳本機 settings-sync.json");
            }
            catch (Exception ex)
            {
                AppendDebugLog("settings-sync-debug.log", $"bootstrap-upload-failed reason={reason} message={ex.Message} detail={ex}");
                AddSyncOperationNotification(NotificationHistoryScope.Sync, "同步設定建立失敗", $"{reason}：{ex.Message}");
            }
        }

        private async Task DownloadLatestLocalSettingsAsync(string reason)
        {
            if (!TryGetAuthenticatedSettingsSyncContext(out var context))
            {
                AppendDebugLog("settings-local-debug.log", $"download-skip reason={reason} authenticated=false");
                return;
            }

            var deviceName = GetLocalSettingsDeviceName();
            var url = BuildIdentitySettingsLocalUrl(context.ApiBaseUrl, deviceName);
            AppendDebugLog(
                "settings-local-debug.log",
                $"download-start reason={reason} url={url} serviceAccount={context.ServiceAccountCode} device={deviceName} tokenPreview={MaskToken(context.Token)}");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {context.Token}");
            request.Headers.TryAddWithoutValidation("x-service-account", context.ServiceAccountCode);
            request.Headers.TryAddWithoutValidation("User-Agent", "nuone-tools/1.202606.1");
            request.Headers.TryAddWithoutValidation("x-client-app", "nuone-tools");

            using var response = await SharedHttpClient.SendAsync(request);
            var rawResponse = await response.Content.ReadAsStringAsync();
            AppendDebugLog(
                "settings-local-debug.log",
                $"download-response reason={reason} status={(int)response.StatusCode} bodyPreview={TrimForDebugPreview(rawResponse)}");

            if (!response.IsSuccessStatusCode)
            {
                var apiErrorMessage = ExtractApiErrorMessage(rawResponse);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiErrorMessage)
                    ? $"本機設定下載失敗，HTTP {(int)response.StatusCode}"
                    : apiErrorMessage);
            }

            using var document = JsonDocument.Parse(rawResponse);
            if (!document.RootElement.TryGetProperty("document", out var documentProperty) ||
                documentProperty.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                AppendDebugLog("settings-local-debug.log", $"download-empty reason={reason} action=upload-local device={deviceName}");
                await UploadCurrentLocalSettingsAsBootstrapAsync(reason);
                return;
            }

            if (!documentProperty.TryGetProperty("settings", out var settingsProperty) ||
                settingsProperty.ValueKind != JsonValueKind.Object)
            {
                AppendDebugLog("settings-local-debug.log", $"download-no-settings reason={reason} device={deviceName}");
                return;
            }

            var settings = JsonSerializer.Deserialize<LocalSettingsConfig>(settingsProperty.GetRawText(), JsonOptions);
            if (settings is null)
            {
                AppendDebugLog("settings-local-debug.log", $"download-deserialize-null reason={reason} device={deviceName}");
                return;
            }

            await ApplyDownloadedLocalSettingsAsync(settings, reason);
        }

        private async Task UploadCurrentLocalSettingsAsBootstrapAsync(string reason)
        {
            var settingsJson = await GetCurrentLocalSettingsJsonAsync();
            if (string.IsNullOrWhiteSpace(settingsJson))
            {
                AppendDebugLog("settings-local-debug.log", $"bootstrap-upload-skip reason={reason} settingsJsonEmpty=true");
                return;
            }

            AppendDebugLog(
                "settings-local-debug.log",
                $"bootstrap-upload-start reason={reason} settingsPreview={TrimForDebugPreview(settingsJson)}");

            try
            {
                await UploadLocalSettingsJsonAsync(settingsJson);
                AppendDebugLog("settings-local-debug.log", $"bootstrap-upload-success reason={reason}");
                AddSyncOperationNotification(NotificationHistoryScope.LocalOnly, "本機設定已建立", $"{reason}：遠端沒有資料，已上傳本機 settings-local.json（{GetLocalSettingsDeviceName()}）");
            }
            catch (Exception ex)
            {
                AppendDebugLog("settings-local-debug.log", $"bootstrap-upload-failed reason={reason} message={ex.Message} detail={ex}");
                AddSyncOperationNotification(NotificationHistoryScope.LocalOnly, "本機設定建立失敗", $"{reason}：{ex.Message}");
            }
        }

        private async Task<string> GetCurrentLocalSettingsJsonAsync()
        {
            if (File.Exists(SettingsLocalConfigPath))
            {
                var existingJson = await File.ReadAllTextAsync(SettingsLocalConfigPath);
                AppendDebugLog(
                    "settings-local-debug.log",
                    $"current-local-json source=file path={SettingsLocalConfigPath} length={existingJson.Length}");
                return existingJson;
            }

            string json = string.Empty;
            await EnqueueOnUiAsync(() =>
            {
                json = JsonSerializer.Serialize(BuildLocalSettingsConfig(), JsonOptions);
            });
            AppendDebugLog(
                "settings-local-debug.log",
                $"current-local-json source=memory path={SettingsLocalConfigPath} length={json.Length}");
            return json;
        }

        private async Task ApplyDownloadedLocalSettingsAsync(LocalSettingsConfig settings, string reason)
        {
            await EnqueueOnUiAsync(() =>
            {
                try
                {
                    _isApplyingRemoteLocalSettings = true;
                    var mergedSettings = MergeLocalExecutionState(LoadLocalSettingsConfig(), settings);
                    Directory.CreateDirectory(ConfigDirectoryPath);
                    WithCrossProcessMutex(
                        LocalSettingsMutexName,
                        () => WriteTextFileAtomically(SettingsLocalConfigPath, JsonSerializer.Serialize(mergedSettings, JsonOptions)));

                    LoadAccountSettings();
                    LoadLoggingSettings();
                    LoadBackupAutomations();
                    LoadAutoExtractProfiles();
                    LoadCustomGroups();
                    UpdateAccountSettingsUi();
                    UpdateLoggingSettingsUi();
                    UpdateSharedStatusBar();
                    RescheduleBackupAutomations();
                    RescheduleAutoExtractProfiles();

                    if (!string.IsNullOrWhiteSpace(mergedSettings.LeftPanePath) &&
                        !PathEquals(LeftPane.CurrentPath, mergedSettings.LeftPanePath) &&
                        IsNavigableDirectoryPath(mergedSettings.LeftPanePath))
                    {
                        OpenInPane(LeftPane, mergedSettings.LeftPanePath);
                    }

                    if (!string.IsNullOrWhiteSpace(mergedSettings.RightPanePath) &&
                        !PathEquals(RightPane.CurrentPath, mergedSettings.RightPanePath) &&
                        IsNavigableDirectoryPath(mergedSettings.RightPanePath))
                    {
                        OpenInPane(RightPane, mergedSettings.RightPanePath);
                    }

                    if (mergedSettings.WindowPlacement is { Width: > 0, Height: > 0 } placement)
                    {
                        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                            placement.X,
                            placement.Y,
                            placement.Width,
                            placement.Height));
                    }

                    AppendDebugLog(
                        "settings-local-debug.log",
                        $"download-applied reason={reason} groups={CustomGroups.Count} automations={BackupAutomations.Count} autoExtract={AutoExtractProfiles.Count}");
                    AddSyncOperationNotification(NotificationHistoryScope.LocalOnly, "本機設定已下載", $"{reason}：已套用 {GetLocalSettingsDeviceName()} 的 settings-local.json");
                }
                finally
                {
                    _isApplyingRemoteLocalSettings = false;
                }
            });
        }

        private async Task<string> GetCurrentSyncSettingsJsonAsync()
        {
            if (File.Exists(SettingsSyncConfigPath))
            {
                var existingJson = await File.ReadAllTextAsync(SettingsSyncConfigPath);
                AppendDebugLog(
                    "settings-sync-debug.log",
                    $"current-sync-json source=file path={SettingsSyncConfigPath} length={existingJson.Length}");
                return existingJson;
            }

            string json = string.Empty;
            await EnqueueOnUiAsync(() =>
            {
                json = JsonSerializer.Serialize(BuildSyncSettingsConfig(), JsonOptions);
            });
            AppendDebugLog(
                "settings-sync-debug.log",
                $"current-sync-json source=memory path={SettingsSyncConfigPath} length={json.Length}");
            return json;
        }

        private LocalSettingsConfig MergeLocalExecutionState(LocalSettingsConfig? currentLocalSettings, LocalSettingsConfig downloadedSettings)
        {
            if (currentLocalSettings is null)
            {
                return downloadedSettings;
            }

            var localBackupMap = currentLocalSettings.BackupAutomations.ToDictionary(item => item.Id, item => item);
            foreach (var downloadedProfile in downloadedSettings.BackupAutomations)
            {
                if (!localBackupMap.TryGetValue(downloadedProfile.Id, out var localProfile))
                {
                    continue;
                }

                if (TryGetMostRecentExecutionText(localProfile.LastRunText, downloadedProfile.LastRunText, out var preferredLastRunText) &&
                    string.Equals(preferredLastRunText, localProfile.LastRunText, StringComparison.Ordinal))
                {
                    downloadedProfile.LastRunText = localProfile.LastRunText;
                    downloadedProfile.LastResultText = localProfile.LastResultText;
                }
            }

            var localAutoExtractMap = currentLocalSettings.AutoExtractProfiles.ToDictionary(item => item.Id, item => item);
            foreach (var downloadedProfile in downloadedSettings.AutoExtractProfiles)
            {
                if (!localAutoExtractMap.TryGetValue(downloadedProfile.Id, out var localProfile))
                {
                    continue;
                }

                if (TryGetMostRecentExecutionText(localProfile.LastRunText, downloadedProfile.LastRunText, out var preferredLastRunText) &&
                    string.Equals(preferredLastRunText, localProfile.LastRunText, StringComparison.Ordinal))
                {
                    downloadedProfile.LastRunText = localProfile.LastRunText;
                    downloadedProfile.LastResultText = localProfile.LastResultText;
                }
            }

            return downloadedSettings;
        }

        private static bool TryGetMostRecentExecutionText(string first, string second, out string preferred)
        {
            var firstValid = TryParseExecutionTimestamp(first, out var firstTimestamp);
            var secondValid = TryParseExecutionTimestamp(second, out var secondTimestamp);

            if (firstValid && secondValid)
            {
                preferred = firstTimestamp >= secondTimestamp ? first : second;
                return true;
            }

            if (firstValid)
            {
                preferred = first;
                return true;
            }

            if (secondValid)
            {
                preferred = second;
                return true;
            }

            preferred = string.Empty;
            return false;
        }

        private static bool TryParseExecutionTimestamp(string rawValue, out DateTime timestamp)
        {
            return DateTime.TryParseExact(
                rawValue,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp);
        }

        private async Task ApplyDownloadedSyncSettingsAsync(ShortcutSettingsConfig settings, string reason)
        {
            await EnqueueOnUiAsync(() =>
            {
                try
                {
                    _isApplyingRemoteSyncSettings = true;
                    Directory.CreateDirectory(ConfigDirectoryPath);
                    WithCrossProcessMutex(
                        SyncSettingsMutexName,
                        () => WriteTextFileAtomically(SettingsSyncConfigPath, JsonSerializer.Serialize(settings, JsonOptions)));

                    LoadShortcutSettings();
                    LoadFileBunkerSettings();
                    LoadToolbarCommands();
                    ApplyThemePreference();
                    ApplySettingsToPanes();
                    ResetEditableShortcutSettings();
                    UpdateFileBunkerSettingsUi();
                    LoadDriveCards();
                    RefreshPane(LeftPane);
                    RefreshPane(RightPane);

                    AppendDebugLog(
                        "settings-sync-debug.log",
                        $"download-applied reason={reason} toolbarCount={ToolbarCommands.Count} hiddenDrives={_hiddenDrivePaths.Count}");
                    AddSyncOperationNotification(NotificationHistoryScope.Sync, "同步設定已下載", $"{reason}：已套用最新 settings-sync.json");
                }
                finally
                {
                    _isApplyingRemoteSyncSettings = false;
                }
            });
        }

        private void QueueSyncSettingsUpload(string settingsJson)
        {
            if (_isApplyingRemoteSyncSettings || string.IsNullOrWhiteSpace(settingsJson))
            {
                AppendDebugLog(
                    "settings-sync-debug.log",
                    $"upload-queue-skip applyingRemote={_isApplyingRemoteSyncSettings} settingsJsonEmpty={string.IsNullOrWhiteSpace(settingsJson)}");
                return;
            }

            lock (_syncSettingsUploadLock)
            {
                _pendingSyncSettingsUploadJson = settingsJson;
                AppendDebugLog(
                    "settings-sync-debug.log",
                    $"upload-queue-enqueue length={settingsJson.Length} workerRunning={_isSyncSettingsUploadWorkerRunning}");
                if (_isSyncSettingsUploadWorkerRunning)
                {
                    return;
                }

                _isSyncSettingsUploadWorkerRunning = true;
            }

            RunFireAndForget(ProcessSyncSettingsUploadQueueAsync(), "sync settings upload queue");
        }

        private void QueueLocalSettingsUpload(string settingsJson)
        {
            if (_isApplyingRemoteLocalSettings || string.IsNullOrWhiteSpace(settingsJson))
            {
                AppendDebugLog(
                    "settings-local-debug.log",
                    $"upload-queue-skip applyingRemote={_isApplyingRemoteLocalSettings} settingsJsonEmpty={string.IsNullOrWhiteSpace(settingsJson)}");
                return;
            }

            lock (_localSettingsUploadLock)
            {
                _pendingLocalSettingsUploadJson = settingsJson;
                AppendDebugLog(
                    "settings-local-debug.log",
                    $"upload-queue-enqueue length={settingsJson.Length} workerRunning={_isLocalSettingsUploadWorkerRunning}");
                if (_isLocalSettingsUploadWorkerRunning)
                {
                    return;
                }

                _isLocalSettingsUploadWorkerRunning = true;
            }

            RunFireAndForget(ProcessLocalSettingsUploadQueueAsync(), "local settings upload queue");
        }

        private async Task ProcessSyncSettingsUploadQueueAsync()
        {
            while (true)
            {
                string? pendingJson;
                lock (_syncSettingsUploadLock)
                {
                    pendingJson = _pendingSyncSettingsUploadJson;
                    _pendingSyncSettingsUploadJson = null;
                    if (string.IsNullOrWhiteSpace(pendingJson))
                    {
                        AppendDebugLog("settings-sync-debug.log", "upload-worker-idle");
                        _isSyncSettingsUploadWorkerRunning = false;
                        return;
                    }
                }

                try
                {
                    AppendDebugLog(
                        "settings-sync-debug.log",
                        $"upload-worker-send length={pendingJson.Length}");
                    await UploadSyncSettingsJsonAsync(pendingJson);
                }
                catch (Exception ex)
                {
                    AppendDebugLog("settings-sync-debug.log", $"upload-failed message={ex.Message} detail={ex}");
                }
            }
        }

        private async Task ProcessLocalSettingsUploadQueueAsync()
        {
            while (true)
            {
                string? pendingJson;
                lock (_localSettingsUploadLock)
                {
                    pendingJson = _pendingLocalSettingsUploadJson;
                    _pendingLocalSettingsUploadJson = null;
                    if (string.IsNullOrWhiteSpace(pendingJson))
                    {
                        AppendDebugLog("settings-local-debug.log", "upload-worker-idle");
                        _isLocalSettingsUploadWorkerRunning = false;
                        return;
                    }
                }

                try
                {
                    AppendDebugLog(
                        "settings-local-debug.log",
                        $"upload-worker-send length={pendingJson.Length}");
                    await UploadLocalSettingsJsonAsync(pendingJson);
                }
                catch (Exception ex)
                {
                    AppendDebugLog("settings-local-debug.log", $"upload-failed message={ex.Message} detail={ex}");
                }
            }
        }

        private async Task UploadSyncSettingsJsonAsync(string settingsJson)
        {
            if (!TryGetAuthenticatedSettingsSyncContext(out var context))
            {
                AppendDebugLog("settings-sync-debug.log", "upload-skip authenticated=false");
                return;
            }

            using var settingsDocument = JsonDocument.Parse(settingsJson);
            var requestBody = JsonSerializer.Serialize(new
            {
                settings = settingsDocument.RootElement.Clone(),
            }, JsonOptions);

            var url = BuildIdentitySettingsSyncUrl(context.ApiBaseUrl);
            AppendDebugLog(
                "settings-sync-debug.log",
                $"upload-start url={url} serviceAccount={context.ServiceAccountCode} tokenPreview={MaskToken(context.Token)} settingsPreview={TrimForDebugPreview(settingsJson)}");

            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {context.Token}");
            request.Headers.TryAddWithoutValidation("x-service-account", context.ServiceAccountCode);
            request.Headers.TryAddWithoutValidation("User-Agent", "nuone-tools/1.202606.1");
            request.Headers.TryAddWithoutValidation("x-client-app", "nuone-tools");

            using var response = await SharedHttpClient.SendAsync(request);
            var rawResponse = await response.Content.ReadAsStringAsync();
            AppendDebugLog(
                "settings-sync-debug.log",
                $"upload-response status={(int)response.StatusCode} bodyPreview={TrimForDebugPreview(rawResponse)}");

            if (!response.IsSuccessStatusCode)
            {
                var apiErrorMessage = ExtractApiErrorMessage(rawResponse);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiErrorMessage)
                    ? $"同步設定上傳失敗，HTTP {(int)response.StatusCode}"
                    : apiErrorMessage);
            }

            AppendDebugLog("settings-sync-debug.log", "upload-success");
            AddSyncOperationNotification(NotificationHistoryScope.Sync, "同步設定已上傳", "settings-sync.json 已同步到後端");
        }

        private async Task UploadLocalSettingsJsonAsync(string settingsJson, bool recordNotification = true)
        {
            if (!TryGetAuthenticatedSettingsSyncContext(out var context))
            {
                AppendDebugLog("settings-local-debug.log", "upload-skip authenticated=false");
                return;
            }

            var deviceName = GetLocalSettingsDeviceName();
            using var settingsDocument = JsonDocument.Parse(settingsJson);
            var requestBody = JsonSerializer.Serialize(new
            {
                device = deviceName,
                settings = settingsDocument.RootElement.Clone(),
            }, JsonOptions);

            var url = $"{NormalizeApiBaseUrl(context.ApiBaseUrl)}/identity/settings/local";
            AppendDebugLog(
                "settings-local-debug.log",
                $"upload-start url={url} serviceAccount={context.ServiceAccountCode} device={deviceName} tokenPreview={MaskToken(context.Token)} settingsPreview={TrimForDebugPreview(settingsJson)}");

            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {context.Token}");
            request.Headers.TryAddWithoutValidation("x-service-account", context.ServiceAccountCode);
            request.Headers.TryAddWithoutValidation("User-Agent", "nuone-tools/1.202606.1");
            request.Headers.TryAddWithoutValidation("x-client-app", "nuone-tools");

            using var response = await SharedHttpClient.SendAsync(request);
            var rawResponse = await response.Content.ReadAsStringAsync();
            AppendDebugLog(
                "settings-local-debug.log",
                $"upload-response status={(int)response.StatusCode} bodyPreview={TrimForDebugPreview(rawResponse)}");

            if (!response.IsSuccessStatusCode)
            {
                var apiErrorMessage = ExtractApiErrorMessage(rawResponse);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiErrorMessage)
                    ? $"本機設定上傳失敗，HTTP {(int)response.StatusCode}"
                    : apiErrorMessage);
            }

            AppendDebugLog("settings-local-debug.log", "upload-success");
            if (recordNotification)
            {
                AddSyncOperationNotification(NotificationHistoryScope.LocalOnly, "本機資料已備份", $"settings-local.json 已備份到後端（{deviceName}）");
            }
        }
        private static string ExtractAccountDisplayName(JsonElement root, string fallbackEmail)
        {
            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                if (payload.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                {
                    var name = nameProperty.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }

                if (payload.TryGetProperty("email", out var emailProperty) && emailProperty.ValueKind == JsonValueKind.String)
                {
                    var email = emailProperty.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        return email;
                    }
                }
            }

            return fallbackEmail;
        }

        private static string SummarizeServiceAccounts(JsonElement root)
        {
            if (!root.TryGetProperty("serviceAccounts", out var property) || property.ValueKind != JsonValueKind.Array)
            {
                return "0 個服務帳號";
            }

            var count = property.GetArrayLength();
            if (count == 0)
            {
                return "0 個服務帳號";
            }

            var labels = new List<string>();
            foreach (var item in property.EnumerateArray().Take(3))
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var label = string.Empty;
                    if (item.TryGetProperty("code", out var codeProperty) && codeProperty.ValueKind == JsonValueKind.String)
                    {
                        label = codeProperty.GetString()?.Trim() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(label) &&
                        item.TryGetProperty("name", out var nameProperty) &&
                        nameProperty.ValueKind == JsonValueKind.String)
                    {
                        label = nameProperty.GetString()?.Trim() ?? string.Empty;
                    }

                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        labels.Add(label);
                    }
                }
            }

            return labels.Count == 0
                ? $"{count} 個服務帳號"
                : $"{count} 個服務帳號 · {string.Join(", ", labels)}";
        }

        private ShortcutSettingsConfig BuildSyncSettingsConfig()
        {
            return new ShortcutSettingsConfig
            {
                CopyToOtherPaneKey = _shortcutSettings.CopyToOtherPaneKey,
                MoveToOtherPaneKey = _shortcutSettings.MoveToOtherPaneKey,
                NavigateUpKey = _shortcutSettings.NavigateUpKey,
                CreateFolderKey = _shortcutSettings.CreateFolderKey,
                DeleteKey = _shortcutSettings.DeleteKey,
                ThemeMode = _shortcutSettings.ThemeMode,
                ShowSelectedFileSize = _shortcutSettings.ShowSelectedFileSize,
                ShowSelectedFolderSize = _shortcutSettings.ShowSelectedFolderSize,
                ShowHiddenSystemItems = _shortcutSettings.ShowHiddenSystemItems,
                DefaultTerminalShellKind = _shortcutSettings.DefaultTerminalShellKind,
                DefaultTerminalWorkingDirectoryMode = _shortcutSettings.DefaultTerminalWorkingDirectoryMode,
                DefaultTerminalCustomWorkingDirectory = _shortcutSettings.DefaultTerminalCustomWorkingDirectory,
                HiddenDrivePaths = _hiddenDrivePaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                LeftPanePath = null,
                RightPanePath = null,
                WindowPlacement = null,
                Account = null,
                FileBunker = BuildFileBunkerSettingsConfig(),
                Logging = null,
                ToolbarCommands = BuildToolbarCommandConfigs(),
                BackupAutomations = null,
                AutoExtractProfiles = null,
                Groups = null,
            };
        }

        private void SaveAppSettings()
        {
            SaveSyncSettingsSections(config =>
            {
                var current = BuildSyncSettingsConfig();
                config.CopyToOtherPaneKey = current.CopyToOtherPaneKey;
                config.MoveToOtherPaneKey = current.MoveToOtherPaneKey;
                config.NavigateUpKey = current.NavigateUpKey;
                config.CreateFolderKey = current.CreateFolderKey;
                config.DeleteKey = current.DeleteKey;
                config.ThemeMode = current.ThemeMode;
                config.ShowSelectedFileSize = current.ShowSelectedFileSize;
                config.ShowSelectedFolderSize = current.ShowSelectedFolderSize;
                config.ShowHiddenSystemItems = current.ShowHiddenSystemItems;
                config.DefaultTerminalShellKind = current.DefaultTerminalShellKind;
                config.DefaultTerminalWorkingDirectoryMode = current.DefaultTerminalWorkingDirectoryMode;
                config.DefaultTerminalCustomWorkingDirectory = current.DefaultTerminalCustomWorkingDirectory;
                config.HiddenDrivePaths = current.HiddenDrivePaths;
                config.FileBunker = current.FileBunker;
                config.ToolbarCommands = current.ToolbarCommands;
            });
        }

        private LocalSettingsConfig BuildLocalSettingsConfig()
        {
            return new LocalSettingsConfig
            {
                Account = BuildAccountSettingsConfig(),
                BackupAutomations = BuildBackupAutomationConfigs(),
                AutoExtractProfiles = BuildAutoExtractConfigs(),
                Groups = BuildGroupConfigs(),
                LeftPanePath = LeftPane.CurrentPath,
                RightPanePath = RightPane.CurrentPath,
                WindowPlacement = BuildWindowPlacementConfig(),
                Logging = BuildLoggingSettingsConfig(),
                LastLocalBackupText = _lastLocalBackupText,
            };
        }

        private void SaveLocalSettings()
        {
            SaveLocalSettingsSections(config =>
            {
                var current = BuildLocalSettingsConfig();
                config.Account = current.Account;
                config.BackupAutomations = current.BackupAutomations;
                config.AutoExtractProfiles = current.AutoExtractProfiles;
                config.Groups = current.Groups;
                config.LeftPanePath = current.LeftPanePath;
                config.RightPanePath = current.RightPanePath;
                config.WindowPlacement = current.WindowPlacement;
                config.Logging = current.Logging;
                config.LastLocalBackupText = current.LastLocalBackupText;
            });
        }

        private void SaveNotificationHistoriesSafe(bool mergeExistingRecords = true)
        {
            try
            {
                SaveNotificationHistories(mergeExistingRecords);
            }
            catch
            {
            }
        }

        private WindowPlacementConfig BuildWindowPlacementConfig()
        {
            return new WindowPlacementConfig
            {
                X = AppWindow.Position.X,
                Y = AppWindow.Position.Y,
                Width = AppWindow.Size.Width,
                Height = AppWindow.Size.Height,
            };
        }

        private AccountSettingsConfig BuildAccountSettingsConfig()
        {
            return new AccountSettingsConfig
            {
                ApiBaseUrl = NormalizeApiBaseUrl(_accountSettings.ApiBaseUrl),
                Email = _accountSettings.Email,
                Token = _accountSettings.Token,
                UserDisplayName = _accountSettings.UserDisplayName,
                ServiceAccountsSummary = _accountSettings.ServiceAccountsSummary,
                PayloadJson = _accountSettings.PayloadJson,
                ServiceAccountsJson = _accountSettings.ServiceAccountsJson,
                LastLoginText = _accountSettings.LastLoginText,
                LastStatusText = _accountSettings.LastStatusText,
            };
        }

        private FileBunkerSettingsConfig BuildFileBunkerSettingsConfig()
        {
            return new FileBunkerSettingsConfig
            {
                InputEndpoint = _fileBunkerSettings.InputEndpoint,
                OutputEndpointBase = _fileBunkerSettings.OutputEndpointBase,
                ApiKey = _fileBunkerSettings.ApiKey,
                KeyLength = Math.Max(1, _fileBunkerSettings.KeyLength),
                ClientId = _fileBunkerSettings.ClientId,
                DaysToExpiration = Math.Max(1, _fileBunkerSettings.DaysToExpiration),
                DaysToPurge = Math.Max(1, _fileBunkerSettings.DaysToPurge),
            };
        }

        private LoggingSettingsConfig BuildLoggingSettingsConfig()
        {
            return new LoggingSettingsConfig
            {
                LogDirectoryPath = NormalizeLogDirectoryPath(_loggingSettings.LogDirectoryPath),
            };
        }

        private List<ToolbarCommandConfig> BuildToolbarCommandConfigs()
        {
            return ToolbarCommands
                .Select(item => new ToolbarCommandConfig
                {
                    Id = item.Id,
                    Title = item.Title,
                    Command = item.Command,
                    IconPath = item.IconPath,
                    IconGlyph = item.IconGlyph,
                    NodeDockerUser = item.NodeDockerUser,
                    NodeDockerHost = item.NodeDockerHost,
                    NodeDockerRemoteDirectory = item.NodeDockerRemoteDirectory,
                    NodeDockerLaunchMode = item.NodeDockerLaunchMode,
                    TerminalShellKind = item.TerminalShellKind,
                    TerminalWorkingDirectoryMode = item.TerminalWorkingDirectoryMode,
                    TerminalCustomWorkingDirectory = item.TerminalCustomWorkingDirectory,
                    TerminalLaunchArguments = item.TerminalLaunchArguments,
                })
                .ToList();
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

        private List<BackupAutomationConfig> BuildBackupAutomationConfigs()
        {
            return BackupAutomations
                .Select(profile => new BackupAutomationConfig
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    JobType = profile.JobType,
                    SourcePath = profile.SourcePath,
                    DestinationPath = profile.DestinationPath,
                    Mode = profile.Mode,
                    ExcludedFolderNamesText = profile.ExcludedFolderNamesText,
                    LogDirectoryPath = ResolveBackupAutomationLogDirectoryPath(profile.LogDirectoryPath),
                    MongoToolPath = profile.MongoToolPath,
                    MongoConnectionString = profile.MongoConnectionString,
                    MongoDatabaseName = profile.MongoDatabaseName,
                    MongoUseGzip = profile.MongoUseGzip,
                    MongoUseArchive = profile.MongoUseArchive,
                    MongoRetentionCount = Math.Max(1, profile.MongoRetentionCount),
                    ScheduleType = profile.ScheduleType,
                    IntervalMinutes = Math.Max(1, profile.IntervalMinutes),
                    ScheduleTimeText = profile.ScheduleTimeText,
                    WeeklyDaysMask = profile.WeeklyDaysMask,
                    RunMissedOnStartup = profile.RunMissedOnStartup,
                    IsEnabled = profile.IsEnabled,
                    NotificationEnabled = profile.NotificationEnabled,
                    ToastEnabled = profile.ToastEnabled,
                    LastRunText = profile.LastRunText,
                    LastResultText = profile.LastResultText,
                })
                .ToList();
        }

        private List<AutoExtractProfileConfig> BuildAutoExtractConfigs()
        {
            return AutoExtractProfiles
                .Select(profile => new AutoExtractProfileConfig
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    WatchPath = profile.WatchPath,
                    ExtractorPath = profile.ExtractorPath,
                    ExtensionFilter = profile.ExtensionFilter,
                    Passwords = profile.Passwords
                        .Select(item => item.Value.Trim())
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.Ordinal)
                        .ToList(),
                    IsEnabled = profile.IsEnabled,
                    NotificationEnabled = profile.NotificationEnabled,
                    ToastEnabled = profile.ToastEnabled,
                    LastRunText = profile.LastRunText,
                    LastResultText = profile.LastResultText,
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

        private async void SaveLocalSettingsSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveLocalSettingsSections(config =>
                {
                    config.Account = BuildAccountSettingsConfig();
                });
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("儲存本機設定失敗", ex.Message);
            }
        }

        private void SaveAutomationProfilesSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveLocalSettingsSections(config =>
                {
                    config.BackupAutomations = BuildBackupAutomationConfigs();
                });
            }
            catch
            {
            }
        }

        private void SaveAutoExtractProfilesSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveLocalSettingsSections(config =>
                {
                    config.AutoExtractProfiles = BuildAutoExtractConfigs();
                });
            }
            catch
            {
            }
        }


        private void SavePanePathsSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveLocalSettingsSections(config =>
                {
                    config.LeftPanePath = LeftPane.CurrentPath;
                    config.RightPanePath = RightPane.CurrentPath;
                    config.WindowPlacement = BuildWindowPlacementConfig();
                });
            }
            catch
            {
            }
        }

        private static WindowPlacementConfig? LoadSavedWindowPlacement()
        {
            try
            {
                var settings = LoadLocalSettingsConfig();
                var placement = settings?.WindowPlacement;
                if (placement is null)
                {
                    return null;
                }

                if (placement.Width <= 0 || placement.Height <= 0)
                {
                    return null;
                }

                return new WindowPlacementConfig
                {
                    X = placement.X,
                    Y = placement.Y,
                    Width = placement.Width,
                    Height = placement.Height,
                };
            }
            catch
            {
                return null;
            }
        }

        private static (string LeftPath, string RightPath) LoadSavedPanePaths()
        {
            try
            {
                var settings = LoadLocalSettingsConfig();
                if (settings is null)
                {
                    return default;
                }

                return (settings.LeftPanePath ?? string.Empty, settings.RightPanePath ?? string.Empty);
            }
            catch
            {
                return default;
            }
        }

        private static bool AddPathToGroup(PathGroup group, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsNavigableDirectoryPath(path))
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

        internal static string GetDisplayName(string path)
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

        private static JsonElement ReadProperty(JsonElement root, string camelCaseName, string propertyName)
        {
            if (root.TryGetProperty(camelCaseName, out var property))
            {
                return property;
            }

            if (root.TryGetProperty(propertyName, out property))
            {
                return property;
            }

            return default;
        }

        private static AppThemeMode ReadThemeMode(JsonElement property, AppThemeMode fallback)
        {
            if (property.ValueKind == JsonValueKind.String
                && Enum.TryParse<AppThemeMode>(property.GetString(), true, out var themeMode))
            {
                return themeMode;
            }

            if (property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out var numericValue)
                && Enum.IsDefined(typeof(AppThemeMode), numericValue))
            {
                return (AppThemeMode)numericValue;
            }

            return fallback;
        }

        private static TEnum ReadEnumSetting<TEnum>(JsonElement property, TEnum fallback)
            where TEnum : struct, Enum
        {
            if (property.ValueKind == JsonValueKind.String
                && Enum.TryParse<TEnum>(property.GetString(), true, out var enumValue))
            {
                return enumValue;
            }

            if (property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out var numericValue)
                && Enum.IsDefined(typeof(TEnum), numericValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
            }

            return fallback;
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

        private static int ReadIntSetting(JsonElement root, string camelCaseName, string propertyName, int fallback)
        {
            if (root.TryGetProperty(camelCaseName, out var property))
            {
                return ReadIntValue(property, fallback);
            }

            if (root.TryGetProperty(propertyName, out property))
            {
                return ReadIntValue(property, fallback);
            }

            return fallback;
        }

        private static List<string> ReadStringListSetting(JsonElement root, string camelCaseName, string propertyName)
        {
            if (root.TryGetProperty(camelCaseName, out var property))
            {
                return ReadStringListValue(property);
            }

            if (root.TryGetProperty(propertyName, out property))
            {
                return ReadStringListValue(property);
            }

            return new List<string>();
        }

        private static List<string> ReadStringListValue(JsonElement property)
        {
            if (property.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return property.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()?.Trim() ?? string.Empty)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        private static int ReadIntValue(JsonElement property, int fallback)
        {
            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt32(out var value) => value,
                JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
                _ => fallback,
            };
        }
    }
}
