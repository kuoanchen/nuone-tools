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

        private void UpdateAppSectionVisuals()
        {
            var isFileManager = _activeSection == AppSection.FileManager;
            var isAutomation = _activeSection == AppSection.Automation;
            FileManagerView.Visibility = isFileManager ? Visibility.Visible : Visibility.Collapsed;
            AutomationView.Visibility = isAutomation ? Visibility.Visible : Visibility.Collapsed;
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
                SharedStatusBackgroundBorder is null ||
                SharedStatusBackgroundText is null)
            {
                return;
            }

            switch (_activeSection)
            {
                case AppSection.Automation:
                    {
                        var totalJobs = BackupAutomations.Count;
                        var enabledJobs = BackupAutomations.Count(profile => profile.IsEnabled);
                        var runningJobs = BackupAutomations.Count(profile => profile.IsRunning);
                        SharedStatusSectionText.Text = "自動化";
                        SharedStatusPrimaryText.Text = $"{totalJobs} 個工作 / {enabledJobs} 個已啟用";
                        SharedStatusDetailText.Text = runningJobs > 0
                            ? $"{runningJobs} 個工作執行中"
                            : "目前沒有執行中的工作";
                        break;
                    }
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
            SharedStatusBackgroundBorder.Visibility = backgroundWorkSummary is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            SharedStatusBackgroundText.Text = backgroundWorkSummary ?? string.Empty;
            Grid.SetColumn(SharedStatusDetailText, backgroundWorkSummary is null ? 6 : 4);
            SharedStatusDetailText.HorizontalAlignment = backgroundWorkSummary is null
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
        }

        private Guid BeginBackgroundWork(string label)
        {
            var workId = Guid.NewGuid();
            lock (_backgroundWorkLock)
            {
                _backgroundWorks[workId] = label;
            }

            EnqueueSharedStatusBarRefresh();
            return workId;
        }

        private void CompleteBackgroundWork(Guid workId)
        {
            lock (_backgroundWorkLock)
            {
                _backgroundWorks.Remove(workId);
            }

            EnqueueSharedStatusBarRefresh();
        }

        private void EnqueueSharedStatusBarRefresh()
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                UpdateSharedStatusBar();
                return;
            }

            DispatcherQueue.TryEnqueue(UpdateSharedStatusBar);
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
                SettingsSection.AutoExtract => "自動解壓",
                _ => "一般",
            };
        }

        private string? BuildBackgroundWorkSummary()
        {
            List<string> labels;
            lock (_backgroundWorkLock)
            {
                labels = _backgroundWorks.Values.ToList();
            }

            if (labels.Count == 0)
            {
                return null;
            }

            if (labels.Count == 1)
            {
                return labels[0];
            }

            return $"{labels[0]} 等 {labels.Count} 個背景工作";
        }
    }
}
