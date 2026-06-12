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
        private void AddAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            var name = AutomationNameTextBox.Text.Trim();
            var sourcePath = AutomationSourcePathTextBox.Text.Trim();
            var destinationPath = AutomationDestinationPathTextBox.Text.Trim();
            var intervalRaw = AutomationIntervalTextBox.Text.Trim();
            var mode = GetAutomationModeFromComboBox(AutomationModeComboBox);

            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            {
                _ = ShowMessageAsync("新增備份工作失敗", "來源與目的地都必須填寫。");
                return;
            }

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                _ = ShowMessageAsync("新增備份工作失敗", "來源路徑不存在。");
                return;
            }

            if (!int.TryParse(intervalRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalMinutes) || intervalMinutes <= 0)
            {
                _ = ShowMessageAsync("新增備份工作失敗", "間隔分鐘必須是大於 0 的整數。");
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = GetDisplayName(sourcePath);
            }

            var profile = new BackupAutomationProfile
            {
                Name = name,
                SourcePath = sourcePath,
                DestinationPath = destinationPath,
                Mode = mode,
                IntervalMinutes = intervalMinutes,
                IsEnabled = true,
                LastRunText = "尚未執行",
                LastResultText = "等待排程",
            };

            profile.SyncIntervalText();
            BackupAutomations.Add(profile);
            SaveAutomationProfilesSafe();
            ActivateAutomation(profile);
            UpdateSharedStatusBar();

            AutomationNameTextBox.Text = string.Empty;
            AutomationSourcePathTextBox.Text = string.Empty;
            AutomationDestinationPathTextBox.Text = string.Empty;
            AutomationModeComboBox.SelectedIndex = 0;
            AutomationIntervalTextBox.Text = "60";
        }

        private async void RunAutomationNow_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile))
            {
                return;
            }

            await RunBackupAutomationAsync(profile, triggeredByTimer: false);
        }

        private async void StartAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile))
            {
                return;
            }

            profile.IsEnabled = true;
            ActivateAutomation(profile);
            profile.LastResultText = profile.Mode == BackupAutomationMode.Mirror
                ? "監聽已啟動"
                : "排程已啟動";
            SaveAutomationProfilesSafe();
            UpdateSharedStatusBar();

            await RunBackupAutomationAsync(profile, triggeredByTimer: false);
        }

        private void DeleteAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile))
            {
                return;
            }

            StopAutomationTimer(profile.Id);
            StopAutomationWatcher(profile.Id);
            BackupAutomations.Remove(profile);
            SaveAutomationProfilesSafe();
            UpdateSharedStatusBar();
        }

        private void StopAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile))
            {
                return;
            }

            RequestAutomationStop(profile);
            profile.IsEnabled = false;
            StopAutomationTimer(profile.Id);
            StopAutomationWatcher(profile.Id);
            profile.LastResultText = profile.Mode == BackupAutomationMode.Mirror
                ? "監聽已停止"
                : "排程已停止";
            SaveAutomationProfilesSafe();
            UpdateSharedStatusBar();
        }

        private void AutomationEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggleSwitch || !TryGetBackupAutomationProfile(toggleSwitch, out var profile))
            {
                return;
            }

            profile.IsEnabled = toggleSwitch.IsOn;
            profile.LastResultText = profile.IsEnabled ? "等待排程" : "已停用";
            SaveAutomationProfilesSafe();
            if (profile.IsEnabled)
            {
                ActivateAutomation(profile);
            }
            else
            {
                StopAutomationTimer(profile.Id);
                StopAutomationWatcher(profile.Id);
            }
            UpdateSharedStatusBar();
        }

        private void AutomationNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.Name = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        private void AutomationSourcePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.SourcePath = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        private void AutomationDestinationPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.DestinationPath = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        private void AutomationModeComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox || !TryGetBackupAutomationProfile(comboBox, out var profile))
            {
                return;
            }

            comboBox.SelectedIndex = profile.Mode == BackupAutomationMode.Mirror ? 1 : 0;
        }

        private void AutomationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox || !TryGetBackupAutomationProfile(comboBox, out var profile))
            {
                return;
            }

            var mode = GetAutomationModeFromComboBox(comboBox);
            if (profile.Mode == mode)
            {
                return;
            }

            profile.Mode = mode;
            profile.LastResultText = profile.IsEnabled
                ? (mode == BackupAutomationMode.Mirror ? "監聽待命 / 等待校正排程" : "等待排程")
                : "已停用";
            SaveAutomationProfilesSafe();
            if (profile.IsEnabled)
            {
                ActivateAutomation(profile);
            }
            UpdateSharedStatusBar();
        }

        private static BackupAutomationMode GetAutomationModeFromComboBox(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
                Enum.TryParse<BackupAutomationMode>(tag, true, out var mode))
            {
                return mode;
            }

            return BackupAutomationMode.Copy;
        }

        private void AutomationIntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.IntervalMinutesText = textBox.Text.Trim();
            if (int.TryParse(profile.IntervalMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
            {
                profile.IntervalMinutes = minutes;
                profile.LastResultText = profile.IsEnabled
                    ? (profile.Mode == BackupAutomationMode.Mirror ? "監聽待命 / 等待校正排程" : "等待排程")
                    : "已停用";
                SaveAutomationProfilesSafe();
                if (profile.IsEnabled)
                {
                    ActivateAutomation(profile);
                }
                UpdateSharedStatusBar();
            }
        }

        private void AddAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            var watchPath = AutoExtractWatchPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(watchPath))
            {
                _ = ShowMessageAsync("新增自動解壓失敗", "監看目錄不能為空。");
                return;
            }

            if (!Directory.Exists(watchPath))
            {
                _ = ShowMessageAsync("新增自動解壓失敗", $"監看目錄不存在：{watchPath}");
                return;
            }

            var profile = new AutoExtractProfile
            {
                Name = string.IsNullOrWhiteSpace(AutoExtractNameTextBox.Text)
                    ? GetDisplayName(watchPath)
                    : AutoExtractNameTextBox.Text.Trim(),
                WatchPath = watchPath,
                ExtractorPath = AutoExtractExtractorPathTextBox.Text.Trim(),
                IsEnabled = true,
                LastRunText = "尚未執行",
                LastResultText = "監看待命",
            };

            AutoExtractProfiles.Add(profile);
            SaveAutoExtractProfilesSafe();
            ActivateAutoExtractProfile(profile);

            AutoExtractNameTextBox.Text = string.Empty;
            AutoExtractWatchPathTextBox.Text = string.Empty;
            AutoExtractExtractorPathTextBox.Text = string.Empty;
        }

        private async void RunAutoExtractNow_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile))
            {
                return;
            }

            await RunAutoExtractProfileAsync(profile, triggeredByWatcher: false);
        }

        private async void StartAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile))
            {
                return;
            }

            profile.IsEnabled = true;
            ActivateAutoExtractProfile(profile);
            profile.LastResultText = "監看已啟動";
            SaveAutoExtractProfilesSafe();

            await RunAutoExtractProfileAsync(profile, triggeredByWatcher: false);
        }

        private void StopAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile))
            {
                return;
            }

            RequestAutoExtractStop(profile);
            profile.IsEnabled = false;
            StopAutoExtractWatcher(profile.Id);
            profile.LastResultText = "監看已停止";
            SaveAutoExtractProfilesSafe();
        }

        private void DeleteAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile))
            {
                return;
            }

            RequestAutoExtractStop(profile);
            StopAutoExtractWatcher(profile.Id);
            AutoExtractProfiles.Remove(profile);
            SaveAutoExtractProfilesSafe();
        }

        private void AutoExtractEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggleSwitch || !TryGetAutoExtractProfile(toggleSwitch, out var profile))
            {
                return;
            }

            profile.IsEnabled = toggleSwitch.IsOn;
            profile.LastResultText = profile.IsEnabled ? "監看待命" : "已停用";
            SaveAutoExtractProfilesSafe();
            if (profile.IsEnabled)
            {
                ActivateAutoExtractProfile(profile);
            }
            else
            {
                RequestAutoExtractStop(profile);
                StopAutoExtractWatcher(profile.Id);
            }
        }

        private void AutoExtractNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.Name = textBox.Text.Trim();
            SaveAutoExtractProfilesSafe();
        }

        private void AutoExtractWatchPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.WatchPath = textBox.Text.Trim();
            SaveAutoExtractProfilesSafe();
            if (profile.IsEnabled)
            {
                ActivateAutoExtractProfile(profile);
            }
        }

        private void AutoExtractExtractorPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.ExtractorPath = textBox.Text.Trim();
            SaveAutoExtractProfilesSafe();
        }

        private void AddAutoExtractPassword_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile))
            {
                return;
            }

            var password = profile.PendingPasswordText.Trim();
            if (string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            if (profile.Passwords.Any(item => string.Equals(item.Value, password, StringComparison.Ordinal)))
            {
                return;
            }

            profile.Passwords.Add(new AutoExtractPasswordItem
            {
                Value = password,
                ParentProfile = profile,
            });
            profile.PendingPasswordText = string.Empty;
            SaveAutoExtractProfilesSafe();
        }

        private void RemoveAutoExtractPassword_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: AutoExtractPasswordItem item } || item.ParentProfile is null)
            {
                return;
            }

            item.ParentProfile.Passwords.Remove(item);
            SaveAutoExtractProfilesSafe();
        }

        private void AutoExtractPasswordItemTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox { DataContext: AutoExtractPasswordItem item })
            {
                return;
            }

            item.Value = sender is TextBox textBox ? textBox.Text : item.Value;
            SaveAutoExtractProfilesSafe();
        }

        private static bool TryGetAutoExtractProfile(object sender, out AutoExtractProfile profile)
        {
            if (sender is FrameworkElement { Tag: AutoExtractProfile taggedProfile })
            {
                profile = taggedProfile;
                return true;
            }

            if (sender is FrameworkElement { DataContext: AutoExtractProfile dataContextProfile })
            {
                profile = dataContextProfile;
                return true;
            }

            profile = null!;
            return false;
        }

        private void LoadBackupAutomations()
        {
            BackupAutomations.Clear();

            try
            {
                if (!File.Exists(SettingsConfigPath))
                {
                    return;
                }

                var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsConfigPath), JsonOptions);
                if (settings?.BackupAutomations is null)
                {
                    return;
                }

                foreach (var config in settings.BackupAutomations)
                {
                    var profile = new BackupAutomationProfile
                    {
                        Id = config.Id == Guid.Empty ? Guid.NewGuid() : config.Id,
                        Name = config.Name,
                        SourcePath = config.SourcePath,
                        DestinationPath = config.DestinationPath,
                        Mode = config.Mode,
                        IntervalMinutes = Math.Max(1, config.IntervalMinutes),
                        IsEnabled = config.IsEnabled,
                        LastRunText = string.IsNullOrWhiteSpace(config.LastRunText) ? "尚未執行" : config.LastRunText,
                        LastResultText = string.IsNullOrWhiteSpace(config.LastResultText) ? (config.IsEnabled ? "等待排程" : "已停用") : config.LastResultText,
                    };
                    profile.SyncIntervalText();
                    BackupAutomations.Add(profile);
                }
            }
            catch
            {
            }
        }

        private void LoadAutoExtractProfiles()
        {
            AutoExtractProfiles.Clear();

            try
            {
                if (!File.Exists(SettingsConfigPath))
                {
                    return;
                }

                var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsConfigPath), JsonOptions);
                if (settings?.AutoExtractProfiles is null)
                {
                    return;
                }

                foreach (var config in settings.AutoExtractProfiles)
                {
                    var profile = new AutoExtractProfile
                    {
                        Id = config.Id == Guid.Empty ? Guid.NewGuid() : config.Id,
                        Name = config.Name,
                        WatchPath = config.WatchPath,
                        ExtractorPath = config.ExtractorPath,
                        IsEnabled = config.IsEnabled,
                        LastRunText = string.IsNullOrWhiteSpace(config.LastRunText) ? "尚未執行" : config.LastRunText,
                        LastResultText = string.IsNullOrWhiteSpace(config.LastResultText)
                            ? (config.IsEnabled ? "監看待命" : "已停用")
                            : config.LastResultText,
                    };

                    foreach (var password in BuildLegacyPasswordEntries(config))
                    {
                        profile.Passwords.Add(new AutoExtractPasswordItem
                        {
                            Value = password,
                            ParentProfile = profile,
                        });
                    }

                    AutoExtractProfiles.Add(profile);
                }
            }
            catch
            {
            }
        }

        private void RescheduleBackupAutomations()
        {
            StopAllAutomationTimers();
            StopAllAutomationWatchers();

            foreach (var profile in BackupAutomations)
            {
                ActivateAutomation(profile);
            }
        }

        private void RescheduleAutoExtractProfiles()
        {
            StopAllAutoExtractWatchers();

            foreach (var profile in AutoExtractProfiles)
            {
                ActivateAutoExtractProfile(profile);
            }
        }

        private void ActivateAutomation(BackupAutomationProfile profile)
        {
            ScheduleBackupAutomation(profile);
            if (profile.Mode == BackupAutomationMode.Mirror)
            {
                StartAutomationWatcher(profile);
            }
            else
            {
                StopAutomationWatcher(profile.Id);
            }
        }

        private void ScheduleBackupAutomation(BackupAutomationProfile profile)
        {
            StopAutomationTimer(profile.Id);

            if (!profile.IsEnabled || profile.IntervalMinutes <= 0)
            {
                profile.LastResultText = profile.IsEnabled ? "間隔設定無效" : "已停用";
                return;
            }

            var timer = new System.Timers.Timer(TimeSpan.FromMinutes(profile.IntervalMinutes).TotalMilliseconds)
            {
                AutoReset = true,
                Enabled = true,
            };

            timer.Elapsed += async (_, _) => await RunBackupAutomationAsync(profile, triggeredByTimer: true);
            _automationTimers[profile.Id] = timer;
            profile.LastResultText = profile.Mode == BackupAutomationMode.Mirror
                ? "監聽待命 / 等待校正排程"
                : "等待排程";
        }

        private void StopAutomationTimer(Guid profileId)
        {
            if (!_automationTimers.Remove(profileId, out var timer))
            {
                return;
            }

            timer.Stop();
            timer.Dispose();
        }

        private void StopAllAutomationTimers()
        {
            foreach (var timer in _automationTimers.Values)
            {
                timer.Stop();
                timer.Dispose();
            }

            _automationTimers.Clear();
        }

        private void StartAutomationWatcher(BackupAutomationProfile profile)
        {
            StopAutomationWatcher(profile.Id);

            if (!profile.IsEnabled)
            {
                return;
            }

            var watcher = new BackupAutomationSourceWatcher(
                profile,
                DispatcherQueue,
                () => _ = RunBackupAutomationAsync(profile, triggeredByTimer: true),
                AutomationWatcherDebounceInterval);
            watcher.Start();
            _automationWatchers[profile.Id] = watcher;
        }

        private void StopAutomationWatcher(Guid profileId)
        {
            if (!_automationWatchers.Remove(profileId, out var watcher))
            {
                return;
            }

            watcher.Dispose();
        }

        private void StopAllAutomationWatchers()
        {
            foreach (var watcher in _automationWatchers.Values)
            {
                watcher.Dispose();
            }

            _automationWatchers.Clear();
        }

        private void RequestAutomationStop(BackupAutomationProfile profile)
        {
            if (_automationCancellationTokens.TryGetValue(profile.Id, out var cancellationTokenSource))
            {
                cancellationTokenSource.Cancel();
                UpdateSharedStatusBar();
            }
        }

        private void ActivateAutoExtractProfile(AutoExtractProfile profile)
        {
            StopAutoExtractWatcher(profile.Id);

            if (!profile.IsEnabled)
            {
                profile.LastResultText = "已停用";
                return;
            }

            var watcher = new AutoExtractProfileWatcher(
                profile,
                DispatcherQueue,
                () => _ = RunAutoExtractProfileAsync(profile, triggeredByWatcher: true),
                AutomationWatcherDebounceInterval);
            watcher.Start();
            _autoExtractWatchers[profile.Id] = watcher;
            profile.LastResultText = "監看待命";
        }

        private void StopAutoExtractWatcher(Guid profileId)
        {
            if (!_autoExtractWatchers.Remove(profileId, out var watcher))
            {
                return;
            }

            watcher.Dispose();
        }

        private void StopAllAutoExtractWatchers()
        {
            foreach (var watcher in _autoExtractWatchers.Values)
            {
                watcher.Dispose();
            }

            _autoExtractWatchers.Clear();
        }

        private void RequestAutoExtractStop(AutoExtractProfile profile)
        {
            if (_autoExtractCancellationTokens.TryGetValue(profile.Id, out var cancellationTokenSource))
            {
                cancellationTokenSource.Cancel();
            }
        }

        private void CancelAllAutoExtractOperations()
        {
            foreach (var cancellationTokenSource in _autoExtractCancellationTokens.Values.ToList())
            {
                cancellationTokenSource.Cancel();
            }
        }

        private async Task RunBackupAutomationAsync(BackupAutomationProfile profile, bool triggeredByTimer)
        {
            lock (_runningAutomationIds)
            {
                if (!_runningAutomationIds.Add(profile.Id))
                {
                    return;
                }
            }

            var cancellationTokenSource = new CancellationTokenSource();
            _automationCancellationTokens[profile.Id] = cancellationTokenSource;
            var backgroundWorkId = BeginBackgroundWork($"{profile.Name} {(profile.Mode == BackupAutomationMode.Mirror ? "同步中" : "備份中")}");

            await EnqueueOnUiAsync(() =>
            {
                profile.IsRunning = true;
                profile.LastResultText = triggeredByTimer ? "背景執行中..." : "手動執行中...";
                UpdateSharedStatusBar();
            });

            try
            {
                await Task.Run(() => ExecuteBackupAutomation(profile, cancellationTokenSource.Token), cancellationTokenSource.Token);

                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = profile.Mode == BackupAutomationMode.Mirror ? "同步完成" : "備份完成";
                    UpdateSharedStatusBar();
                });
            }
            catch (OperationCanceledException)
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = "已停止";
                    UpdateSharedStatusBar();
                });
            }
            catch (Exception ex)
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = $"失敗: {ex.Message}";
                    UpdateSharedStatusBar();
                });
            }
            finally
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.IsRunning = false;
                    SaveAutomationProfilesSafe();
                    UpdateSharedStatusBar();
                });

                if (_automationCancellationTokens.Remove(profile.Id, out var activeCancellationTokenSource))
                {
                    activeCancellationTokenSource.Dispose();
                }

                CompleteBackgroundWork(backgroundWorkId);

                lock (_runningAutomationIds)
                {
                    _runningAutomationIds.Remove(profile.Id);
                }
            }
        }

        private static void ExecuteBackupAutomation(BackupAutomationProfile profile, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(profile.SourcePath) || string.IsNullOrWhiteSpace(profile.DestinationPath))
            {
                throw new InvalidOperationException("來源或目的地未設定。");
            }

            Directory.CreateDirectory(profile.DestinationPath);

            if (File.Exists(profile.SourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationFile = Path.Combine(profile.DestinationPath, Path.GetFileName(profile.SourcePath));
                File.Copy(profile.SourcePath, destinationFile, overwrite: true);
                return;
            }

            if (!Directory.Exists(profile.SourcePath))
            {
                throw new DirectoryNotFoundException("來源不存在。");
            }

            if (profile.Mode == BackupAutomationMode.Mirror)
            {
                SyncDirectoryMirror(profile.SourcePath, profile.DestinationPath, cancellationToken);
                return;
            }

            CopyDirectory(profile.SourcePath, profile.DestinationPath, cancellationToken);
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationDirectory);

            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Copy(file, destinationFile, overwrite: true);
            }

            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(directory);
                if (attributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
                {
                    continue;
                }

                var childDestination = Path.Combine(destinationDirectory, Path.GetFileName(directory));
                CopyDirectory(directory, childDestination, cancellationToken);
            }
        }

        private static void SyncDirectoryMirror(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationDirectory);

            var sourceFiles = Directory.EnumerateFiles(sourceDirectory)
                .Select(filePath => new
                {
                    Name = Path.GetFileName(filePath),
                    Path = filePath,
                })
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToDictionary(entry => entry.Name, entry => entry.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var sourceFile in sourceFiles.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, destinationFile, overwrite: true);
            }

            foreach (var destinationFile in Directory.EnumerateFiles(destinationDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(destinationFile);
                if (!sourceFiles.ContainsKey(fileName))
                {
                    File.Delete(destinationFile);
                }
            }

            var sourceDirectories = Directory.EnumerateDirectories(sourceDirectory)
                .Where(static directory =>
                {
                    var attributes = File.GetAttributes(directory);
                    return !attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
                })
                .Select(directoryPath => new
                {
                    Name = Path.GetFileName(directoryPath),
                    Path = directoryPath,
                })
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToDictionary(entry => entry.Name, entry => entry.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var sourceChildDirectory in sourceDirectories.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationChildDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceChildDirectory));
                SyncDirectoryMirror(sourceChildDirectory, destinationChildDirectory, cancellationToken);
            }

            foreach (var destinationChildDirectory in Directory.EnumerateDirectories(destinationDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directoryName = Path.GetFileName(destinationChildDirectory);
                if (!sourceDirectories.ContainsKey(directoryName))
                {
                    Directory.Delete(destinationChildDirectory, recursive: true);
                }
            }
        }

        private async Task RunAutoExtractProfileAsync(AutoExtractProfile profile, bool triggeredByWatcher)
        {
            string? retryPassword = null;

            lock (_runningAutoExtractIds)
            {
                if (!_runningAutoExtractIds.Add(profile.Id))
                {
                    return;
                }
            }

            var cancellationTokenSource = new CancellationTokenSource();
            _autoExtractCancellationTokens[profile.Id] = cancellationTokenSource;
            var backgroundWorkId = BeginBackgroundWork($"{profile.Name} 解壓中");

            await EnqueueOnUiAsync(() =>
            {
                profile.IsRunning = true;
                profile.LastResultText = triggeredByWatcher ? "偵測到新壓縮檔，處理中..." : "手動掃描中...";
            });

            try
            {
                var result = await Task.Run(() => ExecuteAutoExtractProfile(profile, cancellationTokenSource.Token), cancellationTokenSource.Token);

                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = result.StatusText;
                });

                if (result.ShouldShowPasswordMismatchDialog)
                {
                    retryPassword = await ShowAutoExtractPasswordPromptAsync("自動解壓失敗", result.PasswordMismatchDialogMessage);
                }
            }
            catch (OperationCanceledException)
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = "已停止";
                });
            }
            catch (Exception ex)
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = $"失敗：{ex.Message}";
                });
            }
            finally
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.IsRunning = false;
                    SaveAutoExtractProfilesSafe();
                });

                if (_autoExtractCancellationTokens.Remove(profile.Id, out var activeCancellationTokenSource))
                {
                    activeCancellationTokenSource.Dispose();
                }

                CompleteBackgroundWork(backgroundWorkId);

                lock (_runningAutoExtractIds)
                {
                    _runningAutoExtractIds.Remove(profile.Id);
                }

                if (!string.IsNullOrWhiteSpace(retryPassword))
                {
                    var normalizedPassword = retryPassword.Trim();
                    await EnqueueOnUiAsync(() =>
                    {
                        if (!profile.Passwords.Any(item => string.Equals(item.Value, normalizedPassword, StringComparison.Ordinal)))
                        {
                            profile.Passwords.Add(new AutoExtractPasswordItem
                            {
                                Value = normalizedPassword,
                                ParentProfile = profile,
                            });
                            SaveAutoExtractProfilesSafe();
                        }
                    });

                    await RunAutoExtractProfileAsync(profile, triggeredByWatcher: false);
                }
            }
        }

        private static AutoExtractExecutionResult ExecuteAutoExtractProfile(AutoExtractProfile profile, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(profile.WatchPath))
            {
                throw new InvalidOperationException("監看目錄未設定。");
            }

            if (!Directory.Exists(profile.WatchPath))
            {
                throw new DirectoryNotFoundException($"監看目錄不存在：{profile.WatchPath}");
            }

            var extractorExecutable = ResolveArchiveExtractorExecutable(profile.ExtractorPath);
            var archives = Directory.EnumerateFiles(profile.WatchPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedArchivePath)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (archives.Count == 0)
            {
                return AutoExtractExecutionResult.Create("監看目錄內沒有壓縮檔");
            }

            var passwords = BuildPasswordCandidates(profile.Passwords);
            var extractedCount = 0;
            var skippedCount = 0;
            var failedNames = new List<string>();
            var passwordMismatchArchives = new List<string>();

            foreach (var archivePath in archives)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryPrepareArchiveExtraction(archivePath, out var destinationDirectory, out var skipReason))
                {
                    skippedCount++;
                    if (!string.IsNullOrWhiteSpace(skipReason))
                    {
                        failedNames.Add($"{Path.GetFileName(archivePath)}（{skipReason}）");
                    }
                    continue;
                }

                if (!WaitForArchiveReady(archivePath, cancellationToken))
                {
                    failedNames.Add($"{Path.GetFileName(archivePath)}（檔案仍在寫入）");
                    continue;
                }

                var extracted = TryExtractArchiveWithPasswords(
                    extractorExecutable,
                    archivePath,
                    destinationDirectory,
                    passwords,
                    cancellationToken,
                    out var allPasswordsRejected,
                    out var failureReason);

                if (extracted)
                {
                    extractedCount++;
                }
                else
                {
                    failedNames.Add($"{Path.GetFileName(archivePath)}（{failureReason}）");
                    if (allPasswordsRejected)
                    {
                        passwordMismatchArchives.Add(Path.GetFileName(archivePath));
                    }
                }
            }

            if (extractedCount == 0 && failedNames.Count == 0)
            {
                return AutoExtractExecutionResult.Create(skippedCount > 0 ? "沒有新的壓縮檔需要解壓" : "沒有可處理的壓縮檔");
            }

            if (failedNames.Count == 0)
            {
                return AutoExtractExecutionResult.Create($"完成：成功解壓 {extractedCount} 個壓縮檔");
            }

            var statusText = extractedCount > 0
                ? $"部分完成：成功 {extractedCount}，失敗 {failedNames.Count}"
                : $"全部失敗：{string.Join("、", failedNames.Take(3))}";

            if (passwordMismatchArchives.Count == 0)
            {
                return AutoExtractExecutionResult.Create(statusText);
            }

            var dialogMessage = passwordMismatchArchives.Count == 1
                ? $"壓縮檔「{passwordMismatchArchives[0]}」已嘗試所有密碼，但都不符合。"
                : $"以下壓縮檔已嘗試所有密碼，但都不符合：\n{string.Join("\n", passwordMismatchArchives.Take(6))}";

            return AutoExtractExecutionResult.Create(statusText, true, dialogMessage);
        }

        internal static bool IsSupportedArchivePath(string path)
        {
            return SupportedArchiveExtensions.Contains(Path.GetExtension(path));
        }

        private static string ResolveArchiveExtractorExecutable(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var candidate = configuredPath.Trim().Trim('"');
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                throw new FileNotFoundException("設定的解壓工具不存在。", candidate);
            }

            var candidates = new[]
            {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
                @"C:\Program Files\WinRAR\WinRAR.exe",
                @"C:\Program Files (x86)\WinRAR\WinRAR.exe",
            };

            var executable = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(executable))
            {
                return executable;
            }

            throw new InvalidOperationException("找不到解壓工具。請在設定填入 7z.exe 或 WinRAR.exe 路徑。");
        }

        private static List<string> BuildPasswordCandidates(IEnumerable<AutoExtractPasswordItem> passwordItems)
        {
            var candidates = passwordItems
                .Select(static item => item.Value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            candidates.Insert(0, string.Empty);
            return candidates;
        }

        private static IEnumerable<string> BuildLegacyPasswordEntries(AutoExtractProfileConfig config)
        {
            if (config.Passwords is { Count: > 0 })
            {
                return config.Passwords
                    .Select(static item => item.Trim())
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            var legacyValues = (config.PasswordListText ?? string.Empty)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return legacyValues;
        }

        private static bool TryPrepareArchiveExtraction(string archivePath, out string destinationDirectory, out string? skipReason)
        {
            destinationDirectory = string.Empty;
            skipReason = null;

            if (!File.Exists(archivePath))
            {
                skipReason = "檔案不存在";
                return false;
            }

            var directory = Path.GetDirectoryName(archivePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                skipReason = "找不到所在資料夾";
                return false;
            }

            destinationDirectory = Path.Combine(directory, Path.GetFileNameWithoutExtension(archivePath));
            if (!Directory.Exists(destinationDirectory))
            {
                return true;
            }

            if (Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
            {
                skipReason = "已存在解壓結果";
                return false;
            }

            Directory.Delete(destinationDirectory, recursive: true);
            return true;
        }

        private static bool WaitForArchiveReady(string archivePath, CancellationToken cancellationToken)
        {
            const int maxAttempts = 8;
            long? previousLength = null;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(archivePath);
                    if (!info.Exists)
                    {
                        return false;
                    }

                    using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (previousLength.HasValue && previousLength.Value == info.Length)
                    {
                        return true;
                    }

                    previousLength = info.Length;
                }
                catch
                {
                }

                Thread.Sleep(750);
            }

            return false;
        }

        private static bool TryExtractArchiveWithPasswords(
            string extractorExecutable,
            string archivePath,
            string destinationDirectory,
            IReadOnlyList<string> passwords,
            CancellationToken cancellationToken,
            out bool allPasswordsRejected,
            out string failureReason)
        {
            allPasswordsRejected = false;
            failureReason = "密碼不正確或解壓失敗";
            var tempDirectory = destinationDirectory + ".nuone_extracting";
            var sawPasswordFailure = false;

            foreach (var password in passwords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SafeDeleteDirectory(tempDirectory);
                Directory.CreateDirectory(tempDirectory);

                var success = ExecuteArchiveExtractionAttempt(
                    extractorExecutable,
                    archivePath,
                    tempDirectory,
                    password,
                    out var exitCode,
                    out var standardError);

                if (success && Directory.EnumerateFileSystemEntries(tempDirectory).Any())
                {
                    if (Directory.Exists(destinationDirectory) && !Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
                    {
                        SafeDeleteDirectory(destinationDirectory);
                    }

                    Directory.Move(tempDirectory, destinationDirectory);
                    try
                    {
                        File.Delete(archivePath);
                    }
                    catch
                    {
                    }

                    return true;
                }

                SafeDeleteDirectory(tempDirectory);
                failureReason = !string.IsNullOrWhiteSpace(standardError)
                    ? standardError
                    : $"結束代碼 {exitCode}";

                if (LooksLikePasswordRejected(extractorExecutable, exitCode, failureReason))
                {
                    sawPasswordFailure = true;
                }
            }

            allPasswordsRejected = sawPasswordFailure;
            return false;
        }

        private static bool LooksLikePasswordRejected(string extractorExecutable, int exitCode, string message)
        {
            var fileName = Path.GetFileName(extractorExecutable);
            if (fileName.StartsWith("winrar", StringComparison.OrdinalIgnoreCase) && exitCode == 11)
            {
                return true;
            }

            if (fileName.StartsWith("7z", StringComparison.OrdinalIgnoreCase) && exitCode == 2)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.Contains("wrong password", StringComparison.OrdinalIgnoreCase)
                || message.Contains("incorrect password", StringComparison.OrdinalIgnoreCase)
                || message.Contains("password is incorrect", StringComparison.OrdinalIgnoreCase)
                || message.Contains("can not open encrypted archive", StringComparison.OrdinalIgnoreCase)
                || message.Contains("headers error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("密碼", StringComparison.OrdinalIgnoreCase)
                || message.Contains("wrong pass", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ExecuteArchiveExtractionAttempt(
            string extractorExecutable,
            string archivePath,
            string destinationDirectory,
            string password,
            out int exitCode,
            out string standardError)
        {
            var fileName = Path.GetFileName(extractorExecutable);
            var isWinRar = fileName.StartsWith("winrar", StringComparison.OrdinalIgnoreCase);
            var passwordSwitch = isWinRar
                ? (string.IsNullOrEmpty(password) ? "-p-" : $"-p{password}")
                : $"-p{password}";
            var arguments = isWinRar
                ? $"x -y -ibck -inul {passwordSwitch} \"{archivePath}\" \"{destinationDirectory}\\\""
                : $"x \"{archivePath}\" -o\"{destinationDirectory}\" -y -bso0 -bsp0 -bse1 {passwordSwitch}";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = extractorExecutable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;

            if (string.IsNullOrWhiteSpace(standardError))
            {
                standardError = output.Trim();
            }

            return process.ExitCode == 0;
        }

        private static void SafeDeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
