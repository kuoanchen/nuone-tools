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
        private enum AutomationCreateKind
        {
            Backup,
            AutoExtract,
        }

        internal async void AddAutomationDialog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var kind = await ShowAutomationCreateKindDialogAsync();
                if (kind == null)
                {
                    return;
                }

                if (kind == AutomationCreateKind.Backup)
                {
                    await CreateBackupAutomationFromDialogAsync();
                    return;
                }

                await CreateAutoExtractProfileFromDialogAsync();
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "add automation dialog");
                await ShowMessageAsync("新增自動化工作失敗", ex.Message);
            }
        }

        private async Task<AutomationCreateKind?> ShowAutomationCreateKindDialogAsync()
        {
            var typeComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0,
                Items =
                {
                    new ComboBoxItem { Content = "備份工作", Tag = AutomationCreateKind.Backup },
                    new ComboBoxItem { Content = "自動解壓", Tag = AutomationCreateKind.AutoExtract },
                },
            };

            var panel = new StackPanel
            {
                Spacing = 10,
                Width = 420,
            };
            panel.Children.Add(new TextBlock
            {
                Text = "選擇要建立的自動化類型。",
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(typeComboBox);

            var dialog = new ContentDialog
            {
                Title = "新增自動化",
                Content = panel,
                PrimaryButtonText = "下一步",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            return typeComboBox.SelectedItem is ComboBoxItem { Tag: AutomationCreateKind kind }
                ? kind
                : AutomationCreateKind.Backup;
        }

        private async Task CreateBackupAutomationFromDialogAsync()
        {
            if (!EnsureAutomationExecutionOwner("新增自動化工作"))
            {
                return;
            }

            var profile = new BackupAutomationProfile
            {
                JobType = AutomationJobType.FileBackup,
                Mode = BackupAutomationMode.Copy,
                ExcludedFolderNamesText = ".vs, .vscode, .nuget, bin, obj, packages, node_modules",
                LogDirectoryPath = ResolveBackupAutomationLogDirectoryPath(null),
                MongoToolPath = @"C:\Program Files\MongoDB\Tools\100\bin\mongodump.exe",
                MongoUseArchive = true,
                MongoUseGzip = true,
                MongoRetentionCount = 7,
                MongoRetentionCountText = "7",
                ScheduleType = AutomationScheduleType.Interval,
                IntervalMinutes = 60,
                IntervalMinutesText = "60",
                ScheduleTimeText = "03:00",
                WeeklyDaysMask = 62,
                IsEnabled = true,
                LastRunText = "尚未執行",
                LastResultText = "等待排程",
                NextRunText = "計算中...",
            };

            if (!await ShowBackupAutomationEditorAsync(profile, "新增備份工作"))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = profile.JobType == AutomationJobType.MongoBackup
                    ? (!string.IsNullOrWhiteSpace(profile.MongoDatabaseName) ? $"MongoDB {profile.MongoDatabaseName}" : "MongoDB 備份")
                    : GetDisplayName(profile.SourcePath);
            }

            if (profile.JobType == AutomationJobType.MongoBackup && !profile.MongoUseArchive && !profile.MongoUseGzip)
            {
                profile.MongoUseArchive = true;
            }

            profile.SyncIntervalText();
            profile.SyncMongoRetentionText();
            profile.NextRunText = "計算中...";
            BackupAutomations.Add(profile);
            SaveAutomationProfilesSafe();
            ActivateAutomation(profile);
            UpdateSharedStatusBar();
            AddAutomationNotification("自動化", $"已新增備份工作：{profile.Name}", BuildBackupAutomationProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);
        }

        private async Task CreateAutoExtractProfileFromDialogAsync()
        {
            if (!EnsureAutomationExecutionOwner("新增自動解壓工作"))
            {
                return;
            }

            var profile = new AutoExtractProfile
            {
                ExtensionFilter = ".zip, .rar, .7z",
                IsEnabled = true,
                LastRunText = "尚未執行",
                LastResultText = "等待壓縮檔",
            };

            if (!await ShowAutoExtractProfileEditorAsync(profile, "新增自動解壓"))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = GetDisplayName(profile.WatchPath);
            }

            AutoExtractProfiles.Add(profile);
            SaveAutoExtractProfilesSafe();
            ActivateAutoExtractProfile(profile);
            UpdateSharedStatusBar();
            AddAutomationNotification("自動解壓", $"已新增自動解壓：{profile.Name}", BuildAutoExtractProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);
        }

        internal void AddAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            var jobType = GetAutomationJobTypeFromComboBox(AutomationJobTypeComboBox);
            var name = AutomationNameTextBox.Text.Trim();
            var sourcePath = AutomationSourcePathTextBox.Text.Trim();
            var destinationPath = AutomationDestinationPathTextBox.Text.Trim();
            var intervalRaw = AutomationIntervalTextBox.Text.Trim();
            var scheduleType = GetAutomationScheduleTypeFromComboBox(AutomationScheduleTypeComboBox);
            var scheduleTimeText = AutomationScheduleTimeTextBox.Text.Trim();
            var weeklyDaysMask = GetEntryWeeklyDaysMask();
            var runMissedOnStartup = AutomationRunMissedOnStartupCheckBox.IsChecked == true;
            var mode = GetAutomationModeFromComboBox(AutomationModeComboBox);
            var excludedFolderNamesText = string.Empty;
            var logDirectoryPath = ResolveBackupAutomationLogDirectoryPath(null);
            var mongoToolPath = AutomationMongoToolPathTextBox.Text.Trim();
            var mongoConnectionString = AutomationMongoConnectionStringTextBox.Text.Trim();
            var mongoDatabaseName = AutomationMongoDatabaseNameTextBox.Text.Trim();
            var mongoUseArchive = AutomationMongoUseArchiveCheckBox.IsChecked == true;
            var mongoUseGzip = AutomationMongoUseGzipCheckBox.IsChecked == true;
            var mongoRetentionRaw = AutomationMongoRetentionCountTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                RunFireAndForget(ShowMessageAsync("新增自動化工作失敗", "目的地必須填寫。"), "automation validation message");
                return;
            }

            if (jobType == AutomationJobType.FileBackup &&
                (string.IsNullOrWhiteSpace(sourcePath) || (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))))
            {
                RunFireAndForget(ShowMessageAsync("新增自動化工作失敗", "來源路徑不存在。"), "automation validation message");
                return;
            }

            if (jobType == AutomationJobType.MongoBackup && string.IsNullOrWhiteSpace(mongoConnectionString))
            {
                RunFireAndForget(ShowMessageAsync("新增自動化工作失敗", "Mongo URI 必須填寫。"), "automation validation message");
                return;
            }

            if (!int.TryParse(intervalRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalMinutes) || intervalMinutes <= 0)
            {
                RunFireAndForget(ShowMessageAsync("新增自動化工作失敗", "間隔分鐘必須是大於 0 的整數。"), "automation validation message");
                return;
            }

            if (scheduleType != AutomationScheduleType.Interval && !TryParseScheduleTimeText(scheduleTimeText, out _))
            {
                RunFireAndForget(ShowMessageAsync("新增自動化工作失敗", "時間格式必須是 HH:mm。"), "automation validation message");
                return;
            }

            if (!int.TryParse(mongoRetentionRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mongoRetentionCount) || mongoRetentionCount <= 0)
            {
                mongoRetentionCount = 7;
            }

            if (jobType == AutomationJobType.MongoBackup && !mongoUseArchive && !mongoUseGzip)
            {
                mongoUseArchive = true;
            }

            if (jobType == AutomationJobType.FileBackup && string.IsNullOrWhiteSpace(sourcePath))
            {
                RunFireAndForget(ShowMessageAsync("新增自動化工作失敗", "來源路徑必須填寫。"), "automation validation message");
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = jobType == AutomationJobType.MongoBackup
                    ? (!string.IsNullOrWhiteSpace(mongoDatabaseName) ? $"MongoDB {mongoDatabaseName}" : "MongoDB 備份")
                    : GetDisplayName(sourcePath);
            }

            var profile = new BackupAutomationProfile
            {
                Name = name,
                JobType = jobType,
                SourcePath = sourcePath,
                DestinationPath = destinationPath,
                Mode = mode,
                ExcludedFolderNamesText = excludedFolderNamesText,
                LogDirectoryPath = logDirectoryPath,
                MongoToolPath = mongoToolPath,
                MongoConnectionString = mongoConnectionString,
                MongoDatabaseName = mongoDatabaseName,
                MongoUseArchive = mongoUseArchive,
                MongoUseGzip = mongoUseGzip,
                MongoRetentionCount = mongoRetentionCount,
                ScheduleType = scheduleType,
                IntervalMinutes = intervalMinutes,
                ScheduleTimeText = string.IsNullOrWhiteSpace(scheduleTimeText) ? "03:00" : scheduleTimeText,
                WeeklyDaysMask = weeklyDaysMask,
                RunMissedOnStartup = runMissedOnStartup,
                IsEnabled = true,
                LastRunText = "尚未執行",
                LastResultText = "等待排程",
            };

            profile.SyncIntervalText();
            profile.SyncMongoRetentionText();
            profile.NextRunText = "計算中...";
            BackupAutomations.Add(profile);
            SaveAutomationProfilesSafe();
            ActivateAutomation(profile);
            UpdateSharedStatusBar();
            AddAutomationNotification("自動化", $"已新增備份工作：{profile.Name}", BuildBackupAutomationProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);

            AutomationNameTextBox.Text = string.Empty;
            AutomationSourcePathTextBox.Text = string.Empty;
            AutomationDestinationPathTextBox.Text = string.Empty;
            AutomationMongoToolPathTextBox.Text = @"C:\Program Files\MongoDB\Tools\100\bin\mongodump.exe";
            AutomationMongoConnectionStringTextBox.Text = string.Empty;
            AutomationMongoDatabaseNameTextBox.Text = string.Empty;
            AutomationMongoRetentionCountTextBox.Text = "7";
            AutomationMongoUseArchiveCheckBox.IsChecked = true;
            AutomationMongoUseGzipCheckBox.IsChecked = true;
            AutomationJobTypeComboBox.SelectedIndex = 0;
            AutomationModeComboBox.SelectedIndex = 0;
            AutomationIntervalTextBox.Text = "60";
            AutomationScheduleTypeComboBox.SelectedIndex = 0;
            AutomationScheduleTimeTextBox.Text = "03:00";
            AutomationWeeklyMondayCheckBox.IsChecked = true;
            AutomationWeeklyTuesdayCheckBox.IsChecked = true;
            AutomationWeeklyWednesdayCheckBox.IsChecked = true;
            AutomationWeeklyThursdayCheckBox.IsChecked = true;
            AutomationWeeklyFridayCheckBox.IsChecked = true;
            AutomationWeeklySaturdayCheckBox.IsChecked = false;
            AutomationWeeklySundayCheckBox.IsChecked = false;
            AutomationRunMissedOnStartupCheckBox.IsChecked = false;
            UpdateAutomationEntryFormForJobType(AutomationJobType.FileBackup);
            UpdateAutomationEntryScheduleForm(scheduleType: AutomationScheduleType.Interval);
        }

        internal async void RunAutomationNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryGetBackupAutomationProfile(sender, out var profile))
                {
                    return;
                }

                if (!EnsureAutomationExecutionOwner("手動執行自動化"))
                {
                    return;
                }

                await RunBackupAutomationAsync(profile, triggeredByTimer: false);
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "run automation now");
                await ShowMessageAsync("執行自動化失敗", ex.Message);
            }
        }

        internal async void EditAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureAutomationExecutionOwner("編輯自動化工作"))
                {
                    return;
                }

                if (!TryGetBackupAutomationProfile(sender, out var profile) ||
                    !await ShowBackupAutomationEditorAsync(profile))
                {
                    return;
                }

                SaveAutomationProfilesSafe();
                if (profile.IsEnabled)
                {
                    ActivateAutomation(profile);
                }
                UpdateSharedStatusBar();
                AddAutomationNotification("自動化", $"已更新工作：{profile.Name}", BuildBackupAutomationProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "edit automation job");
                await ShowMessageAsync("編輯自動化工作失敗", ex.Message);
            }
        }

        internal async void StartAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureAutomationExecutionOwner("啟用自動化工作"))
                {
                    return;
                }

                if (!TryGetBackupAutomationProfile(sender, out var profile))
                {
                    return;
                }

                profile.IsEnabled = true;
                ActivateAutomation(profile);
                profile.LastResultText = profile.JobType == AutomationJobType.MongoBackup
                    ? "排程已啟動"
                    : profile.Mode == BackupAutomationMode.Mirror
                        ? "監聽已啟動"
                        : "排程已啟動";
                SaveAutomationProfilesSafe();
                UpdateSharedStatusBar();
                AddAutomationNotification("自動化", $"已啟用工作：{profile.Name}", BuildBackupAutomationProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);

                await RunBackupAutomationAsync(profile, triggeredByTimer: false);
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "start automation job");
                await ShowMessageAsync("啟動自動化工作失敗", ex.Message);
            }
        }

        internal void DeleteAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAutomationExecutionOwner("刪除自動化工作"))
            {
                return;
            }

            if (!TryGetBackupAutomationProfile(sender, out var profile))
            {
                return;
            }

            StopAutomationTimer(profile.Id);
            StopAutomationWatcher(profile.Id);
            BackupAutomations.Remove(profile);
            SaveAutomationProfilesSafe();
            UpdateSharedStatusBar();
            AddAutomationNotification("自動化", $"已刪除工作：{profile.Name}", BuildBackupAutomationProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);
        }

        internal void StopAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAutomationExecutionOwner("停止自動化工作"))
            {
                return;
            }

            if (!TryGetBackupAutomationProfile(sender, out var profile))
            {
                return;
            }

            RequestAutomationStop(profile);
            profile.IsEnabled = false;
            StopAutomationTimer(profile.Id);
            StopAutomationWatcher(profile.Id);
            profile.NextRunText = "未啟用";
            profile.LastResultText = profile.JobType == AutomationJobType.MongoBackup
                ? "排程已停止"
                : profile.Mode == BackupAutomationMode.Mirror
                    ? "監聽已停止"
                    : "排程已停止";
            SaveAutomationProfilesSafe();
            UpdateSharedStatusBar();
            AddAutomationNotification("自動化", $"已停止工作：{profile.Name}", BuildBackupAutomationProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);
        }

        internal void AutomationEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggleSwitch || !TryGetBackupAutomationProfile(toggleSwitch, out var profile))
            {
                return;
            }

            if (toggleSwitch.IsOn == profile.IsEnabled)
            {
                return;
            }

            if (!EnsureAutomationExecutionOwner($"{(toggleSwitch.IsOn ? "啟用" : "停用")}自動化工作"))
            {
                toggleSwitch.IsOn = profile.IsEnabled;
                return;
            }

            profile.IsEnabled = toggleSwitch.IsOn;
            profile.LastResultText = profile.IsEnabled
                ? profile.JobType == AutomationJobType.MongoBackup
                    ? "等待排程"
                    : profile.Mode == BackupAutomationMode.Mirror
                        ? "監聽待命 / 等待校正排程"
                        : "等待排程"
                : "已停用";
            SaveAutomationProfilesSafe();
            if (profile.IsEnabled)
            {
                ActivateAutomation(profile);
            }
            else
            {
                StopAutomationTimer(profile.Id);
                StopAutomationWatcher(profile.Id);
                profile.NextRunText = "未啟用";
            }
            UpdateSharedStatusBar();
            AddAutomationNotification(
                "自動化",
                $"{(profile.IsEnabled ? "已啟用" : "已停用")}工作：{profile.Name}",
                BuildBackupAutomationProfileDetail(profile),
                profile.NotificationEnabled,
                profile.ToastEnabled);
        }

        internal void AutomationNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.Name = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        internal void AutomationSourcePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.SourcePath = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        internal void AutomationDestinationPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.DestinationPath = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        internal void AutomationModeComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox || !TryGetBackupAutomationProfile(comboBox, out var profile))
            {
                return;
            }

            comboBox.SelectedIndex = profile.Mode == BackupAutomationMode.Mirror ? 1 : 0;
        }

        internal void AutomationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
                ? profile.JobType == AutomationJobType.MongoBackup
                    ? "等待排程"
                    : (mode == BackupAutomationMode.Mirror ? "監聽待命 / 等待校正排程" : "等待排程")
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

        internal void AutomationJobTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAutomationEntryFormForJobType(GetAutomationJobTypeFromComboBox(AutomationJobTypeComboBox));
        }

        internal void AutomationJobTypeItemComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox || !TryGetBackupAutomationProfile(comboBox, out var profile))
            {
                return;
            }

            comboBox.SelectedIndex = profile.JobType == AutomationJobType.MongoBackup ? 1 : 0;
        }

        internal void AutomationJobTypeItemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox || !TryGetBackupAutomationProfile(comboBox, out var profile))
            {
                return;
            }

            var jobType = GetAutomationJobTypeFromComboBox(comboBox);
            if (profile.JobType == jobType)
            {
                return;
            }

            profile.JobType = jobType;
            if (jobType == AutomationJobType.MongoBackup)
            {
                profile.Mode = BackupAutomationMode.Copy;
                profile.LastResultText = profile.IsEnabled ? "等待排程" : "已停用";
            }
            else
            {
                profile.LastResultText = profile.IsEnabled
                    ? (profile.Mode == BackupAutomationMode.Mirror ? "監聽待命 / 等待校正排程" : "等待排程")
                    : "已停用";
            }

            SaveAutomationProfilesSafe();
            if (profile.IsEnabled)
            {
                ActivateAutomation(profile);
            }
            UpdateSharedStatusBar();
        }

        private static AutomationJobType GetAutomationJobTypeFromComboBox(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
                Enum.TryParse<AutomationJobType>(tag, true, out var jobType))
            {
                return jobType;
            }

            return AutomationJobType.FileBackup;
        }

        private static AutomationScheduleType GetAutomationScheduleTypeFromComboBox(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
                Enum.TryParse<AutomationScheduleType>(tag, true, out var scheduleType))
            {
                return scheduleType;
            }

            return AutomationScheduleType.Interval;
        }

        private void UpdateAutomationEntryFormForJobType(AutomationJobType jobType)
        {
            var isMongo = jobType == AutomationJobType.MongoBackup;
            AutomationFileSettingsPanel.Visibility = isMongo ? Visibility.Collapsed : Visibility.Visible;
            AutomationMongoSettingsPanel.Visibility = isMongo ? Visibility.Visible : Visibility.Collapsed;
            AutomationHintTextBlock.Text = isMongo
                ? "MongoDB 備份會呼叫 mongodump，建議使用 archive + gzip，並設定保留份數避免目的地持續膨脹。"
                : "同步鏡像會讓目的地維持和來源一致，會刪除目的地多出來的檔案與資料夾。";
        }

        internal void AutomationScheduleTypeComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox || !TryGetBackupAutomationProfile(comboBox, out var profile))
            {
                return;
            }

            comboBox.SelectedIndex = profile.ScheduleType switch
            {
                AutomationScheduleType.Daily => 1,
                AutomationScheduleType.Weekly => 2,
                _ => 0,
            };
        }

        internal void AutomationScheduleTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && TryGetBackupAutomationProfile(comboBox, out var profile))
            {
                var scheduleType = GetAutomationScheduleTypeFromComboBox(comboBox);
                if (profile.ScheduleType == scheduleType)
                {
                    return;
                }

                profile.ScheduleType = scheduleType;
                SaveAutomationProfilesSafe();
                if (profile.IsEnabled)
                {
                    ActivateAutomation(profile);
                }

                UpdateSharedStatusBar();
                return;
            }

            UpdateAutomationEntryScheduleForm(GetAutomationScheduleTypeFromComboBox(AutomationScheduleTypeComboBox));
        }

        private void UpdateAutomationEntryScheduleForm(AutomationScheduleType scheduleType)
        {
            var isInterval = scheduleType == AutomationScheduleType.Interval;
            var isWeekly = scheduleType == AutomationScheduleType.Weekly;
            AutomationIntervalSchedulePanel.Visibility = isInterval ? Visibility.Visible : Visibility.Collapsed;
            AutomationTimeSchedulePanel.Visibility = isInterval ? Visibility.Collapsed : Visibility.Visible;
            AutomationWeeklyDaysPanel.Visibility = isWeekly ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void AutomationIntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
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
                    ? profile.JobType == AutomationJobType.MongoBackup
                        ? "等待排程"
                        : (profile.Mode == BackupAutomationMode.Mirror ? "監聽待命 / 等待校正排程" : "等待排程")
                    : "已停用";
                SaveAutomationProfilesSafe();
                if (profile.IsEnabled)
                {
                    ActivateAutomation(profile);
                }
                UpdateSharedStatusBar();
            }
        }

        internal void AutomationScheduleTimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.ScheduleTimeText = textBox.Text.Trim();
            if (TryParseScheduleTimeText(profile.ScheduleTimeText, out _))
            {
                SaveAutomationProfilesSafe();
                if (profile.IsEnabled)
                {
                    ActivateAutomation(profile);
                }
            }
        }

        internal void AutomationRunMissedOnStartupCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || !TryGetBackupAutomationProfile(checkBox, out var profile))
            {
                return;
            }

            profile.RunMissedOnStartup = checkBox.IsChecked == true;
            SaveAutomationProfilesSafe();
        }

        internal void AutomationWeeklyDayCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || !TryGetBackupAutomationProfile(checkBox, out var profile))
            {
                return;
            }

            if (!TryParseDayOfWeekTag(checkBox.Tag, out var dayOfWeek))
            {
                return;
            }

            profile.SetWeekdaySelected(dayOfWeek, checkBox.IsChecked == true);
            SaveAutomationProfilesSafe();
            if (profile.IsEnabled)
            {
                ActivateAutomation(profile);
            }
        }

        internal void AutomationMongoToolPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.MongoToolPath = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        internal void AutomationMongoConnectionStringTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.MongoConnectionString = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        internal void AutomationMongoDatabaseNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.MongoDatabaseName = textBox.Text.Trim();
            SaveAutomationProfilesSafe();
        }

        internal void AutomationMongoRetentionCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetBackupAutomationProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.MongoRetentionCountText = textBox.Text.Trim();
            if (int.TryParse(profile.MongoRetentionCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retentionCount) && retentionCount > 0)
            {
                profile.MongoRetentionCount = retentionCount;
                SaveAutomationProfilesSafe();
            }
        }

        internal void AutomationMongoUseArchiveCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || !TryGetBackupAutomationProfile(checkBox, out var profile))
            {
                return;
            }

            profile.MongoUseArchive = checkBox.IsChecked == true;
            SaveAutomationProfilesSafe();
        }

        internal void AutomationMongoUseGzipCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || !TryGetBackupAutomationProfile(checkBox, out var profile))
            {
                return;
            }

            profile.MongoUseGzip = checkBox.IsChecked == true;
            SaveAutomationProfilesSafe();
        }

        internal void AddAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAutomationExecutionOwner("新增自動解壓工作"))
            {
                return;
            }

            var watchPath = AutoExtractWatchPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(watchPath))
            {
                RunFireAndForget(ShowMessageAsync("新增自動解壓失敗", "監看目錄不能為空。"), "auto extract validation message");
                return;
            }

            if (!Directory.Exists(watchPath))
            {
                RunFireAndForget(ShowMessageAsync("新增自動解壓失敗", $"監看目錄不存在：{watchPath}"), "auto extract validation message");
                return;
            }

            var profile = new AutoExtractProfile
            {
                Name = string.IsNullOrWhiteSpace(AutoExtractNameTextBox.Text)
                    ? GetDisplayName(watchPath)
                    : AutoExtractNameTextBox.Text.Trim(),
                WatchPath = watchPath,
                ExtractorPath = AutoExtractExtractorPathTextBox.Text.Trim(),
                ExtensionFilter = AutoExtractExtensionFilterTextBox.Text.Trim(),
                IsEnabled = true,
                LastRunText = "尚未執行",
                LastResultText = "監看待命",
            };

            AutoExtractProfiles.Add(profile);
            SaveAutoExtractProfilesSafe();
            ActivateAutoExtractProfile(profile);
            UpdateSharedStatusBar();
            AddAutomationNotification("自動解壓", $"已新增工作：{profile.Name}", BuildAutoExtractProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);

            AutoExtractNameTextBox.Text = string.Empty;
            AutoExtractWatchPathTextBox.Text = string.Empty;
            AutoExtractExtractorPathTextBox.Text = string.Empty;
            AutoExtractExtensionFilterTextBox.Text = ".zip, .rar, .7z";
        }

        internal async void RunAutoExtractNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryGetAutoExtractProfile(sender, out var profile))
                {
                    return;
                }

                if (!EnsureAutomationExecutionOwner("手動執行自動解壓"))
                {
                    return;
                }

                await RunAutoExtractProfileAsync(profile, triggeredByWatcher: false);
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "run auto extract now");
                await ShowMessageAsync("執行自動解壓失敗", ex.Message);
            }
        }

        internal async void EditAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureAutomationExecutionOwner("編輯自動解壓工作"))
                {
                    return;
                }

                if (!TryGetAutoExtractProfile(sender, out var profile) ||
                    !await ShowAutoExtractProfileEditorAsync(profile))
                {
                    return;
                }

                SaveAutoExtractProfilesSafe();
                if (profile.IsEnabled)
                {
                    ActivateAutoExtractProfile(profile);
                }
                UpdateSharedStatusBar();
                AddAutomationNotification("自動解壓", $"已更新工作：{profile.Name}", BuildAutoExtractProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "edit auto extract profile");
                await ShowMessageAsync("編輯自動解壓失敗", ex.Message);
            }
        }

        internal async void StartAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureAutomationExecutionOwner("啟用自動解壓工作"))
                {
                    return;
                }

                if (!TryGetAutoExtractProfile(sender, out var profile))
                {
                    return;
                }

                profile.IsEnabled = true;
                ActivateAutoExtractProfile(profile);
                profile.LastResultText = "監看已啟動";
                SaveAutoExtractProfilesSafe();
                UpdateSharedStatusBar();
                AddAutomationNotification("自動解壓", $"已啟用工作：{profile.Name}", BuildAutoExtractProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);

                await RunAutoExtractProfileAsync(profile, triggeredByWatcher: false);
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "start auto extract profile");
                await ShowMessageAsync("啟動自動解壓失敗", ex.Message);
            }
        }

        internal void StopAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAutomationExecutionOwner("停止自動解壓工作"))
            {
                return;
            }

            if (!TryGetAutoExtractProfile(sender, out var profile))
            {
                return;
            }

            RequestAutoExtractStop(profile);
            profile.IsEnabled = false;
            StopAutoExtractWatcher(profile.Id);
            profile.LastResultText = "監看已停止";
            SaveAutoExtractProfilesSafe();
            UpdateSharedStatusBar();
            AddAutomationNotification("自動解壓", $"已停止工作：{profile.Name}", BuildAutoExtractProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);
        }

        internal void DeleteAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAutomationExecutionOwner("刪除自動解壓工作"))
            {
                return;
            }

            if (!TryGetAutoExtractProfile(sender, out var profile))
            {
                return;
            }

            RequestAutoExtractStop(profile);
            StopAutoExtractWatcher(profile.Id);
            AutoExtractProfiles.Remove(profile);
            SaveAutoExtractProfilesSafe();
            UpdateSharedStatusBar();
            AddAutomationNotification("自動解壓", $"已刪除工作：{profile.Name}", BuildAutoExtractProfileDetail(profile), profile.NotificationEnabled, profile.ToastEnabled);
        }

        internal void AutoExtractEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggleSwitch || !TryGetAutoExtractProfile(toggleSwitch, out var profile))
            {
                return;
            }

            if (toggleSwitch.IsOn == profile.IsEnabled)
            {
                return;
            }

            if (!EnsureAutomationExecutionOwner($"{(toggleSwitch.IsOn ? "啟用" : "停用")}自動解壓工作"))
            {
                toggleSwitch.IsOn = profile.IsEnabled;
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

            UpdateSharedStatusBar();
            AddAutomationNotification(
                "自動解壓",
                $"{(profile.IsEnabled ? "已啟用" : "已停用")}工作：{profile.Name}",
                BuildAutoExtractProfileDetail(profile),
                profile.NotificationEnabled,
                profile.ToastEnabled);
        }

        internal void AutoExtractNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.Name = textBox.Text.Trim();
            SaveAutoExtractProfilesSafe();
        }

        internal void AutoExtractWatchPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
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

        internal void AutoExtractExtractorPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.ExtractorPath = textBox.Text.Trim();
            SaveAutoExtractProfilesSafe();
        }

        internal void AutoExtractExtensionFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetAutoExtractProfile(sender, out var profile) || sender is not TextBox textBox)
            {
                return;
            }

            profile.ExtensionFilter = textBox.Text;
            SaveAutoExtractProfilesSafe();
        }

        internal void AddAutoExtractPassword_Click(object sender, RoutedEventArgs e)
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

        internal void RemoveAutoExtractPassword_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: AutoExtractPasswordItem item } || item.ParentProfile is null)
            {
                return;
            }

            item.ParentProfile.Passwords.Remove(item);
            SaveAutoExtractProfilesSafe();
        }

        internal void AutoExtractPasswordItemTextBox_TextChanged(object sender, TextChangedEventArgs e)
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
                var localSettings = LoadLocalSettingsConfig();
                if (localSettings?.BackupAutomations is { Count: > 0 })
                {
                    foreach (var config in localSettings.BackupAutomations)
                    {
                        var profile = new BackupAutomationProfile
                        {
                            Id = config.Id == Guid.Empty ? Guid.NewGuid() : config.Id,
                            Name = config.Name,
                            JobType = config.JobType,
                            SourcePath = config.SourcePath,
                            DestinationPath = config.DestinationPath,
                            Mode = config.Mode,
                            ExcludedFolderNamesText = config.ExcludedFolderNamesText ?? string.Empty,
                            LogDirectoryPath = ResolveBackupAutomationLogDirectoryPath(config.LogDirectoryPath),
                            MongoToolPath = config.MongoToolPath,
                            MongoConnectionString = config.MongoConnectionString,
                            MongoDatabaseName = config.MongoDatabaseName,
                            MongoUseGzip = config.MongoUseGzip,
                            MongoUseArchive = config.MongoUseArchive,
                            MongoRetentionCount = Math.Max(1, config.MongoRetentionCount),
                            ScheduleType = config.ScheduleType,
                            IntervalMinutes = Math.Max(1, config.IntervalMinutes),
                            ScheduleTimeText = string.IsNullOrWhiteSpace(config.ScheduleTimeText) ? "03:00" : config.ScheduleTimeText,
                            WeeklyDaysMask = config.WeeklyDaysMask == 0 ? 62 : config.WeeklyDaysMask,
                            RunMissedOnStartup = config.RunMissedOnStartup,
                            IsEnabled = config.IsEnabled,
                            NotificationEnabled = config.NotificationEnabled,
                            ToastEnabled = config.ToastEnabled,
                            LastRunText = string.IsNullOrWhiteSpace(config.LastRunText) ? "尚未執行" : config.LastRunText,
                            LastResultText = string.IsNullOrWhiteSpace(config.LastResultText) ? (config.IsEnabled ? "等待排程" : "已停用") : config.LastResultText,
                        };
                        profile.SyncIntervalText();
                        profile.SyncMongoRetentionText();
                        profile.NextRunText = "尚未排程";
                        BackupAutomations.Add(profile);
                    }

                    return;
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
                var localSettings = LoadLocalSettingsConfig();
                if (localSettings?.AutoExtractProfiles is { Count: > 0 })
                {
                    foreach (var config in localSettings.AutoExtractProfiles)
                    {
                        var profile = new AutoExtractProfile
                        {
                            Id = config.Id == Guid.Empty ? Guid.NewGuid() : config.Id,
                            Name = config.Name,
                            WatchPath = config.WatchPath,
                            ExtractorPath = config.ExtractorPath,
                            ExtensionFilter = string.IsNullOrWhiteSpace(config.ExtensionFilter)
                                ? ".zip, .rar, .7z"
                                : config.ExtensionFilter,
                            IsEnabled = config.IsEnabled,
                            NotificationEnabled = config.NotificationEnabled,
                            ToastEnabled = config.ToastEnabled,
                            LastRunText = string.IsNullOrWhiteSpace(config.LastRunText) ? "尚未執行" : config.LastRunText,
                            LastResultText = string.IsNullOrWhiteSpace(config.LastResultText)
                                ? (config.IsEnabled ? "等待監看" : "已停用")
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

                    return;
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

            if (!IsAutomationExecutionOwner)
            {
                return;
            }

            foreach (var profile in BackupAutomations)
            {
                ActivateAutomation(profile, triggeredByStartup: true);
            }
        }

        private void RescheduleAutoExtractProfiles()
        {
            StopAllAutoExtractWatchers();

            if (!IsAutomationExecutionOwner)
            {
                return;
            }

            foreach (var profile in AutoExtractProfiles)
            {
                ActivateAutoExtractProfile(profile);
            }
        }

        private void ActivateAutomation(BackupAutomationProfile profile, bool triggeredByStartup = false)
        {
            if (!IsAutomationExecutionOwner)
            {
                StopAutomationTimer(profile.Id);
                StopAutomationWatcher(profile.Id);
                return;
            }

            if (triggeredByStartup && profile.IsEnabled && profile.RunMissedOnStartup && ShouldRunMissedAutomationOnStartup(profile, DateTime.Now))
            {
                profile.NextRunText = "啟動後補跑中...";
                RunFireAndForget(
                    RunBackupAutomationAsync(profile, triggeredByTimer: true),
                    "backup automation missed startup run");
            }

            ScheduleBackupAutomation(profile);
            if (profile.JobType == AutomationJobType.FileBackup && profile.Mode == BackupAutomationMode.Mirror)
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

            if (!profile.IsEnabled)
            {
                profile.LastResultText = "已停用";
                profile.NextRunText = "未啟用";
                return;
            }

            if (!TryCalculateNextRun(profile, DateTime.Now, out var nextRunAt, out var invalidReason))
            {
                profile.LastResultText = invalidReason;
                profile.NextRunText = "無法排程";
                return;
            }

            var delay = nextRunAt - DateTime.Now;
            if (delay < TimeSpan.FromSeconds(1))
            {
                delay = TimeSpan.FromSeconds(1);
            }

            var timer = new System.Timers.Timer(delay.TotalMilliseconds)
            {
                AutoReset = false,
                Enabled = true,
            };

            timer.Elapsed += async (_, _) =>
            {
                try
                {
                    StopAutomationTimer(profile.Id);
                    await RunBackupAutomationAsync(profile, triggeredByTimer: true);
                }
                catch (Exception ex)
                {
                    LogBoundaryException(ex, "backup automation timer elapsed");
                }
            };
            _automationTimers[profile.Id] = timer;
            profile.NextRunText = nextRunAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            profile.LastResultText = profile.JobType == AutomationJobType.MongoBackup
                ? "等待排程"
                : profile.Mode == BackupAutomationMode.Mirror
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
                () => RunFireAndForget(
                    RunBackupAutomationAsync(profile, triggeredByTimer: true),
                    "backup automation watcher trigger"),
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
            if (!IsAutomationExecutionOwner)
            {
                StopAutoExtractWatcher(profile.Id);
                return;
            }

            StopAutoExtractWatcher(profile.Id);

            if (!profile.IsEnabled)
            {
                profile.LastResultText = "已停用";
                return;
            }

            var watcher = new AutoExtractProfileWatcher(
                profile,
                DispatcherQueue,
                () => RunFireAndForget(
                    RunAutoExtractProfileAsync(profile, triggeredByWatcher: true),
                    "auto extract watcher trigger"),
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
                UpdateSharedStatusBar();
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
            var backgroundWorkId = BeginBackgroundWork(GetAutomationBackgroundWorkTitle(profile), isAutomation: true);
            string? completionRecord = null;
            string? notificationSummary = null;
            string? notificationDetails = null;

            await EnqueueOnUiAsync(() =>
            {
                profile.IsRunning = true;
                profile.LastResultText = triggeredByTimer ? "背景執行中..." : "手動執行中...";
                UpdateSharedStatusBar();
            });

            try
            {
                var result = await Task.Run(() => ExecuteBackupAutomation(profile, cancellationTokenSource.Token), cancellationTokenSource.Token);

                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = result.StatusText;
                    UpdateSharedStatusBar();
                });
                completionRecord = result.CompletionRecord;
                notificationSummary = BuildBackupAutomationNotificationSummary(profile, result.StatusText);
                notificationDetails = result.CompletionRecord;
            }
            catch (OperationCanceledException)
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = "已停止";
                    UpdateSharedStatusBar();
                });
                completionRecord = $"停止：{profile.Name}";
                notificationSummary = $"已停止：{profile.Name}";
                notificationDetails = BuildBackupAutomationProfileDetail(profile);
            }
            catch (Exception ex)
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = $"失敗: {ex.Message}";
                    UpdateSharedStatusBar();
                });
                completionRecord = $"失敗：{profile.Name} · {ex.Message}";
                notificationSummary = $"執行失敗：{profile.Name}";
                notificationDetails = completionRecord;
            }
            finally
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.IsRunning = false;
                    SaveAutomationProfilesSafe();
                    if (profile.IsEnabled)
                    {
                        ActivateAutomation(profile);
                    }
                    else
                    {
                        profile.NextRunText = "未啟用";
                    }
                    UpdateSharedStatusBar();
                });

                if (_automationCancellationTokens.Remove(profile.Id, out var activeCancellationTokenSource))
                {
                    activeCancellationTokenSource.Dispose();
                }

                CompleteBackgroundWork(backgroundWorkId, completionRecord, persistToLocalHistory: false);

                lock (_runningAutomationIds)
                {
                    _runningAutomationIds.Remove(profile.Id);
                }

                if (!string.IsNullOrWhiteSpace(notificationSummary))
                {
                    AddAutomationNotification("自動化", notificationSummary, notificationDetails ?? notificationSummary, profile.NotificationEnabled, profile.ToastEnabled);
                }
            }
        }

        private static BackupAutomationExecutionResult ExecuteBackupAutomation(BackupAutomationProfile profile, CancellationToken cancellationToken)
        {
            using var logScope = CreateBackupAutomationLogScope(profile);

            try
            {
                logScope.WriteLine($"工作：{profile.Name}");
                logScope.WriteLine($"類型：{profile.JobType}");
                logScope.WriteLine($"開始時間：{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");

                if (profile.JobType == AutomationJobType.MongoBackup)
                {
                    ExecuteMongoBackup(profile, logScope, cancellationToken);
                    var mongoStatusText = "MongoDB 備份完成";
                    var mongoCompletionRecord = BuildBackupAutomationCompletionRecord(profile.Name, mongoStatusText, logScope.LogFilePath);
                    logScope.WriteLine(mongoStatusText);
                    logScope.WriteLine(mongoCompletionRecord);
                    return new BackupAutomationExecutionResult(mongoStatusText, mongoCompletionRecord);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(profile.SourcePath) || string.IsNullOrWhiteSpace(profile.DestinationPath))
                {
                    throw new InvalidOperationException("來源或目的地未設定。");
                }

                Directory.CreateDirectory(profile.DestinationPath);

                var excludedFolderNames = ParseExcludedFolderNames(profile.ExcludedFolderNamesText);
                if (excludedFolderNames.Count > 0)
                {
                    logScope.WriteLine($"排除資料夾：{string.Join(", ", excludedFolderNames.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))}");
                }

                var summary = new BackupAutomationSummary();

                if (File.Exists(profile.SourcePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destinationFile = Path.Combine(profile.DestinationPath, Path.GetFileName(profile.SourcePath));
                    if (ShouldCopyFile(profile.SourcePath, destinationFile))
                    {
                        File.Copy(profile.SourcePath, destinationFile, overwrite: true);
                        summary.CopiedFiles++;
                        logScope.WriteLine($"複製檔案：{destinationFile}");
                    }
                }
                else
                {
                    if (!Directory.Exists(profile.SourcePath))
                    {
                        throw new DirectoryNotFoundException("來源不存在。");
                    }

                    if (profile.Mode == BackupAutomationMode.Mirror)
                    {
                        SyncDirectoryMirror(profile.SourcePath, profile.DestinationPath, excludedFolderNames, summary, logScope, cancellationToken);
                    }
                    else
                    {
                        CopyDirectory(profile.SourcePath, profile.DestinationPath, excludedFolderNames, summary, logScope, cancellationToken);
                    }
                }

                var statusText = BuildBackupAutomationStatusText(profile, summary);
                var completionRecord = BuildBackupAutomationCompletionRecord(profile.Name, statusText, logScope.LogFilePath);
                logScope.WriteLine(statusText);
                logScope.WriteLine(completionRecord);
                return new BackupAutomationExecutionResult(statusText, completionRecord);
            }
            catch (OperationCanceledException)
            {
                logScope.WriteLine("工作已停止。");
                throw;
            }
            catch (Exception ex)
            {
                logScope.WriteLine($"失敗：{ex.Message}");
                if (!string.IsNullOrWhiteSpace(logScope.LogFilePath))
                {
                    throw new InvalidOperationException($"{ex.Message}（log：{logScope.LogFilePath}）", ex);
                }

                throw;
            }
        }

        private static void ExecuteMongoBackup(
            BackupAutomationProfile profile,
            BackupAutomationLogScope logScope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(profile.DestinationPath))
            {
                throw new InvalidOperationException("MongoDB 備份目的地未設定。");
            }

            if (string.IsNullOrWhiteSpace(profile.MongoConnectionString))
            {
                throw new InvalidOperationException("Mongo URI 未設定。");
            }

            Directory.CreateDirectory(profile.DestinationPath);

            var executable = ResolveMongoDumpExecutable(profile.MongoToolPath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var databaseNames = ParseMongoDatabaseNames(profile.MongoDatabaseName);
            logScope.WriteLine($"mongodump：{executable}");
            logScope.WriteLine($"目的地：{profile.DestinationPath}");
            logScope.WriteLine($"URI：{SanitizeMongoConnectionStringForLog(profile.MongoConnectionString)}");
            logScope.WriteLine(
                $"模式：{(profile.MongoUseArchive ? "archive" : "folder")} / gzip={(profile.MongoUseGzip ? "on" : "off")} / 保留份數={Math.Max(1, profile.MongoRetentionCount).ToString(CultureInfo.InvariantCulture)}");
            logScope.WriteLine(
                databaseNames.Count == 0
                    ? "資料庫：全部"
                    : $"資料庫：{string.Join(", ", databaseNames)}");

            if (databaseNames.Count == 0)
            {
                ExecuteMongoBackupTarget(profile, logScope, executable, timestamp, databaseName: null, cancellationToken);
                CleanupOldMongoBackups(profile.DestinationPath, BuildMongoBackupBaseName(databaseName: null), Math.Max(1, profile.MongoRetentionCount), logScope);
                return;
            }

            foreach (var databaseName in databaseNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExecuteMongoBackupTarget(profile, logScope, executable, timestamp, databaseName, cancellationToken);
                CleanupOldMongoBackups(profile.DestinationPath, BuildMongoBackupBaseName(databaseName), Math.Max(1, profile.MongoRetentionCount), logScope);
            }
        }

        private static void ExecuteMongoBackupTarget(
            BackupAutomationProfile profile,
            BackupAutomationLogScope logScope,
            string executable,
            string timestamp,
            string? databaseName,
            CancellationToken cancellationToken)
        {
            var backupBaseName = BuildMongoBackupBaseName(databaseName);
            var backupStem = $"{backupBaseName}_{timestamp}";
            string createdPath;
            var argumentParts = new List<string>
            {
                $"--uri=\"{profile.MongoConnectionString.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            };

            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                argumentParts.Add($"--db=\"{databaseName.Replace("\"", "\\\"", StringComparison.Ordinal)}\"");
            }

            if (profile.MongoUseArchive)
            {
                var extension = profile.MongoUseGzip ? ".archive.gz" : ".archive";
                createdPath = Path.Combine(profile.DestinationPath, backupStem + extension);
                argumentParts.Add($"--archive=\"{createdPath}\"");
            }
            else
            {
                createdPath = Path.Combine(profile.DestinationPath, backupStem);
                argumentParts.Add($"--out=\"{createdPath}\"");
            }

            if (profile.MongoUseGzip)
            {
                argumentParts.Add("--gzip");
            }

            var arguments = string.Join(" ", argumentParts);
            logScope.WriteLine($"開始備份：{(string.IsNullOrWhiteSpace(databaseName) ? "全部資料庫" : databaseName)}");
            logScope.WriteLine($"輸出：{createdPath}");
            ExecuteProcessOrThrow(executable, arguments, cancellationToken, logScope);
            logScope.WriteLine($"完成輸出：{createdPath}");
        }

        private static string BuildMongoBackupBaseName(string? databaseName)
        {
            var rawName = string.IsNullOrWhiteSpace(databaseName)
                ? "all-databases"
                : databaseName;
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(rawName.Length);
            foreach (var character in rawName)
            {
                builder.Append(invalidChars.Contains(character) ? '_' : character);
            }

            var sanitized = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "mongo-backup" : sanitized;
        }

        private static List<string> ParseMongoDatabaseNames(string rawValue)
        {
            return (rawValue ?? string.Empty)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string ResolveMongoDumpExecutable(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var candidate = configuredPath.Trim().Trim('"');
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                throw new FileNotFoundException("設定的 mongodump 不存在。", candidate);
            }

            var candidates = new[]
            {
                @"C:\Program Files\MongoDB\Tools\100\bin\mongodump.exe",
                @"C:\Program Files\MongoDB\Tools\bin\mongodump.exe",
                @"C:\Program Files (x86)\MongoDB\Tools\100\bin\mongodump.exe",
                @"C:\Program Files (x86)\MongoDB\Tools\bin\mongodump.exe",
            };

            var executable = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(executable))
            {
                return executable;
            }

            return "mongodump";
        }

        private static void ExecuteProcessOrThrow(
            string fileName,
            string arguments,
            CancellationToken cancellationToken,
            BackupAutomationLogScope? logScope = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logScope?.WriteLine($"執行：{fileName} {SanitizeMongoDumpArgumentsForLog(arguments)}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
            };

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
            });

            process.Start();
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                logScope?.WriteLine($"stdout：{TrimLogOutput(standardOutput)}");
            }

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                logScope?.WriteLine($"stderr：{TrimLogOutput(standardError)}");
            }

            logScope?.WriteLine($"結束代碼：{process.ExitCode.ToString(CultureInfo.InvariantCulture)}");

            if (process.ExitCode == 0)
            {
                return;
            }

            var errorMessage = string.IsNullOrWhiteSpace(standardError)
                ? standardOutput.Trim()
                : standardError.Trim();
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = $"結束代碼 {process.ExitCode}";
            }

            throw new InvalidOperationException(errorMessage);
        }

        private static void CleanupOldMongoBackups(
            string destinationPath,
            string backupBaseName,
            int retentionCount,
            BackupAutomationLogScope? logScope = null)
        {
            if (!Directory.Exists(destinationPath))
            {
                return;
            }

            var prefix = backupBaseName + "_";
            var entries = Directory.EnumerateFileSystemEntries(destinationPath)
                .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(path => new
                {
                    Path = path,
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
                    IsDirectory = Directory.Exists(path),
                })
                .OrderByDescending(entry => entry.LastWriteTimeUtc)
                .ToList();

            foreach (var entry in entries.Skip(Math.Max(1, retentionCount)))
            {
                try
                {
                    if (entry.IsDirectory)
                    {
                        Directory.Delete(entry.Path, recursive: true);
                        logScope?.WriteLine($"清理舊備份資料夾：{entry.Path}");
                    }
                    else if (File.Exists(entry.Path))
                    {
                        File.Delete(entry.Path);
                        logScope?.WriteLine($"清理舊備份檔案：{entry.Path}");
                    }
                }
                catch
                {
                }
            }
        }

        private static string SanitizeMongoConnectionStringForLog(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return string.Empty;
            }

            var trimmed = connectionString.Trim();
            var schemeIndex = trimmed.IndexOf("://", StringComparison.Ordinal);
            if (schemeIndex < 0)
            {
                return "***";
            }

            var authorityStart = schemeIndex + 3;
            var authorityEnd = trimmed.IndexOf('/', authorityStart);
            if (authorityEnd < 0)
            {
                authorityEnd = trimmed.Length;
            }

            var authority = trimmed.Substring(authorityStart, authorityEnd - authorityStart);
            var atIndex = authority.LastIndexOf('@');
            if (atIndex < 0)
            {
                return trimmed;
            }

            var sanitizedAuthority = $"***@{authority[(atIndex + 1)..]}";
            return trimmed[..authorityStart] + sanitizedAuthority + trimmed[authorityEnd..];
        }

        private static string SanitizeMongoDumpArgumentsForLog(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return string.Empty;
            }

            var marker = "--uri=\"";
            var markerIndex = arguments.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return arguments;
            }

            var valueStart = markerIndex + marker.Length;
            var valueEnd = arguments.IndexOf('"', valueStart);
            if (valueEnd < 0)
            {
                return arguments;
            }

            var sanitizedUri = SanitizeMongoConnectionStringForLog(arguments[valueStart..valueEnd]);
            return arguments[..valueStart] + sanitizedUri + arguments[valueEnd..];
        }

        private static string TrimLogOutput(string text)
        {
            var normalized = NormalizeBackgroundWorkRecordForTextBox(text).Trim();
            if (normalized.Length <= 800)
            {
                return normalized;
            }

            return normalized[..800] + "...";
        }

        private static string GetAutomationBackgroundWorkTitle(BackupAutomationProfile profile)
        {
            if (profile.JobType == AutomationJobType.MongoBackup)
            {
                return $"{profile.Name} MongoDB 備份中";
            }

            return $"{profile.Name} {(profile.Mode == BackupAutomationMode.Mirror ? "同步中" : "備份中")}";
        }

        private void AddAutomationNotification(string category, string summary, string details, bool notificationEnabled = true, bool toastEnabled = true)
        {
            if (!notificationEnabled && !toastEnabled)
            {
                return;
            }

            AddNotificationHistoryRecord(
                NotificationHistoryScope.LocalOnly,
                category,
                summary,
                details,
                showWindowsToast: toastEnabled,
                persistRecord: notificationEnabled);
        }

        private static string BuildBackupAutomationProfileDetail(BackupAutomationProfile profile)
        {
            var builder = new StringBuilder();
            builder.Append("名稱：");
            builder.AppendLine(profile.Name);
            builder.Append("類型：");
            builder.AppendLine(profile.JobTypeText);
            builder.Append("排程：");
            builder.AppendLine(profile.ScheduleDescription);
            builder.Append("狀態：");
            builder.AppendLine(profile.LastResultText);

            if (profile.JobType == AutomationJobType.FileBackup)
            {
                builder.Append("來源：");
                builder.AppendLine(profile.SourcePath);
                builder.Append("目的地：");
                builder.AppendLine(profile.DestinationPath);
            }

            builder.Append("Log：");
            builder.AppendLine(ResolveBackupAutomationLogDirectoryPath(profile.LogDirectoryPath));

            return builder.ToString().TrimEnd();
        }

        private static string BuildBackupAutomationNotificationSummary(BackupAutomationProfile profile, string statusText)
        {
            var action = profile.JobType == AutomationJobType.MongoBackup
                ? "MongoDB 備份"
                : profile.Mode == BackupAutomationMode.Mirror
                    ? "同步"
                    : "備份";
            return $"{action}完成：{profile.Name} · {statusText}";
        }

        private sealed class BackupAutomationExecutionResult
        {
            public BackupAutomationExecutionResult(string statusText, string completionRecord)
            {
                StatusText = statusText;
                CompletionRecord = completionRecord;
            }

            public string StatusText { get; }

            public string CompletionRecord { get; }
        }

        private sealed class BackupAutomationSummary
        {
            public int CopiedFiles { get; set; }

            public int DeletedFiles { get; set; }

            public int DeletedDirectories { get; set; }

            public int SkippedDirectories { get; set; }
        }

        private sealed class BackupAutomationLogScope : IDisposable
        {
            private static readonly object FileWriteLock = new();
            private readonly Guid _profileId;
            private readonly string _profileName;
            private readonly AutomationJobType _jobType;

            public BackupAutomationLogScope(BackupAutomationProfile profile)
            {
                _profileId = profile.Id;
                _profileName = profile.Name;
                _jobType = profile.JobType;
                var logDirectoryPath = ResolveBackupAutomationLogDirectoryPath(profile.LogDirectoryPath);
                Directory.CreateDirectory(logDirectoryPath);
                LogFilePath = Path.Combine(
                    logDirectoryPath,
                    $"{BuildSafeLogFileName(profile.Name, profile.Id)}-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
            }

            public string? LogFilePath { get; }

            public void WriteLine(string message)
            {
                var line =
                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} " +
                    $"[{_jobType}] [{_profileName}] {message}";

                if (!string.IsNullOrWhiteSpace(LogFilePath))
                {
                    lock (FileWriteLock)
                    {
                        File.AppendAllText(LogFilePath, line + Environment.NewLine);
                    }
                }

                AppLogging.Information(
                    "BackupAutomation ProfileId={ProfileId} Profile={ProfileName} JobType={JobType} Message={AutomationMessage}",
                    _profileId,
                    _profileName,
                    _jobType,
                    message);
            }

            public void Dispose()
            {
            }
        }

        private static BackupAutomationLogScope CreateBackupAutomationLogScope(BackupAutomationProfile profile)
        {
            return new BackupAutomationLogScope(profile);
        }

        private static string ResolveBackupAutomationLogDirectoryPath(string? path)
        {
            return NormalizeLogDirectoryPath(string.IsNullOrWhiteSpace(path) ? CurrentLogDirectoryPath : path);
        }

        private static string BuildSafeLogFileName(string? profileName, Guid profileId)
        {
            var rawName = string.IsNullOrWhiteSpace(profileName)
                ? $"automation-{profileId.ToString("N", CultureInfo.InvariantCulture)}"
                : profileName.Trim();
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var sanitized = new string(rawName.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(sanitized)
                ? $"automation-{profileId.ToString("N", CultureInfo.InvariantCulture)}"
                : sanitized;
        }

        private static string BuildBackupAutomationStatusText(BackupAutomationProfile profile, BackupAutomationSummary summary)
        {
            var verb = profile.Mode == BackupAutomationMode.Mirror ? "同步完成" : "備份完成";
            var parts = new List<string>
            {
                $"複製檔案 {summary.CopiedFiles.ToString(CultureInfo.InvariantCulture)}",
            };

            if (summary.DeletedFiles > 0)
            {
                parts.Add($"刪除檔案 {summary.DeletedFiles.ToString(CultureInfo.InvariantCulture)}");
            }

            if (summary.DeletedDirectories > 0)
            {
                parts.Add($"刪除資料夾 {summary.DeletedDirectories.ToString(CultureInfo.InvariantCulture)}");
            }

            if (summary.SkippedDirectories > 0)
            {
                parts.Add($"排除資料夾 {summary.SkippedDirectories.ToString(CultureInfo.InvariantCulture)}");
            }

            return $"{verb}：{string.Join("，", parts)}";
        }

        private static string BuildBackupAutomationCompletionRecord(string profileName, string statusText, string? logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                return $"完成：{profileName} · {statusText}";
            }

            return $"完成：{profileName} · {statusText}{Environment.NewLine}log：{logFilePath}";
        }

        private static bool ShouldCopyFile(string sourceFile, string destinationFile)
        {
            if (!File.Exists(destinationFile))
            {
                return true;
            }

            var sourceInfo = new FileInfo(sourceFile);
            var destinationInfo = new FileInfo(destinationFile);
            if (sourceInfo.Length != destinationInfo.Length)
            {
                return true;
            }

            if (sourceInfo.LastWriteTimeUtc == destinationInfo.LastWriteTimeUtc)
            {
                return false;
            }

            return !AreFilesContentEqual(sourceFile, destinationFile);
        }

        private static bool AreFilesContentEqual(string sourceFile, string destinationFile)
        {
            const int bufferSize = 81920;
            using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var destinationStream = new FileStream(destinationFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (sourceStream.Length != destinationStream.Length)
            {
                return false;
            }

            var sourceBuffer = new byte[bufferSize];
            var destinationBuffer = new byte[bufferSize];
            while (true)
            {
                var sourceBytesRead = sourceStream.Read(sourceBuffer, 0, sourceBuffer.Length);
                var destinationBytesRead = destinationStream.Read(destinationBuffer, 0, destinationBuffer.Length);
                if (sourceBytesRead != destinationBytesRead)
                {
                    return false;
                }

                if (sourceBytesRead == 0)
                {
                    return true;
                }

                for (var index = 0; index < sourceBytesRead; index++)
                {
                    if (sourceBuffer[index] != destinationBuffer[index])
                    {
                        return false;
                    }
                }
            }
        }

        private static HashSet<string> ParseExcludedFolderNames(string rawValue)
        {
            return rawValue
                .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool ShouldExcludeDirectory(string path, HashSet<string> excludedFolderNames)
        {
            if (excludedFolderNames.Count == 0)
            {
                return false;
            }

            var directoryName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return !string.IsNullOrWhiteSpace(directoryName) && excludedFolderNames.Contains(directoryName);
        }

        private static string SanitizeFileName(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(invalidCharacters.Contains(character) ? '_' : character);
            }

            var sanitized = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "backup" : sanitized;
        }

        private static bool TryCalculateNextRun(
            BackupAutomationProfile profile,
            DateTime referenceTime,
            out DateTime nextRunAt,
            out string invalidReason)
        {
            invalidReason = "排程設定無效";
            nextRunAt = referenceTime;

            switch (profile.ScheduleType)
            {
                case AutomationScheduleType.Interval:
                    if (profile.IntervalMinutes <= 0)
                    {
                        invalidReason = "間隔設定無效";
                        return false;
                    }

                    nextRunAt = referenceTime.AddMinutes(profile.IntervalMinutes);
                    return true;

                case AutomationScheduleType.Daily:
                    if (!TryParseScheduleTimeText(profile.ScheduleTimeText, out var dailyTime))
                    {
                        invalidReason = "每日時間格式無效";
                        return false;
                    }

                    nextRunAt = referenceTime.Date.Add(dailyTime);
                    if (nextRunAt <= referenceTime)
                    {
                        nextRunAt = nextRunAt.AddDays(1);
                    }

                    return true;

                case AutomationScheduleType.Weekly:
                    if (!TryParseScheduleTimeText(profile.ScheduleTimeText, out var weeklyTime))
                    {
                        invalidReason = "每週時間格式無效";
                        return false;
                    }

                    var selectedDays = GetSelectedWeekDays(profile.WeeklyDaysMask);
                    if (selectedDays.Count == 0)
                    {
                        invalidReason = "每週至少要選一天";
                        return false;
                    }

                    for (var offset = 0; offset < 8; offset++)
                    {
                        var candidateDate = referenceTime.Date.AddDays(offset);
                        if (!selectedDays.Contains(candidateDate.DayOfWeek))
                        {
                            continue;
                        }

                        var candidate = candidateDate.Add(weeklyTime);
                        if (candidate > referenceTime)
                        {
                            nextRunAt = candidate;
                            return true;
                        }
                    }

                    invalidReason = "無法計算下次執行";
                    return false;

                default:
                    invalidReason = "未知的排程類型";
                    return false;
            }
        }

        private static bool ShouldRunMissedAutomationOnStartup(BackupAutomationProfile profile, DateTime now)
        {
            if (!TryGetLastRunDateTime(profile.LastRunText, out var lastRunAt))
            {
                return true;
            }

            return profile.ScheduleType switch
            {
                AutomationScheduleType.Interval => profile.IntervalMinutes > 0 && lastRunAt.AddMinutes(profile.IntervalMinutes) <= now,
                AutomationScheduleType.Daily => TryParseScheduleTimeText(profile.ScheduleTimeText, out var dailyTime) &&
                    lastRunAt < GetMostRecentDailyOccurrence(now, dailyTime),
                AutomationScheduleType.Weekly => TryParseScheduleTimeText(profile.ScheduleTimeText, out var weeklyTime) &&
                    lastRunAt < GetMostRecentWeeklyOccurrence(now, weeklyTime, profile.WeeklyDaysMask),
                _ => false,
            };
        }

        private static bool TryParseScheduleTimeText(string rawValue, out TimeSpan timeOfDay)
        {
            return TimeSpan.TryParseExact(
                rawValue?.Trim(),
                new[] { "h\\:mm", "hh\\:mm" },
                CultureInfo.InvariantCulture,
                out timeOfDay);
        }

        private static bool TryGetLastRunDateTime(string rawValue, out DateTime lastRunAt)
        {
            return DateTime.TryParseExact(
                rawValue,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out lastRunAt);
        }

        private static DateTime GetMostRecentDailyOccurrence(DateTime now, TimeSpan timeOfDay)
        {
            var candidate = now.Date.Add(timeOfDay);
            return candidate <= now ? candidate : candidate.AddDays(-1);
        }

        private static DateTime GetMostRecentWeeklyOccurrence(DateTime now, TimeSpan timeOfDay, int weeklyDaysMask)
        {
            var selectedDays = GetSelectedWeekDays(weeklyDaysMask);
            for (var offset = 0; offset < 8; offset++)
            {
                var candidateDate = now.Date.AddDays(-offset);
                if (!selectedDays.Contains(candidateDate.DayOfWeek))
                {
                    continue;
                }

                var candidate = candidateDate.Add(timeOfDay);
                if (candidate <= now)
                {
                    return candidate;
                }
            }

            return DateTime.MinValue;
        }

        private static HashSet<DayOfWeek> GetSelectedWeekDays(int weeklyDaysMask)
        {
            var selectedDays = new HashSet<DayOfWeek>();
            foreach (var dayOfWeek in Enum.GetValues<DayOfWeek>())
            {
                if ((weeklyDaysMask & GetWeekdayMask(dayOfWeek)) != 0)
                {
                    selectedDays.Add(dayOfWeek);
                }
            }

            return selectedDays;
        }

        private static bool TryParseDayOfWeekTag(object? rawValue, out DayOfWeek dayOfWeek)
        {
            if (rawValue is string text && Enum.TryParse<DayOfWeek>(text, true, out dayOfWeek))
            {
                return true;
            }

            dayOfWeek = default;
            return false;
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

        private int GetEntryWeeklyDaysMask()
        {
            var weeklyDaysMask = 0;
            if (AutomationWeeklyMondayCheckBox.IsChecked == true) weeklyDaysMask |= GetWeekdayMask(DayOfWeek.Monday);
            if (AutomationWeeklyTuesdayCheckBox.IsChecked == true) weeklyDaysMask |= GetWeekdayMask(DayOfWeek.Tuesday);
            if (AutomationWeeklyWednesdayCheckBox.IsChecked == true) weeklyDaysMask |= GetWeekdayMask(DayOfWeek.Wednesday);
            if (AutomationWeeklyThursdayCheckBox.IsChecked == true) weeklyDaysMask |= GetWeekdayMask(DayOfWeek.Thursday);
            if (AutomationWeeklyFridayCheckBox.IsChecked == true) weeklyDaysMask |= GetWeekdayMask(DayOfWeek.Friday);
            if (AutomationWeeklySaturdayCheckBox.IsChecked == true) weeklyDaysMask |= GetWeekdayMask(DayOfWeek.Saturday);
            if (AutomationWeeklySundayCheckBox.IsChecked == true) weeklyDaysMask |= GetWeekdayMask(DayOfWeek.Sunday);
            return weeklyDaysMask == 0 ? 62 : weeklyDaysMask;
        }

        private static void CopyDirectory(
            string sourceDirectory,
            string destinationDirectory,
            HashSet<string> excludedFolderNames,
            BackupAutomationSummary summary,
            BackupAutomationLogScope logScope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationDirectory);

            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
                if (!ShouldCopyFile(file, destinationFile))
                {
                    continue;
                }

                File.Copy(file, destinationFile, overwrite: true);
                summary.CopiedFiles++;
                logScope.WriteLine($"複製檔案：{destinationFile}");
            }

            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldExcludeDirectory(directory, excludedFolderNames))
                {
                    summary.SkippedDirectories++;
                    continue;
                }

                var attributes = File.GetAttributes(directory);
                if (attributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
                {
                    continue;
                }

                var childDestination = Path.Combine(destinationDirectory, Path.GetFileName(directory));
                CopyDirectory(directory, childDestination, excludedFolderNames, summary, logScope, cancellationToken);
            }
        }

        private static void SyncDirectoryMirror(
            string sourceDirectory,
            string destinationDirectory,
            HashSet<string> excludedFolderNames,
            BackupAutomationSummary summary,
            BackupAutomationLogScope logScope,
            CancellationToken cancellationToken)
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
                if (!ShouldCopyFile(sourceFile, destinationFile))
                {
                    continue;
                }

                File.Copy(sourceFile, destinationFile, overwrite: true);
                summary.CopiedFiles++;
                logScope.WriteLine($"複製檔案：{destinationFile}");
            }

            foreach (var destinationFile in Directory.EnumerateFiles(destinationDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(destinationFile);
                if (!sourceFiles.ContainsKey(fileName))
                {
                    File.Delete(destinationFile);
                    summary.DeletedFiles++;
                    logScope.WriteLine($"刪除檔案：{destinationFile}");
                }
            }

            var sourceDirectories = Directory.EnumerateDirectories(sourceDirectory)
                .Where(directory =>
                {
                    if (!ShouldExcludeDirectory(directory, excludedFolderNames))
                    {
                        return true;
                    }

                    summary.SkippedDirectories++;
                    return false;
                })
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
                SyncDirectoryMirror(sourceChildDirectory, destinationChildDirectory, excludedFolderNames, summary, logScope, cancellationToken);
            }

            foreach (var destinationChildDirectory in Directory.EnumerateDirectories(destinationDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldExcludeDirectory(destinationChildDirectory, excludedFolderNames))
                {
                    continue;
                }

                var directoryName = Path.GetFileName(destinationChildDirectory);
                if (!sourceDirectories.ContainsKey(directoryName))
                {
                    Directory.Delete(destinationChildDirectory, recursive: true);
                    summary.DeletedDirectories++;
                    logScope.WriteLine($"刪除資料夾：{destinationChildDirectory}");
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
            var backgroundWorkId = BeginBackgroundWork(BuildAutoExtractBackgroundWorkTitle(profile), isAutomation: true);
            string? completionRecord = null;
            string? notificationSummary = null;
            string? notificationDetails = null;

            await EnqueueOnUiAsync(() =>
            {
                profile.IsRunning = true;
                profile.LastResultText = triggeredByWatcher ? "偵測到新壓縮檔，處理中..." : "手動掃描中...";
                UpdateSharedStatusBar();
            });

            try
            {
                var result = await Task.Run(() => ExecuteAutoExtractProfile(profile, cancellationTokenSource.Token), cancellationTokenSource.Token);

                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = result.StatusText;
                    UpdateSharedStatusBar();
                });
                completionRecord = $"完成：{profile.Name} · {result.StatusText}";
                notificationSummary = BuildAutoExtractNotificationSummary(profile, result.StatusText);
                notificationDetails = BuildAutoExtractExecutionDetail(profile, result);

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
                    UpdateSharedStatusBar();
                });
                completionRecord = $"停止：{profile.Name}";
                notificationSummary = $"已停止：{profile.Name}";
                notificationDetails = BuildAutoExtractProfileDetail(profile);
            }
            catch (Exception ex)
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.LastRunText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    profile.LastResultText = $"失敗：{ex.Message}";
                    UpdateSharedStatusBar();
                });
                completionRecord = $"失敗：{profile.Name} · {ex.Message}";
                notificationSummary = $"執行失敗：{profile.Name}";
                notificationDetails = completionRecord;
            }
            finally
            {
                await EnqueueOnUiAsync(() =>
                {
                    profile.IsRunning = false;
                    SaveAutoExtractProfilesSafe();
                    UpdateSharedStatusBar();
                });

                if (_autoExtractCancellationTokens.Remove(profile.Id, out var activeCancellationTokenSource))
                {
                    activeCancellationTokenSource.Dispose();
                }

                CompleteBackgroundWork(backgroundWorkId, completionRecord, persistToLocalHistory: false);

                lock (_runningAutoExtractIds)
                {
                    _runningAutoExtractIds.Remove(profile.Id);
                }

                if (!string.IsNullOrWhiteSpace(notificationSummary))
                {
                    AddAutomationNotification("自動解壓", notificationSummary, notificationDetails ?? notificationSummary, profile.NotificationEnabled, profile.ToastEnabled);
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

        private static string BuildAutoExtractBackgroundWorkTitle(AutoExtractProfile profile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(profile.WatchPath) || !Directory.Exists(profile.WatchPath))
                {
                    return $"{profile.Name} 解壓中";
                }

                var archives = Directory.EnumerateFiles(profile.WatchPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => IsAutoExtractPathAllowed(profile, path))
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (archives.Count == 0)
                {
                    return $"{profile.Name} 解壓中";
                }

                var firstName = Path.GetFileName(archives[0]);
                if (archives.Count == 1)
                {
                    return $"解壓 {firstName} 中";
                }

                return $"解壓 {firstName} 等 {archives.Count} 個檔案中";
            }
            catch
            {
                return $"{profile.Name} 解壓中";
            }
        }

        private static string BuildAutoExtractProfileDetail(AutoExtractProfile profile)
        {
            var builder = new StringBuilder();
            builder.Append("名稱：");
            builder.AppendLine(profile.Name);
            builder.Append("監看目錄：");
            builder.AppendLine(profile.WatchPath);
            builder.Append("副檔名：");
            builder.AppendLine(GetAutoExtractExtensionSummary(profile.ExtensionFilter));
            builder.Append("狀態：");
            builder.AppendLine(profile.LastResultText);
            return builder.ToString().TrimEnd();
        }

        private static string BuildAutoExtractNotificationSummary(AutoExtractProfile profile, string statusText)
        {
            return statusText.StartsWith("失敗", StringComparison.Ordinal)
                ? $"解壓失敗：{profile.Name}"
                : $"解壓完成：{profile.Name}";
        }

        private static string BuildAutoExtractExecutionDetail(AutoExtractProfile profile, AutoExtractExecutionResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine(BuildAutoExtractProfileDetail(profile));
            builder.AppendLine();
            builder.Append(result.StatusText);
            if (result.ShouldShowPasswordMismatchDialog && !string.IsNullOrWhiteSpace(result.PasswordMismatchDialogMessage))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append(result.PasswordMismatchDialogMessage);
            }

            return builder.ToString().TrimEnd();
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
                .Where(path => IsAutoExtractPathAllowed(profile, path))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (archives.Count == 0)
            {
                return AutoExtractExecutionResult.Create("監看目錄內沒有符合副檔名篩選的壓縮檔");
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

        internal static bool IsAutoExtractPathAllowed(AutoExtractProfile profile, string path)
        {
            if (!IsSupportedArchivePath(path))
            {
                return false;
            }

            var extensions = ParseAutoExtractExtensions(profile.ExtensionFilter);
            return string.IsNullOrWhiteSpace(profile.ExtensionFilter) ||
                extensions.Contains(Path.GetExtension(path));
        }

        internal static string GetAutoExtractExtensionSummary(string extensionFilter)
        {
            var extensions = ParseAutoExtractExtensions(extensionFilter);
            if (string.IsNullOrWhiteSpace(extensionFilter))
            {
                return string.Join(" ", SupportedArchiveExtensions.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
            }

            return extensions.Count == 0
                ? "無有效副檔名"
                : string.Join(" ", extensions.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        }

        private static HashSet<string> ParseAutoExtractExtensions(string extensionFilter)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(extensionFilter))
            {
                return extensions;
            }

            foreach (var value in extensionFilter.Split(
                new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var extension = value.StartsWith(".", StringComparison.Ordinal) ? value : $".{value}";
                if (SupportedArchiveExtensions.Contains(extension))
                {
                    extensions.Add(extension);
                }
            }

            return extensions;
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
