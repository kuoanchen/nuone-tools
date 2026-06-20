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
        internal void ShowAddGroupEditor_Click(object sender, RoutedEventArgs e)
        {
            AddGroupEditor.Visibility = Visibility.Visible;
            NewGroupNameTextBox.Text = string.Empty;
            _ = NewGroupNameTextBox.Focus(FocusState.Programmatic);
        }

        internal void CancelAddGroup_Click(object sender, RoutedEventArgs e)
        {
            HideAddGroupEditor();
        }

        internal void ConfirmAddGroup_Click(object sender, RoutedEventArgs e)
        {
            TryAddGroup();
        }

        internal void NewGroupNameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                TryAddGroup();
            }
        }

        internal void AddCurrentPathToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPathGroup(sender, out var group))
            {
                return;
            }

            if (AddPathToGroup(group, _activePane.CurrentPath))
            {
                SaveCustomGroupsSafe();
            }
        }

        private void GroupedPath_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path })
            {
                OpenInPane(_activePane, path);
            }
        }

        internal void GroupedPath_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: GroupedPathItem item })
            {
                OpenInPane(_activePane, item.Path);
                e.Handled = true;
            }
        }

        internal void GroupedPathItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(GetBrushColor("InputAltBrush", "#231E2B"));
                border.BorderBrush = new SolidColorBrush(GetBrushColor("PanelStrokeBrush", "#3A3146"));
            }
        }

        internal void GroupedPathItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
                border.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
        }

        internal async void RenameGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPathGroup(sender, out var group))
            {
                return;
            }

            var newName = await PromptForTextAsync("群組改名", "輸入新的群組名稱", group.Title);
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, group.Title, StringComparison.Ordinal))
            {
                return;
            }

            if (CustomGroups.Any(item => !ReferenceEquals(item, group) && string.Equals(item.Title, newName, StringComparison.OrdinalIgnoreCase)))
            {
                await ShowMessageAsync("名稱重複", "已有相同名稱的分組。");
                return;
            }

            group.Title = newName;
            SaveCustomGroupsSafe();
        }

        internal async void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPathGroup(sender, out var group))
            {
                return;
            }

            var confirmed = await ConfirmAsync("刪除分組", $"確定要刪除分組「{group.Title}」嗎？");
            if (!confirmed)
            {
                return;
            }

            CustomGroups.Remove(group);
            SaveCustomGroupsSafe();
        }

        internal void GroupedPathOpen_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetGroupedPathItem(sender, out var item))
            {
                OpenInPane(_activePane, item.Path);
            }
        }

        internal async void RenameGroupedPathAlias_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetGroupedPathItem(sender, out var item))
            {
                return;
            }

            var newAlias = await PromptForTextAsync("更改別名", "輸入新的顯示名稱", item.Title);
            if (string.IsNullOrWhiteSpace(newAlias) || string.Equals(newAlias, item.Title, StringComparison.Ordinal))
            {
                return;
            }

            item.Title = newAlias;
            SaveCustomGroupsSafe();
        }

        internal async void RemoveGroupedPath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetGroupedPathItem(sender, out var item) || item.ParentGroup is null)
            {
                return;
            }

            var confirmed = await ConfirmAsync("移除捷徑", $"確定要從分組移除「{item.Title}」嗎？");
            if (!confirmed)
            {
                return;
            }

            item.ParentGroup.Items.Remove(item);
            SaveCustomGroupsSafe();
        }

        private void QueueSelectionFlyout(PaneViewModel pane, FrameworkElement target, string path)
        {
            _pendingFlyoutPane = pane;
            _pendingFlyoutTarget = target;
            _pendingFlyoutPath = path;
            _selectionFlyoutTimer.Stop();
            _selectionFlyoutTimer.Start();
        }

        private void CancelPendingFlyout()
        {
            if (_pendingFlyoutTarget is not null &&
                FlyoutBase.GetAttachedFlyout(_pendingFlyoutTarget) is FlyoutBase flyout)
            {
                flyout.Hide();
            }

            _selectionFlyoutTimer.Stop();
            _pendingFlyoutPane = null;
            _pendingFlyoutTarget = null;
            _pendingFlyoutPath = null;
        }

        private void SelectionFlyoutTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();

            if (_pendingFlyoutPane is null || _pendingFlyoutTarget is null || string.IsNullOrWhiteSpace(_pendingFlyoutPath))
            {
                CancelPendingFlyout();
                return;
            }

            if (_pendingFlyoutPane.SelectedItem is null || !PathEquals(_pendingFlyoutPane.SelectedItem.FullPath, _pendingFlyoutPath))
            {
                CancelPendingFlyout();
                return;
            }

            if (FlyoutBase.GetAttachedFlyout(_pendingFlyoutTarget) is FlyoutBase flyout)
            {
                flyout.ShowAt(_pendingFlyoutTarget);
            }

            CancelPendingFlyout();
        }

        private void TryAddGroup()
        {
            var title = NewGroupNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            if (CustomGroups.Any(group => string.Equals(group.Title, title, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            CustomGroups.Add(new PathGroup(title));
            SaveCustomGroupsSafe();
            HideAddGroupEditor();
        }

        private void HideAddGroupEditor()
        {
            NewGroupNameTextBox.Text = string.Empty;
            AddGroupEditor.Visibility = Visibility.Collapsed;
        }

        private void LoadCustomGroups()
        {
            CustomGroups.Clear();

            var groups = LoadGroupConfigs();
            foreach (var group in groups.Where(static group => !string.IsNullOrWhiteSpace(group.Title)))
            {
                var pathGroup = new PathGroup(group.Title);
                foreach (var item in group.Items.Where(static item => !string.IsNullOrWhiteSpace(item.Path)))
                {
                    pathGroup.Items.Add(new GroupedPathItem
                    {
                        Title = string.IsNullOrWhiteSpace(item.Title)
                            ? GetDisplayName(item.Path)
                            : item.Title,
                        Path = item.Path,
                        ParentGroup = pathGroup,
                    });
                }

                CustomGroups.Add(pathGroup);
            }
        }

        private static List<PathGroupConfig> LoadGroupConfigs()
        {
            try
            {
                var localSettings = LoadLocalSettingsConfig();
                if (localSettings?.Groups is { Count: > 0 })
                {
                    return localSettings.Groups;
                }

                if (File.Exists(LegacyGroupsConfigPath))
                {
                    return JsonSerializer.Deserialize<List<PathGroupConfig>>(File.ReadAllText(LegacyGroupsConfigPath), JsonOptions) ?? new();
                }
            }
            catch
            {
            }

            return new List<PathGroupConfig>();
        }
    }
}
