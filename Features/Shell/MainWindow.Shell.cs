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
        private static readonly TimeSpan BackgroundWorkToastThreshold = TimeSpan.FromMilliseconds(1200);
        private Flyout? _backgroundWorkNotificationFlyout;

        private void ShowFileManagerApp_Click(object sender, RoutedEventArgs e)
        {
            _settingsCaptureTarget = ShortcutCaptureTarget.None;
            SwitchToAppSection(AppSection.FileManager);
        }

        private void ShowSettingsApp_Click(object sender, RoutedEventArgs e)
        {
            ResetEditableShortcutSettings();
            SwitchToSettingsSection(SettingsSection.General);
            SwitchToAppSection(AppSection.Settings);
        }

        private void ShowAutomationApp_Click(object sender, RoutedEventArgs e)
        {
            SwitchToAppSection(AppSection.Automation);
        }

        private void ShowTerminalApp_Click(object sender, RoutedEventArgs e)
        {
            OpenTerminalSection(useActivePaneWhenAvailable: true);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            ResetEditableShortcutSettings();
            SwitchToSettingsSection(SettingsSection.General);
            SwitchToAppSection(AppSection.Settings);
        }

        private void SwitchToAppSection(AppSection section)
        {
            CancelPendingFlyout();
            _activeSection = section;
            UpdateAppSectionVisuals();
            UpdateSharedStatusBar();
        }

        private void OpenTerminalSection(bool useActivePaneWhenAvailable = false, string? workingDirectoryOverride = null)
        {
            var preferredWorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectoryOverride)
                ? workingDirectoryOverride.Trim()
                : useActivePaneWhenAvailable &&
                    _shortcutSettings.DefaultTerminalWorkingDirectoryMode == ToolbarWorkingDirectoryMode.ActivePane
                    ? _activePane.CurrentPath?.Trim()
                    : null;
            EnsureTerminalTabExists(preferredWorkingDirectory);
            SwitchToAppSection(AppSection.Terminal);
            FocusTerminalHost();
        }

        private void UpdateAppSectionVisuals()
        {
            var isFileManager = _activeSection == AppSection.FileManager;
            var isAutomation = _activeSection == AppSection.Automation;
            var isTerminal = _activeSection == AppSection.Terminal;
            FileManagerView.Visibility = isFileManager ? Visibility.Visible : Visibility.Collapsed;
            AutomationView.Visibility = isAutomation ? Visibility.Visible : Visibility.Collapsed;
            TerminalView.Visibility = isTerminal ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = _activeSection == AppSection.Settings ? Visibility.Visible : Visibility.Collapsed;

            ApplyAppRailButtonState(
                FileManagerAppButtonBorder,
                FileManagerAppIcon,
                FileManagerAppText,
                isFileManager);

            ApplyAppRailButtonState(
                AutomationAppButtonBorder,
                AutomationAppIcon,
                AutomationAppText,
                isAutomation);

            ApplyAppRailButtonState(
                TerminalAppButtonBorder,
                TerminalAppIcon,
                TerminalAppText,
                isTerminal);

            ApplyAppRailButtonState(
                SettingsAppButtonBorder,
                SettingsAppIcon,
                SettingsAppText,
                _activeSection == AppSection.Settings);
        }

        private void ApplyAppRailButtonState(Border border, FontIcon icon, TextBlock label, bool isActive)
        {
            var activeBackground = GetBrushColor("AppRailActiveBrush", "#2D2835");
            var activeBorder = GetBrushColor("AppRailActiveBorderBrush", "#4A3E58");
            var inactiveBackground = ColorHelper.FromArgb(0, 0, 0, 0);
            var activeText = GetBrushColor("TextPrimaryBrush", "#F6F2FF");
            var inactiveText = GetBrushColor("TextSecondaryBrush", "#B9AECF");

            border.Background = new SolidColorBrush(isActive ? activeBackground : inactiveBackground);
            border.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBackground);
            icon.Foreground = new SolidColorBrush(isActive ? activeText : inactiveText);
            label.Foreground = new SolidColorBrush(isActive ? activeText : inactiveText);
        }

        private void UpdateSharedStatusBar()
        {
            if (SharedStatusSectionText is null ||
                SharedStatusPrimaryText is null ||
                SharedStatusDetailText is null ||
                BackgroundWorkTopStatusText is null ||
                BackgroundWorkTopProgressRing is null ||
                BackgroundWorkHistoryCountBadge is null ||
                BackgroundWorkHistoryCountText is null)
            {
                return;
            }

            switch (_activeSection)
            {
                case AppSection.Automation:
                    {
                        var totalJobs = BackupAutomations.Count + AutoExtractProfiles.Count;
                        var enabledJobs = BackupAutomations.Count(profile => profile.IsEnabled) +
                            AutoExtractProfiles.Count(profile => profile.IsEnabled);
                        var runningJobs = BackupAutomations.Count(profile => profile.IsRunning) +
                            AutoExtractProfiles.Count(profile => profile.IsRunning);
                        SharedStatusSectionText.Text = "自動化";
                        SharedStatusPrimaryText.Text = $"{totalJobs} 個工作 / {enabledJobs} 個已啟用";
                        SharedStatusDetailText.Text = !IsAutomationExecutionOwner
                            ? "自動化由另一個 nuone-tools 視窗執行中"
                            : runningJobs > 0
                                ? $"{runningJobs} 個工作執行中"
                                : "目前沒有執行中的工作";
                        break;
                    }
                case AppSection.Terminal:
                    SharedStatusSectionText.Text = "終端機";
                    SharedStatusPrimaryText.Text = GetTerminalSharedStatusPrimaryText();
                    SharedStatusDetailText.Text = GetTerminalSharedStatusDetailText();
                    break;
                case AppSection.Settings:
                    SharedStatusSectionText.Text = "設定";
                    SharedStatusPrimaryText.Text = $"目前分頁 · {GetSettingsSectionTitle(_activeSettingsSection)}";
                    SharedStatusDetailText.Text = _activeSettingsSection switch
                    {
                        SettingsSection.Account => string.IsNullOrWhiteSpace(_accountSettings.Token)
                            ? "尚未登入 Nuone API"
                            : $"已登入 · {_accountSettings.UserDisplayName}",
                        _ => "變更會立即儲存到本機 config",
                    };
                    break;
                default:
                    SharedStatusSectionText.Text = "檔案管理";
                    SharedStatusPrimaryText.Text = $"{_activePane.Name} · {BuildSharedStatusPath(_activePane)}";
                    SharedStatusDetailText.Text = BuildFileManagerStatusDetail(_activePane);
                    break;
            }

            var backgroundWorkSummary = BuildBackgroundWorkSummary();
            BackgroundWorkTopStatusText.Text = backgroundWorkSummary ?? "目前沒有執行中的工作";
            BackgroundWorkTopProgressRing.Visibility = HasRunningBackgroundWork()
                ? Visibility.Visible
                : Visibility.Collapsed;
            var historyCount = GetBackgroundWorkRecordCount();
            BackgroundWorkHistoryCountText.Text = historyCount > 99
                ? "99+"
                : historyCount.ToString(CultureInfo.InvariantCulture);
            BackgroundWorkHistoryCountBadge.Visibility = historyCount > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private Guid BeginBackgroundWork(string label, string? details = null, bool isAutomation = false)
        {
            var workId = Guid.NewGuid();
            lock (_backgroundWorkLock)
            {
                _backgroundWorks[workId] = new BackgroundWorkState
                {
                    Label = label,
                    Details = string.IsNullOrWhiteSpace(details) ? label : details.Trim(),
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    IsAutomation = isAutomation,
                };
                AddBackgroundWorkRecordLocked($"開始：{label}", details, isAutomation);
            }

            EnqueueSharedStatusBarRefresh();
            return workId;
        }

        private void UpdateBackgroundWork(Guid workId, string? details = null, double? progressPercent = null)
        {
            lock (_backgroundWorkLock)
            {
                if (!_backgroundWorks.TryGetValue(workId, out var work))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(details))
                {
                    work.Details = details.Trim();
                }

                if (progressPercent.HasValue)
                {
                    work.ProgressPercent = Math.Clamp(progressPercent.Value, 0, 100);
                }
            }

            EnqueueSharedStatusBarRefresh();
        }

        private void CompleteBackgroundWork(
            Guid workId,
            string? completionRecord = null,
            string? completionDetails = null,
            bool persistToLocalHistory = true)
        {
            lock (_backgroundWorkLock)
            {
                if (_backgroundWorks.Remove(workId, out var work))
                {
                    var elapsed = DateTimeOffset.UtcNow - work.StartedAtUtc;
                    var recordText = string.IsNullOrWhiteSpace(completionRecord)
                        ? $"完成：{NormalizeBackgroundWorkCompletionLabel(work.Label)}"
                        : completionRecord;
                    var recordDetails = string.IsNullOrWhiteSpace(completionDetails)
                        ? (string.IsNullOrWhiteSpace(work.Details) ? recordText : work.Details)
                        : completionDetails.Trim();
                    AddBackgroundWorkRecordLocked(recordText, recordDetails, work.IsAutomation);

                    if (persistToLocalHistory && work.IsAutomation)
                    {
                        AddNotificationHistoryRecord(
                            NotificationHistoryScope.LocalOnly,
                            "背景工作",
                            recordText,
                            recordDetails,
                            showWindowsToast: elapsed >= BackgroundWorkToastThreshold);
                    }
                }
            }

            EnqueueSharedStatusBarRefresh();
        }

        private static string NormalizeBackgroundWorkCompletionLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            var trimmed = label.Trim();
            return trimmed.EndsWith("中", StringComparison.Ordinal)
                ? trimmed[..^1].TrimEnd()
                : trimmed;
        }

        private void BackgroundWorkNotification_Click(object sender, RoutedEventArgs e)
        {
            if (BackgroundWorkNotificationButton is null)
            {
                return;
            }

            var flyout = _backgroundWorkNotificationFlyout ??= new Flyout
            {
                Placement = FlyoutPlacementMode.Bottom,
            };
            flyout.Content = BuildBackgroundWorkNotificationContent(flyout);

            if (flyout.IsOpen)
            {
                flyout.Hide();
            }

            flyout.ShowAt(BackgroundWorkNotificationButton);
        }

        private FrameworkElement BuildBackgroundWorkNotificationContent(Flyout flyout)
        {
            var records = GetBackgroundWorkRecordsSnapshot();
            var localHistory = GetNotificationHistorySnapshot(NotificationHistoryScope.LocalOnly);
            var syncHistory = GetNotificationHistorySnapshot(NotificationHistoryScope.Sync);
            var entries = BuildNotificationListEntries(records, localHistory, syncHistory);
            var panel = new StackPanel
            {
                Width = 380,
                Spacing = 12,
            };

            panel.Children.Add(new TextBlock
            {
                Text = "背景工作記錄",
                Foreground = new SolidColorBrush(GetBrushColor("TextPrimaryBrush", "#F6F2FF")),
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });

            panel.Children.Add(new ScrollViewer
            {
                Content = BuildNotificationSummaryList(flyout, entries),
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });

            var clearButton = new Button
            {
                Content = "清除記錄",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = entries.Count > 0,
            };
            clearButton.Click += (_, _) =>
            {
                ClearAllNotificationRecords();
                flyout.Hide();
            };
            panel.Children.Add(clearButton);

            return panel;
        }

        private static string NormalizeBackgroundWorkRecordForTextBox(string record)
        {
            return record
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }

        private FrameworkElement BuildNotificationSectionHeader(string title, int count)
        {
            var panel = new StackPanel
            {
                Spacing = 2,
            };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(GetBrushColor("TextPrimaryBrush", "#F6F2FF")),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            panel.Children.Add(new TextBlock
            {
                Text = count == 0 ? "目前沒有記錄" : $"{count.ToString(CultureInfo.InvariantCulture)} 筆",
                Foreground = new SolidColorBrush(GetBrushColor("TextSecondaryBrush", "#B9AECF")),
                FontSize = 11,
            });
            return panel;
        }

        private FrameworkElement BuildNotificationSummaryList(
            Flyout flyout,
            IReadOnlyList<NotificationListEntry> entries)
        {
            var host = new StackPanel
            {
                Spacing = 8,
            };

            if (entries.Count == 0)
            {
                host.Children.Add(new TextBlock
                {
                    Text = "尚無通知記錄。",
                    Foreground = new SolidColorBrush(GetBrushColor("TextSecondaryBrush", "#B9AECF")),
                    TextWrapping = TextWrapping.Wrap,
                });
                return host;
            }

            foreach (var entry in entries.Take(30))
            {
                if (entry.IsGroupHeader)
                {
                    host.Children.Add(BuildNotificationGroupHeader(entry.GroupHeaderText));
                    continue;
                }

                host.Children.Add(BuildSummaryButton(entry, () =>
                {
                    flyout.Content = BuildNotificationDetailContent(flyout, entry);
                }));
            }

            return host;
        }

        private FrameworkElement BuildNotificationGroupHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(GetBrushColor("TextSecondaryBrush", "#B9AECF")),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(2, 6, 2, 0),
            };
        }

        private Button BuildSummaryButton(NotificationListEntry entry, Action onClick)
        {
            var button = new Button
            {
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    Background = new SolidColorBrush(GetBrushColor("InputAltBrush", "#231E2B")),
                    BorderBrush = new SolidColorBrush(GetBrushColor("PanelStrokeBrush", "#3A3146")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Child = new TextBlock
                    {
                        Text = BuildNotificationSummaryText(entry),
                        Foreground = new SolidColorBrush(GetBrushColor("TextPrimaryBrush", "#F6F2FF")),
                        FontSize = 12,
                        MaxLines = 3,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };
            button.Click += (_, _) => onClick();
            return button;
        }

        private FrameworkElement BuildNotificationDetailContent(Flyout flyout, NotificationListEntry entry)
        {
            var panel = new StackPanel
            {
                Width = 380,
                Spacing = 12,
            };

            panel.Children.Add(new TextBlock
            {
                Text = "detail",
                Foreground = new SolidColorBrush(GetBrushColor("TextPrimaryBrush", "#F6F2FF")),
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            panel.Children.Add(new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = entry.DialogText,
                    Foreground = new SolidColorBrush(GetBrushColor("TextPrimaryBrush", "#F6F2FF")),
                    FontSize = 12,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap,
                },
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });

            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
            };
            var backButton = new Button
            {
                Content = "返回 resumen",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            backButton.Click += (_, _) =>
            {
                flyout.Content = BuildBackgroundWorkNotificationContent(flyout);
            };
            var copyButton = new Button
            {
                Content = "複製 detail",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            copyButton.Click += (_, _) =>
            {
                CopyTextToClipboard(entry.DialogText);
            };
            actionPanel.Children.Add(backButton);
            actionPanel.Children.Add(copyButton);
            panel.Children.Add(actionPanel);

            return panel;
        }

        private static string BuildNotificationSummaryText(NotificationListEntry entry)
        {
            return $"[{entry.ScopeLabel}] {entry.CardText}";
        }

        private List<NotificationListEntry> BuildNotificationListEntries(
            IReadOnlyList<BackgroundWorkRecord> sessionRecords,
            IReadOnlyList<NotificationHistoryRecord> localHistory,
            IReadOnlyList<NotificationHistoryRecord> syncHistory)
        {
            var entries = new List<NotificationListEntry>();
            foreach (var work in GetRunningBackgroundWorksSnapshot().Where(static work => work.IsAutomation))
            {
                var timestampText = work.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var progressText = work.ProgressPercent.HasValue
                    ? $" · {work.ProgressPercent.Value:0}%"
                    : string.Empty;
                var summary = $"{timestampText}　進行中：{work.Label}{progressText}";
                var detailBuilder = new StringBuilder();
                detailBuilder.Append("時間：");
                detailBuilder.AppendLine(timestampText);
                detailBuilder.AppendLine();
                detailBuilder.Append("進行中：");
                detailBuilder.AppendLine(work.Label);

                if (!string.IsNullOrWhiteSpace(work.Details))
                {
                    detailBuilder.AppendLine();
                    detailBuilder.AppendLine(work.Details);
                }

                entries.Add(new NotificationListEntry
                {
                    ScopeLabel = "進行中",
                    Timestamp = work.Timestamp,
                    CardText = summary,
                    DialogText = detailBuilder.ToString().TrimEnd(),
                });
            }

            foreach (var record in sessionRecords.Where(static record => record.IsAutomation))
            {
                var timestampText = record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var summary = $"{timestampText}　{record.Summary}";
                var detailBuilder = new StringBuilder();
                detailBuilder.Append("時間：");
                detailBuilder.AppendLine(timestampText);
                detailBuilder.AppendLine();
                detailBuilder.AppendLine(record.Summary);

                if (!string.Equals(record.Details.Trim(), record.Summary.Trim(), StringComparison.Ordinal))
                {
                    detailBuilder.AppendLine();
                    detailBuilder.AppendLine(record.Details.Trim());
                }

                entries.Add(new NotificationListEntry
                {
                    ScopeLabel = "本次",
                    Timestamp = record.Timestamp,
                    CardText = summary,
                    DialogText = detailBuilder.ToString().TrimEnd(),
                });
            }

            foreach (var record in localHistory.Where(static record => IsTrackedNotificationCategory(record.Category)))
            {
                var dialogText = BuildNotificationHistoryDialogText(record);
                entries.Add(new NotificationListEntry
                {
                    ScopeLabel = "本機",
                    Timestamp = ParseNotificationTimestamp(record.CreatedAtUtc),
                    CardText = BuildNotificationHistoryCardText(record),
                    DialogText = dialogText,
                });
            }

            foreach (var record in syncHistory.Where(static record => IsTrackedNotificationCategory(record.Category)))
            {
                var dialogText = BuildNotificationHistoryDialogText(record);
                entries.Add(new NotificationListEntry
                {
                    ScopeLabel = "同步",
                    Timestamp = ParseNotificationTimestamp(record.CreatedAtUtc),
                    CardText = BuildNotificationHistoryCardText(record),
                    DialogText = dialogText,
                });
            }

            var orderedEntries = entries
                .OrderByDescending(static item => item.Timestamp)
                .ToList();

            var groupedEntries = new List<NotificationListEntry>();
            DateTime? currentGroupDate = null;
            foreach (var entry in orderedEntries)
            {
                var localDate = entry.Timestamp.ToLocalTime().Date;
                if (currentGroupDate != localDate)
                {
                    groupedEntries.Add(NotificationListEntry.CreateGroupHeader(FormatNotificationGroupDate(localDate)));
                    currentGroupDate = localDate;
                }

                groupedEntries.Add(entry);
            }

            return groupedEntries;
        }

        private static string BuildNotificationHistoryCardText(NotificationHistoryRecord record)
        {
            var timestampText = FormatNotificationTimestamp(record.CreatedAtUtc);
            var deviceText = string.IsNullOrWhiteSpace(record.DeviceName) ? string.Empty : $" [{record.DeviceName}]";
            var categoryText = string.IsNullOrWhiteSpace(record.Category) ? string.Empty : $" · {record.Category}";
            return $"{timestampText}{deviceText}{categoryText}{Environment.NewLine}{record.Summary}";
        }

        private static string BuildNotificationHistoryDialogText(NotificationHistoryRecord record)
        {
            var builder = new StringBuilder();
            builder.Append("時間：");
            builder.AppendLine(FormatNotificationTimestamp(record.CreatedAtUtc));

            if (!string.IsNullOrWhiteSpace(record.DeviceName))
            {
                builder.Append("裝置：");
                builder.AppendLine(record.DeviceName);
            }

            if (!string.IsNullOrWhiteSpace(record.Category))
            {
                builder.Append("分類：");
                builder.AppendLine(record.Category);
            }

            builder.AppendLine();
            builder.AppendLine(record.Summary);

            if (!string.IsNullOrWhiteSpace(record.Details) &&
                !string.Equals(record.Details.Trim(), record.Summary.Trim(), StringComparison.Ordinal))
            {
                builder.AppendLine();
                builder.AppendLine(record.Details.Trim());
            }

            return builder.ToString().TrimEnd();
        }

        private static string FormatNotificationTimestamp(string? createdAtUtc)
        {
            if (ParseNotificationTimestamp(createdAtUtc) is DateTimeOffset parsed)
            {
                return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatNotificationGroupDate(DateTime date)
        {
            return date == DateTime.Now.Date
                ? "今天"
                : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static DateTimeOffset ParseNotificationTimestamp(string? createdAtUtc)
        {
            if (DateTimeOffset.TryParse(createdAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }

            return DateTimeOffset.MinValue;
        }

        private void AddNotificationHistoryRecord(
            NotificationHistoryScope scope,
            string category,
            string summary,
            string details,
            bool showWindowsToast = true,
            bool persistRecord = true)
        {
            if (!IsTrackedNotificationCategory(category))
            {
                return;
            }

            var normalizedSummary = NormalizeBackgroundWorkRecordForTextBox(summary).Trim();
            if (string.IsNullOrWhiteSpace(normalizedSummary))
            {
                return;
            }

            var normalizedDetails = NormalizeBackgroundWorkRecordForTextBox(details).Trim();
            var record = new NotificationHistoryRecord
            {
                Id = Guid.NewGuid(),
                Scope = scope,
                Category = category?.Trim() ?? string.Empty,
                Summary = normalizedSummary,
                Details = string.IsNullOrWhiteSpace(normalizedDetails) ? normalizedSummary : normalizedDetails,
                CreatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                DeviceName = Environment.MachineName,
            };

            if (persistRecord)
            {
                lock (_notificationHistoryLock)
                {
                    var target = scope == NotificationHistoryScope.Sync
                        ? _syncNotificationHistory
                        : _localNotificationHistory;
                    target.Insert(0, record);
                    while (target.Count > 200)
                    {
                        target.RemoveAt(target.Count - 1);
                    }
                }

                SaveNotificationHistoriesSafe();
            }

            if (showWindowsToast)
            {
                WindowsNotificationService.Show(record);
            }
        }

        private List<NotificationHistoryRecord> GetNotificationHistorySnapshot(NotificationHistoryScope scope)
        {
            lock (_notificationHistoryLock)
            {
                var source = scope == NotificationHistoryScope.Sync
                    ? _syncNotificationHistory
                    : _localNotificationHistory;
                return source.ToList();
            }
        }

        private void ClearNotificationHistory(NotificationHistoryScope scope)
        {
            lock (_notificationHistoryLock)
            {
                var target = scope == NotificationHistoryScope.Sync
                    ? _syncNotificationHistory
                    : _localNotificationHistory;
                target.Clear();
            }

            SaveNotificationHistoriesSafe(mergeExistingRecords: false);
        }

        private void ClearAllNotificationRecords()
        {
            lock (_backgroundWorkLock)
            {
                _backgroundWorkRecords.Clear();
            }

            lock (_notificationHistoryLock)
            {
                _localNotificationHistory.Clear();
                _syncNotificationHistory.Clear();
            }

            SaveNotificationHistoriesSafe(mergeExistingRecords: false);
            UpdateSharedStatusBar();
        }

        private sealed class NotificationListEntry
        {
            public bool IsGroupHeader { get; init; }

            public string GroupHeaderText { get; init; } = string.Empty;

            public string ScopeLabel { get; init; } = string.Empty;

            public DateTimeOffset Timestamp { get; init; }

            public string CardText { get; init; } = string.Empty;

            public string DialogText { get; init; } = string.Empty;

            public static NotificationListEntry CreateGroupHeader(string text)
            {
                return new NotificationListEntry
                {
                    IsGroupHeader = true,
                    GroupHeaderText = text,
                };
            }
        }

        private static bool IsTrackedNotificationCategory(string? category)
        {
            return string.Equals(category?.Trim(), "自動化", StringComparison.Ordinal) ||
                   string.Equals(category?.Trim(), "自動解壓", StringComparison.Ordinal) ||
                   string.Equals(category?.Trim(), "更新", StringComparison.Ordinal);
        }

        private sealed class BackgroundWorkState
        {
            public string Label { get; set; } = string.Empty;

            public string Details { get; set; } = string.Empty;

            public DateTimeOffset StartedAtUtc { get; set; }

            public bool IsAutomation { get; set; }

            public double? ProgressPercent { get; set; }
        }

        private sealed class BackgroundWorkRecord
        {
            public DateTimeOffset Timestamp { get; init; }

            public string Summary { get; init; } = string.Empty;

            public string Details { get; init; } = string.Empty;

            public bool IsAutomation { get; init; }
        }

        private sealed class RunningBackgroundWorkSnapshot
        {
            public DateTimeOffset Timestamp { get; init; }

            public string Label { get; init; } = string.Empty;

            public string Details { get; init; } = string.Empty;

            public bool IsAutomation { get; init; }

            public double? ProgressPercent { get; init; }
        }

        private void EnqueueSharedStatusBarRefresh()
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                RunSafely(UpdateSharedStatusBar, "shared status bar refresh");
                return;
            }

            TryEnqueueUi(UpdateSharedStatusBar, "shared status bar refresh");
        }

        private static string BuildSharedStatusPath(PaneViewModel pane)
        {
            return string.IsNullOrWhiteSpace(pane.CurrentPath) ? "尚未開啟路徑" : pane.CurrentPath;
        }

        private static string BuildFileManagerStatusDetail(PaneViewModel pane)
        {
            var segments = new List<string>();

            if (!string.IsNullOrWhiteSpace(pane.StatusText))
            {
                segments.Add(pane.StatusText);
            }

            if (!string.IsNullOrWhiteSpace(pane.SummaryText))
            {
                segments.Add(pane.SummaryText);
            }

            if (!string.IsNullOrWhiteSpace(pane.SelectionText))
            {
                segments.Add($"選取：{pane.SelectionText}");
            }

            return segments.Count == 0 ? "準備就緒" : string.Join(" / ", segments);
        }

        private static string GetSettingsSectionTitle(SettingsSection section)
        {
            return section switch
            {
                SettingsSection.Account => "帳號",
                SettingsSection.Appearance => "外觀",
                SettingsSection.Shortcuts => "快捷鍵",
                SettingsSection.Toolbar => "工具列",
                _ => "一般",
            };
        }

        private string? BuildBackgroundWorkSummary()
        {
            List<RunningBackgroundWorkSnapshot> works;
            lock (_backgroundWorkLock)
            {
                works = _backgroundWorks.Values
                    .Select(static work => new RunningBackgroundWorkSnapshot
                    {
                        Timestamp = work.StartedAtUtc,
                        Label = work.Label,
                        Details = work.Details,
                        IsAutomation = work.IsAutomation,
                        ProgressPercent = work.ProgressPercent,
                    })
                    .ToList();
            }

            if (works.Count == 0)
            {
                return null;
            }

            if (works.Count == 1)
            {
                var work = works[0];
                var progressText = work.ProgressPercent.HasValue
                    ? $" · {work.ProgressPercent.Value:0}%"
                    : string.Empty;
                return string.IsNullOrWhiteSpace(work.Details) || string.Equals(work.Details, work.Label, StringComparison.Ordinal)
                    ? $"{work.Label}{progressText}"
                    : $"{work.Label}{progressText} · {work.Details}";
            }

            return $"{works[0].Label} 等 {works.Count} 個背景工作";
        }

        private bool HasRunningBackgroundWork()
        {
            lock (_backgroundWorkLock)
            {
                return _backgroundWorks.Count > 0;
            }
        }

        private void AddBackgroundWorkRecordLocked(string summary, string? details = null, bool isAutomation = false)
        {
            var normalizedSummary = NormalizeBackgroundWorkRecordForTextBox(summary).Trim();
            if (string.IsNullOrWhiteSpace(normalizedSummary))
            {
                return;
            }

            var normalizedDetails = NormalizeBackgroundWorkRecordForTextBox(details ?? string.Empty).Trim();
            _backgroundWorkRecords.Insert(0, new BackgroundWorkRecord
            {
                Timestamp = DateTimeOffset.Now,
                Summary = normalizedSummary,
                Details = string.IsNullOrWhiteSpace(normalizedDetails)
                    ? normalizedSummary
                    : normalizedDetails,
                IsAutomation = isAutomation,
            });
            while (_backgroundWorkRecords.Count > 100)
            {
                _backgroundWorkRecords.RemoveAt(_backgroundWorkRecords.Count - 1);
            }
        }

        private int GetBackgroundWorkRecordCount()
        {
            lock (_backgroundWorkLock)
            {
                return _backgroundWorkRecords.Count(static record => record.IsAutomation);
            }
        }

        private List<BackgroundWorkRecord> GetBackgroundWorkRecordsSnapshot()
        {
            lock (_backgroundWorkLock)
            {
                return _backgroundWorkRecords
                    .Where(static record => record.IsAutomation)
                    .ToList();
            }
        }

        private List<RunningBackgroundWorkSnapshot> GetRunningBackgroundWorksSnapshot()
        {
            lock (_backgroundWorkLock)
            {
                return _backgroundWorks.Values
                    .Select(static work => new RunningBackgroundWorkSnapshot
                    {
                        Timestamp = work.StartedAtUtc,
                        Label = work.Label,
                        Details = work.Details,
                        IsAutomation = work.IsAutomation,
                        ProgressPercent = work.ProgressPercent,
                    })
                    .Where(static work => work.IsAutomation)
                    .OrderByDescending(static work => work.Timestamp)
                    .ToList();
            }
        }

        private void ClearBackgroundWorkRecords()
        {
            lock (_backgroundWorkLock)
            {
                _backgroundWorkRecords.Clear();
            }

            UpdateSharedStatusBar();
        }
    }
}
