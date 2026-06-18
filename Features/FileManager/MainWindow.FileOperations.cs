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
        internal void LeftPaneList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            HandlePaneDoubleTapped(LeftPane, e);
        }

        internal void RightPaneList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
            HandlePaneDoubleTapped(RightPane, e);
        }

        internal void LeftPaneEntry_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            HandleItemTapped(LeftPane, sender as FrameworkElement);
        }

        internal void RightPaneEntry_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
            HandleItemTapped(RightPane, sender as FrameworkElement);
        }

        internal async void LeftPaneEntry_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            await HandleEntryRightTappedAsync(LeftPane, sender as FrameworkElement, e);
        }

        internal async void RightPaneEntry_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
            await HandleEntryRightTappedAsync(RightPane, sender as FrameworkElement, e);
        }

        private void SyncPaneSelectionFromListView(PaneViewModel pane, ListView listView)
        {
            var selectedEntries = listView.SelectedItems.OfType<FileEntry>().ToList();
            pane.UpdateSelection(selectedEntries);
        }

        private IReadOnlyList<FileEntry> GetSelectedEntries(PaneViewModel pane)
        {
            var listView = ReferenceEquals(pane, LeftPane) ? LeftPaneListView : RightPaneListView;
            return listView.SelectedItems.OfType<FileEntry>().ToList();
        }

        private async Task UpdateSelectionSizeAsync(PaneViewModel pane)
        {
            var selectedEntries = GetSelectedEntries(pane);
            pane.UpdateSelectionText(BuildSelectionSummary(selectedEntries));

            if (MainWindow.IsSshPath(pane.CurrentPath))
            {
                return;
            }

            var shouldCalculateFileSize = _shortcutSettings.ShowSelectedFileSize;
            var shouldCalculateFolderSize = _shortcutSettings.ShowSelectedFolderSize;
            var selectedFiles = shouldCalculateFileSize
                ? selectedEntries.Where(static item => !item.IsDirectory).ToList()
                : new List<FileEntry>();
            var selectedFolders = shouldCalculateFolderSize
                ? selectedEntries.Where(static item => item.IsDirectory).ToList()
                : new List<FileEntry>();

            if (selectedEntries.Count == 0 || (selectedFiles.Count == 0 && selectedFolders.Count == 0))
            {
                return;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var previous = ReferenceEquals(pane, LeftPane)
                ? Interlocked.Exchange(ref _leftSelectionSizeCts, cancellationTokenSource)
                : Interlocked.Exchange(ref _rightSelectionSizeCts, cancellationTokenSource);
            previous?.Cancel();
            previous?.Dispose();

            var token = cancellationTokenSource.Token;
            pane.UpdateSelectionText($"{BuildSelectionSummary(selectedEntries)} / 計算大小中...");

            try
            {
                var totalSize = await Task.Run(() =>
                {
                    long size = 0;

                    foreach (var file in selectedFiles)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            if (File.Exists(file.FullPath))
                            {
                                size += new FileInfo(file.FullPath).Length;
                            }
                        }
                        catch
                        {
                        }
                    }

                    foreach (var folder in selectedFolders)
                    {
                        token.ThrowIfCancellationRequested();
                        size += CalculateDirectorySize(folder.FullPath, token);
                    }

                    return size;
                }, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                pane.UpdateSelectionText($"{BuildSelectionSummary(selectedEntries)} / {FormatSize(totalSize)}");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(pane, LeftPane))
                {
                    if (ReferenceEquals(_leftSelectionSizeCts, cancellationTokenSource))
                    {
                        _leftSelectionSizeCts = null;
                    }
                }
                else if (ReferenceEquals(_rightSelectionSizeCts, cancellationTokenSource))
                {
                    _rightSelectionSizeCts = null;
                }

                cancellationTokenSource.Dispose();
            }
        }

        private static string BuildSelectionSummary(IReadOnlyList<FileEntry> selectedEntries)
        {
            return selectedEntries.Count switch
            {
                0 => "未選取",
                1 => selectedEntries[0].Name,
                _ => $"{selectedEntries.Count} 個已選取",
            };
        }

        private static long CalculateDirectorySize(string path, CancellationToken cancellationToken)
        {
            long totalSize = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        totalSize += new FileInfo(file).Length;
                    }
                    catch
                    {
                    }
                }

                foreach (var directory in Directory.EnumerateDirectories(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(directory);
                        if (attributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    totalSize += CalculateDirectorySize(directory, cancellationToken);
                }
            }
            catch
            {
            }

            return totalSize;
        }

        private void HandleItemDoubleTapped(PaneViewModel pane, FileEntry? entry)
        {
            if (entry is null)
            {
                return;
            }

            pane.SelectedItem = entry;

            if (entry.IsDirectory)
            {
                OpenInPane(pane, entry.FullPath);
                return;
            }

            _ = OpenFileWithLoadingAsync(pane, entry.FullPath);
        }

        private void HandleItemTapped(PaneViewModel pane, FrameworkElement? element)
        {
            var entry = element?.DataContext as FileEntry;
            if (entry is null)
            {
                return;
            }

            var wasSelected = pane.SelectedItem is not null
                && PathEquals(pane.SelectedItem.FullPath, entry.FullPath)
                && pane.SelectedCount == 1;

            if (!wasSelected || element is null)
            {
                CancelPendingFlyout();
                return;
            }

            QueueSelectionFlyout(pane, element, entry.FullPath);
        }

        private async Task HandleEntryRightTappedAsync(PaneViewModel pane, FrameworkElement? element, RightTappedRoutedEventArgs e)
        {
            var entry = element?.DataContext as FileEntry;
            if (entry is null)
            {
                return;
            }

            if (IsSshPath(entry.FullPath) || IsSshPath(pane.CurrentPath))
            {
                return;
            }

            CancelPendingFlyout();
            SelectEntryForContextMenu(pane, entry);
            e.Handled = true;

            try
            {
                var selectedPaths = GetSelectedEntriesInDisplayOrder(pane)
                    .Select(item => item.FullPath)
                    .ToArray();
                if (selectedPaths.Length == 0)
                {
                    return;
                }

                var position = e.GetPosition(RootLayout);
                var primaryPath = entry.FullPath;
                ShellContextMenuHost.ShowForPaths(
                    WindowNative.GetWindowHandle(this),
                    RootLayout.XamlRoot.RasterizationScale,
                    position.X,
                    position.Y,
                    pane.CurrentPath,
                    selectedPaths,
                    BuildInjectedShellMenuItems(pane),
                    commandId => HandleInjectedShellMenuCommand(commandId, pane, selectedPaths, primaryPath));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("右鍵選單失敗", ex.Message);
            }
        }

        private void SelectEntryForContextMenu(PaneViewModel pane, FileEntry entry)
        {
            var listView = ReferenceEquals(pane, LeftPane) ? LeftPaneListView : RightPaneListView;
            var selectedEntries = listView.SelectedItems.OfType<FileEntry>().ToList();
            var isAlreadySelected = selectedEntries.Any(item => PathEquals(item.FullPath, entry.FullPath));

            if (!isAlreadySelected)
            {
                listView.SelectedItems.Clear();
                listView.SelectedItems.Add(entry);
                SyncPaneSelectionFromListView(pane, listView);
            }

            pane.SelectedItem = entry;
        }

        private void HandlePaneDoubleTapped(PaneViewModel pane, DoubleTappedRoutedEventArgs e)
        {
            CancelPendingFlyout();

            var entry = FindDataContext<FileEntry>(e.OriginalSource as DependencyObject);
            if (entry is not null)
            {
                HandleItemDoubleTapped(pane, entry);
                e.Handled = true;
                return;
            }

            NavigateUp(pane);
            e.Handled = true;
        }

        private void OpenSelected_Click(object sender, RoutedEventArgs e)
        {
            _ = OpenSelectedEntriesAsync();
        }

        internal void OpenPath_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetPath(sender, out var path))
            {
                if (IsSshPath(path))
                {
                    if (TryParseSshPath(path, out _, out var remotePath) &&
                        !string.Equals(remotePath, "/", StringComparison.Ordinal))
                    {
                        _ = OpenFileWithLoadingAsync(_activePane, path);
                        return;
                    }

                    OpenInPane(_activePane, path);
                    return;
                }

                _ = OpenFileWithLoadingAsync(_activePane, path);
            }
        }

        internal void OpenInRightPane_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetPath(sender, out var path))
            {
                OpenInOtherPane(RightPane, path);
            }
        }

        internal void OpenInLeftPane_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetPath(sender, out var path))
            {
                OpenInOtherPane(LeftPane, path);
            }
        }

        internal async void CopyToRightPane_Click(object sender, RoutedEventArgs e)
        {
            await CopyOrMoveToPaneAsync(sender, LeftPane, RightPane, move: false);
        }

        internal async void MoveToRightPane_Click(object sender, RoutedEventArgs e)
        {
            await CopyOrMoveToPaneAsync(sender, LeftPane, RightPane, move: true);
        }

        internal async void CopyToLeftPane_Click(object sender, RoutedEventArgs e)
        {
            await CopyOrMoveToPaneAsync(sender, RightPane, LeftPane, move: false);
        }

        internal async void MoveToLeftPane_Click(object sender, RoutedEventArgs e)
        {
            await CopyOrMoveToPaneAsync(sender, RightPane, LeftPane, move: true);
        }

        internal async void RenamePath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPath(sender, out var path))
            {
                return;
            }

            await RenameSinglePathAsync(path, _activePane);
        }

        internal async void DeletePath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPath(sender, out var path))
            {
                return;
            }

            await DeletePathAsync(path, _activePane);
        }

        private async Task DeletePathAsync(string path, PaneViewModel pane)
        {
            if (IsSshPath(path))
            {
                await ShowMessageAsync("遠端 Linux", "遠端 Linux 路徑目前先支援瀏覽。");
                return;
            }

            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var confirmed = await ConfirmAsync("刪除項目", $"確定要刪除「{name}」嗎？");
            if (!confirmed)
            {
                return;
            }

            var backgroundWorkId = BeginBackgroundWork($"刪除 {name} 中");
            try
            {
                var deleted = await Task.Run(() => DeletePathCore(path));
                if (!deleted)
                {
                    return;
                }

                await EnqueueOnUiAsync(() => RefreshPaneAfterLocalChange(pane));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("刪除失敗", ex.Message);
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        internal async void CreateFolderLeft_Click(object sender, RoutedEventArgs e)
        {
            await CreateFolderAsync(LeftPane);
        }

        internal async void CreateFolderRight_Click(object sender, RoutedEventArgs e)
        {
            await CreateFolderAsync(RightPane);
        }

        internal void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPath(sender, out var path))
            {
                return;
            }

            var package = new DataPackage();
            package.SetText(path);
            Clipboard.SetContent(package);
        }

        private IReadOnlyList<ShellInjectedMenuItem> BuildInjectedShellMenuItems(PaneViewModel pane)
        {
            var targetPaneLabel = ReferenceEquals(pane, LeftPane) ? "右側" : "左側";

            return new[]
            {
                new ShellInjectedMenuItem("Nuone Tools", ShellInjectedMenuItemKind.SubmenuHeader),
                new ShellInjectedMenuItem($"在{targetPaneLabel}打開", ShellInjectedCommand.OpenInOtherPane),
                new ShellInjectedMenuItem($"複製到{targetPaneLabel}", ShellInjectedCommand.CopyToOtherPane),
                new ShellInjectedMenuItem($"搬移到{targetPaneLabel}", ShellInjectedCommand.MoveToOtherPane),
                new ShellInjectedMenuItem("新增自動化", ShellInjectedCommand.CreateAutomation),
                new ShellInjectedMenuItem("部署 Node.js 到 Docker", ShellInjectedCommand.DeployNodeDocker),
                new ShellInjectedMenuItem(string.Empty, ShellInjectedMenuItemKind.Separator),
                new ShellInjectedMenuItem("重新命名", ShellInjectedCommand.Rename),
                new ShellInjectedMenuItem("新增資料夾", ShellInjectedCommand.CreateFolder),
                new ShellInjectedMenuItem("複製路徑", ShellInjectedCommand.CopyPath),
            };
        }

        private void HandleInjectedShellMenuCommand(
            ShellInjectedCommand command,
            PaneViewModel pane,
            IReadOnlyList<string> selectedPaths,
            string primaryPath)
        {
            switch (command)
            {
                case ShellInjectedCommand.OpenInOtherPane:
                    {
                        var targetPane = ReferenceEquals(pane, LeftPane) ? RightPane : LeftPane;
                        OpenInOtherPane(targetPane, primaryPath);
                        break;
                    }
                case ShellInjectedCommand.CopyToOtherPane:
                    _ = ExecuteInjectedTransferAsync(pane, selectedPaths, move: false);
                    break;
                case ShellInjectedCommand.MoveToOtherPane:
                    _ = ExecuteInjectedTransferAsync(pane, selectedPaths, move: true);
                    break;
                case ShellInjectedCommand.Rename:
                    _ = TriggerRenameAsync();
                    break;
                case ShellInjectedCommand.CreateFolder:
                    _ = CreateFolderAsync(pane);
                    break;
                case ShellInjectedCommand.CreateAutomation:
                    _ = CreateAutomationFromPanePairAsync(pane, primaryPath);
                    break;
                case ShellInjectedCommand.DeployNodeDocker:
                    _ = DeploySelectedNodePackageToDockerAsync(pane, selectedPaths);
                    break;
                case ShellInjectedCommand.CopyPath:
                    {
                        var package = new DataPackage();
                        package.SetText(string.Join(Environment.NewLine, selectedPaths));
                        Clipboard.SetContent(package);
                        break;
                    }
            }
        }

        private async Task ExecuteInjectedTransferAsync(PaneViewModel sourcePane, IReadOnlyList<string> selectedPaths, bool move)
        {
            var targetPane = ReferenceEquals(sourcePane, LeftPane) ? RightPane : LeftPane;
            await CopyOrMovePathsAsync(selectedPaths, sourcePane, targetPane, move);
        }

        private async Task CreateAutomationFromPanePairAsync(PaneViewModel sourcePane, string selectedSourcePath)
        {
            var destinationPane = ReferenceEquals(sourcePane, LeftPane) ? RightPane : LeftPane;
            var sourcePath = selectedSourcePath?.Trim();
            var destinationPath = destinationPane.CurrentPath?.Trim();

            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            {
                await ShowMessageAsync("新增自動化失敗", "來源與目的地路徑都必須存在。");
                return;
            }

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                await ShowMessageAsync("新增自動化失敗", $"來源不存在：{sourcePath}");
                return;
            }

            if (!Directory.Exists(destinationPath))
            {
                await ShowMessageAsync("新增自動化失敗", $"目的地資料夾不存在：{destinationPath}");
                return;
            }

            var normalizedSource = NormalizePath(sourcePath);
            var normalizedDestination = NormalizePath(destinationPath);
            var defaultName = $"{GetDisplayName(normalizedSource)} 備份";
            var finalName = defaultName;
            var suffix = 2;

            while (BackupAutomations.Any(profile =>
                       string.Equals(profile.Name, finalName, StringComparison.OrdinalIgnoreCase)))
            {
                finalName = $"{defaultName} {suffix}";
                suffix++;
            }

            var profile = new BackupAutomationProfile
            {
                Name = finalName,
                SourcePath = normalizedSource,
                DestinationPath = normalizedDestination,
                Mode = BackupAutomationMode.Copy,
                IntervalMinutes = 60,
                IsEnabled = true,
                LastRunText = "尚未執行",
                LastResultText = "等待排程",
            };

            profile.SyncIntervalText();
            BackupAutomations.Add(profile);
            SaveAutomationProfilesSafe();
            ActivateAutomation(profile);
            SwitchToAppSection(AppSection.Automation);
        }

        private void SwapPanes_Click(object sender, RoutedEventArgs e)
        {
            var leftPath = LeftPane.CurrentPath;
            var rightPath = RightPane.CurrentPath;

            LeftPane.NavigateTo(rightPath);
            RightPane.NavigateTo(leftPath);
        }

        private async Task CopyOrMoveToPaneAsync(object sender, PaneViewModel sourcePane, PaneViewModel targetPane, bool move)
        {
            ActivatePane(sourcePane);

            if (!TryGetPath(sender, out var path))
            {
                return;
            }

            await CopyOrMovePathsAsync(new[] { path }, sourcePane, targetPane, move);
        }

        private async Task CopyOrMovePathsAsync(IEnumerable<string> paths, PaneViewModel sourcePane, PaneViewModel targetPane, bool move)
        {
            var sourcePaths = paths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => Directory.Exists(path) || File.Exists(path))
                .ToList();

            if (paths.Any(IsSshPath) || IsSshPath(sourcePane.CurrentPath) || IsSshPath(targetPane.CurrentPath))
            {
                await ShowMessageAsync("遠端 Linux", "遠端 Linux 路徑目前先支援瀏覽。");
                return;
            }

            if (sourcePaths.Count == 0)
            {
                return;
            }

            var targetDirectory = targetPane.CurrentPath?.Trim();
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                await ShowMessageAsync(move ? "搬移失敗" : "複製失敗", "目的地資料夾不存在。");
                return;
            }

            var actionLabel = move ? "搬移" : "複製";
            var backgroundLabel = BuildTransferBackgroundLabel(sourcePaths, actionLabel);
            var backgroundDetails = BuildTransferBackgroundDetails(sourcePaths, targetDirectory, actionLabel);
            var completionLabel = $"完成：{BuildTransferBackgroundLabel(sourcePaths, actionLabel, includeInProgressSuffix: false)}";
            var backgroundWorkId = BeginBackgroundWork(backgroundLabel, backgroundDetails);

            try
            {
                await ExecuteNativeTransferAsync(sourcePaths, targetDirectory, move);

                await EnqueueOnUiAsync(() =>
                {
                    RefreshPaneAfterLocalChange(sourcePane);
                    RefreshPaneAfterLocalChange(targetPane);

                    if (sourcePaths.Count == 1)
                    {
                        var destinationPath = Path.Combine(
                            targetDirectory,
                            Path.GetFileName(sourcePaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                        targetPane.SelectedItem = targetPane.Items.FirstOrDefault(item => PathEquals(item.FullPath, destinationPath));
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(move ? "搬移失敗" : "複製失敗", ex.Message);
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId, completionLabel, backgroundDetails);
            }
        }

        private static string BuildTransferBackgroundLabel(
            IReadOnlyList<string> sourcePaths,
            string actionLabel,
            bool includeInProgressSuffix = true)
        {
            var countText = sourcePaths.Count == 1
                ? Path.GetFileName(sourcePaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : $"{sourcePaths.Count} 個項目";
            return includeInProgressSuffix
                ? $"{actionLabel} {countText} 中"
                : $"{actionLabel} {countText}";
        }

        private static string BuildTransferBackgroundDetails(
            IReadOnlyList<string> sourcePaths,
            string targetDirectory,
            string actionLabel)
        {
            var builder = new StringBuilder();
            builder.Append("動作：");
            builder.AppendLine(actionLabel);
            builder.AppendLine();
            builder.Append("目的地：");
            builder.AppendLine(targetDirectory);

            builder.AppendLine();
            builder.AppendLine("項目：");

            foreach (var sourcePath in sourcePaths)
            {
                var trimmedSourcePath = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var itemName = Path.GetFileName(trimmedSourcePath);
                var destinationPath = Path.Combine(targetDirectory, itemName);

                builder.Append("- ");
                builder.AppendLine(itemName);
                builder.Append("  從：");
                builder.AppendLine(trimmedSourcePath);
                builder.Append("  到：");
                builder.AppendLine(destinationPath);
            }

            return builder.ToString().TrimEnd();
        }

        private static bool TryGetPath(object sender, out string path)
        {
            if (sender is MenuFlyoutItem { Tag: string itemPath } && !string.IsNullOrWhiteSpace(itemPath))
            {
                path = itemPath;
                return true;
            }

            path = string.Empty;
            return false;
        }

        private static bool TryGetPathGroup(object sender, out PathGroup group)
        {
            if (sender is MenuFlyoutItem { Tag: PathGroup taggedGroup })
            {
                group = taggedGroup;
                return true;
            }

            if (sender is FrameworkElement { DataContext: PathGroup dataContextGroup })
            {
                group = dataContextGroup;
                return true;
            }

            group = null!;
            return false;
        }

        private static bool TryGetGroupedPathItem(object sender, out GroupedPathItem item)
        {
            if (sender is MenuFlyoutItem { Tag: GroupedPathItem taggedItem })
            {
                item = taggedItem;
                return true;
            }

            if (sender is FrameworkElement { DataContext: GroupedPathItem dataContextItem })
            {
                item = dataContextItem;
                return true;
            }

            item = null!;
            return false;
        }

        private static bool TryGetBackupAutomationProfile(object sender, out BackupAutomationProfile profile)
        {
            if (sender is FrameworkElement { Tag: BackupAutomationProfile taggedProfile })
            {
                profile = taggedProfile;
                return true;
            }

            if (sender is FrameworkElement { DataContext: BackupAutomationProfile dataContextProfile })
            {
                profile = dataContextProfile;
                return true;
            }

            profile = null!;
            return false;
        }

        private void OpenInOtherPane(PaneViewModel pane, string path)
        {
            ActivatePane(pane);

            if (IsSshPath(path))
            {
                OpenInPane(pane, path);
                return;
            }

            if (Directory.Exists(path))
            {
                OpenInPane(pane, path);
                return;
            }

            if (!File.Exists(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                return;
            }

            OpenInPane(pane, parent);
            pane.SelectedItem = pane.Items.FirstOrDefault(item => PathEquals(item.FullPath, path));
        }

        private void OpenPath(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return;
            }

            if (IsPowerShellScript(path))
            {
                OpenPowerShellScriptInTerminal(path);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }

        private static bool IsPowerShellScript(string path)
        {
            return File.Exists(path) &&
                string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase);
        }

        private void OpenPowerShellScriptInTerminal(string scriptPath)
        {
            var fullScriptPath = Path.GetFullPath(scriptPath);
            var workingDirectory = Path.GetDirectoryName(fullScriptPath);
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            var escapedScriptPath = fullScriptPath.Replace("\"", "\"\"", StringComparison.Ordinal);
            var command = $"powershell.exe -NoLogo -NoExit -ExecutionPolicy Bypass -File \"{escapedScriptPath}\"";
            OpenBuiltInTerminalTabAndRunCommand(TerminalShellKind.PowerShell, workingDirectory, command);
        }

        private async Task OpenFileWithLoadingAsync(PaneViewModel pane, string path)
        {
            var fileName = IsSshPath(path)
                ? (TryParseSshPath(path, out _, out var remotePath)
                    ? Path.GetFileName(remotePath.TrimEnd('/'))
                    : string.Empty)
                : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "檔案";
            }

            var overlayVersion = 0;
            await EnqueueOnUiAsync(() =>
            {
                overlayVersion = pane.BeginOverlay($"開啟 {fileName} 中...");
            });

            try
            {
                if (IsSshPath(path))
                {
                    await OpenRemoteFileWithLocalAppAsync(path);
                }
                else if (IsPowerShellScript(path))
                {
                    await EnqueueOnUiAsync(() => OpenPath(path));
                }
                else
                {
                    await Task.Run(() => OpenPath(path));
                }
            }
            finally
            {
                await EnqueueOnUiAsync(() => pane.EndOverlay(overlayVersion));
            }
        }

        private async Task OpenSelectedEntriesAsync()
        {
            var selectedEntries = GetSelectedEntriesInDisplayOrder(_activePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            foreach (var entry in selectedEntries)
            {
                if (entry.IsDirectory)
                {
                    OpenInPane(_activePane, entry.FullPath);
                    continue;
                }

                await OpenFileWithLoadingAsync(_activePane, entry.FullPath);
            }
        }

        private async Task OpenRemoteFileWithLocalAppAsync(string sshPath)
        {
            if (!TryParseSshPath(sshPath, out var connection, out var remotePath))
            {
                await ShowMessageAsync("遠端 Linux", "SSH 路徑格式不正確。");
                return;
            }

            var remoteFileName = Path.GetFileName(remotePath.TrimEnd('/'));
            if (string.IsNullOrWhiteSpace(remoteFileName))
            {
                await ShowMessageAsync("遠端 Linux", "這個路徑不是可開啟的遠端檔案。");
                return;
            }

            var backgroundWorkId = BeginBackgroundWork($"下載並開啟 {remoteFileName} 中");

            try
            {
                var localPath = await DownloadRemoteFileToTempAsync(connection, remotePath, remoteFileName);
                OpenPath(localPath);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("開啟遠端檔案失敗", ex.Message);
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        private static async Task<string> DownloadRemoteFileToTempAsync(
            string connection,
            string remotePath,
            string remoteFileName)
        {
            var safeConnection = string.Concat(connection.Select(static character =>
                char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                    ? character
                    : '_'));
            var tempDirectory = Path.Combine(Path.GetTempPath(), "nuone-tools", "remote-open", safeConnection);
            Directory.CreateDirectory(tempDirectory);

            var extension = Path.GetExtension(remoteFileName);
            var baseName = string.IsNullOrWhiteSpace(extension)
                ? remoteFileName
                : Path.GetFileNameWithoutExtension(remoteFileName);
            var localPath = Path.Combine(
                tempDirectory,
                $"{baseName}-{DateTime.Now:yyyyMMddHHmmss}{extension}");

            var remoteCommand = $"sh -lc {QuoteBashSingle($"cat -- {QuoteBashSingle(remotePath)}")}";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ssh.exe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("-T");
            process.StartInfo.ArgumentList.Add("-o");
            process.StartInfo.ArgumentList.Add("BatchMode=yes");
            process.StartInfo.ArgumentList.Add(connection);
            process.StartInfo.ArgumentList.Add(remoteCommand);

            if (!process.Start())
            {
                throw new InvalidOperationException("無法啟動 ssh.exe。");
            }

            await using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                var copyTask = process.StandardOutput.BaseStream.CopyToAsync(fileStream);
                var stdErrorTask = process.StandardError.ReadToEndAsync();
                await copyTask;
                var stdError = (await stdErrorTask).Trim();

                if (!process.WaitForExit(8000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    throw new InvalidOperationException("下載遠端檔案逾時。");
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdError)
                        ? $"SSH 命令失敗（ExitCode {process.ExitCode}）。"
                        : stdError);
                }
            }

            return localPath;
        }

        private async Task CreateFolderAsync(PaneViewModel pane)
        {
            ActivatePane(pane);

             if (IsSshPath(pane.CurrentPath))
            {
                await ShowMessageAsync("遠端 Linux", "遠端 Linux 路徑目前先支援瀏覽。");
                return;
            }

            var folderName = await PromptForTextAsync("新增資料夾", "輸入資料夾名稱", "New Folder");
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            try
            {
                var targetPath = EnsureUniquePath(Path.Combine(pane.CurrentPath, folderName));
                Directory.CreateDirectory(targetPath);
                RefreshPaneAfterLocalChange(pane);
                pane.SelectedItem = pane.Items.FirstOrDefault(item => PathEquals(item.FullPath, targetPath));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("新增資料夾失敗", ex.Message);
            }
        }

        private static void EnsureDestinationDoesNotExist(string path)
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                throw new IOException($"目的地已存在: {path}");
            }
        }

        private async Task TriggerRenameAsync()
        {
            var pane = _activePane;
            var selectedEntries = GetSelectedEntriesInDisplayOrder(pane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            if (selectedEntries.Count == 1)
            {
                await RenameSinglePathAsync(selectedEntries[0].FullPath, pane);
                return;
            }

            await BatchRenameSelectedAsync(pane, selectedEntries);
        }

        private async Task RenameSinglePathAsync(string path, PaneViewModel pane)
        {
            if (IsSshPath(path) || IsSshPath(pane.CurrentPath))
            {
                await ShowMessageAsync("遠端 Linux", "遠端 Linux 路徑目前先支援瀏覽。");
                return;
            }

            var currentName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var isFile = File.Exists(path);
            var newName = await ShowRenameSinglePathDialogAsync(currentName, isFile);
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, currentName, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var targetPath = Path.Combine(Path.GetDirectoryName(path) ?? pane.CurrentPath, newName);
                EnsureDestinationDoesNotExist(targetPath);

                if (Directory.Exists(path))
                {
                    Directory.Move(path, targetPath);
                }
                else if (File.Exists(path))
                {
                    File.Move(path, targetPath);
                }
                else
                {
                    return;
                }

                RefreshPaneAfterLocalChange(pane);
                pane.SelectedItem = pane.Items.FirstOrDefault(item => PathEquals(item.FullPath, targetPath));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("重新命名失敗", ex.Message);
            }
        }

        private async Task<string?> ShowRenameSinglePathDialogAsync(string currentName, bool isFile)
        {
            var extension = isFile ? Path.GetExtension(currentName) : string.Empty;
            var baseName = isFile ? Path.GetFileNameWithoutExtension(currentName) : currentName;
            var includeExtensionCheckBox = new CheckBox
            {
                Content = "包含副檔名",
                IsChecked = false,
                Visibility = isFile && !string.IsNullOrEmpty(extension) ? Visibility.Visible : Visibility.Collapsed,
            };
            var textBox = new TextBox
            {
                Text = currentName,
                SelectionStart = 0,
                SelectionLength = currentName.Length,
            };

            void ApplySelectionMode()
            {
                if (!isFile || string.IsNullOrEmpty(extension))
                {
                    textBox.Text = currentName;
                    textBox.SelectionStart = 0;
                    textBox.SelectionLength = currentName.Length;
                    return;
                }

                var includeExtension = includeExtensionCheckBox.IsChecked == true;
                var desiredText = includeExtension ? currentName : baseName;
                if (!string.Equals(textBox.Text, desiredText, StringComparison.Ordinal))
                {
                    textBox.Text = desiredText;
                }

                textBox.SelectionStart = 0;
                textBox.SelectionLength = textBox.Text.Length;
            }

            includeExtensionCheckBox.Checked += (_, _) => ApplySelectionMode();
            includeExtensionCheckBox.Unchecked += (_, _) => ApplySelectionMode();

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = "輸入新的名稱" });
            panel.Children.Add(textBox);
            if (includeExtensionCheckBox.Visibility == Visibility.Visible)
            {
                panel.Children.Add(includeExtensionCheckBox);
            }

            ApplySelectionMode();

            var dialog = new ContentDialog
            {
                Title = "重新命名",
                Content = panel,
                PrimaryButtonText = "確定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var value = textBox.Text.Trim();
            if (!isFile || string.IsNullOrEmpty(extension) || includeExtensionCheckBox.IsChecked == true)
            {
                return value;
            }

            return $"{value}{extension}";
        }

        private async Task BatchRenameSelectedAsync(PaneViewModel pane, IReadOnlyList<FileEntry> selectedEntries)
        {
            var previewItems = new ObservableCollection<BatchRenamePreviewItem>();
            var detectedStartNumber = DetectBatchRenameStartNumber(selectedEntries);
            var detectedDigits = DetectBatchRenameDigits(selectedEntries, detectedStartNumber);
            var formatTextBox = new TextBox
            {
                Text = "##",
                PlaceholderText = "例如 ##、Photo_##、{name}_{n}",
            };
            var startTextBox = new TextBox
            {
                Text = detectedStartNumber.ToString(CultureInfo.InvariantCulture),
                InputScope = new InputScope
                {
                    Names = { new InputScopeName(InputScopeNameValue.Number) },
                },
            };
            var digitsTextBox = new TextBox
            {
                Text = detectedDigits.ToString(CultureInfo.InvariantCulture),
                InputScope = new InputScope
                {
                    Names = { new InputScopeName(InputScopeNameValue.Number) },
                },
            };
            var extensionTextBox = new TextBox
            {
                PlaceholderText = "例如 mp4、jpg；留空表示不強制",
            };
            var keepExtensionCheckBox = new CheckBox
            {
                Content = "保留原副檔名",
                IsChecked = true,
            };
            var previewListView = CreateBatchRenamePreviewListView(previewItems);

            var dialog = new ContentDialog
            {
                Title = "批次重新命名",
                Content = CreateBatchRenameDialogContent(
                    selectedEntries,
                    formatTextBox,
                    startTextBox,
                    digitsTextBox,
                    extensionTextBox,
                    keepExtensionCheckBox,
                    previewListView),
                PrimaryButtonText = "執行",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 1200,
                MaxWidth = 1200,
                Width = 1200,
                MinHeight = 760,
                MaxHeight = 900,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);
            dialog.Resources["ContentDialogMinWidth"] = 120d;
            dialog.Resources["ContentDialogMaxWidth"] = 1200d;
            dialog.Resources["ContentDialogMaxHeight"] = 900d;

            void RefreshPreview()
            {
                UpdateBatchRenamePreview(
                    selectedEntries,
                    pane.CurrentPath,
                    formatTextBox.Text,
                    startTextBox.Text,
                    digitsTextBox.Text,
                    keepExtensionCheckBox.IsChecked == true,
                    extensionTextBox.Text,
                    previewItems);
                dialog.IsPrimaryButtonEnabled = previewItems.Count > 0 && previewItems.All(static item => item.CanApply);
            }

            formatTextBox.TextChanged += (_, _) => RefreshPreview();
            startTextBox.TextChanged += (_, _) => RefreshPreview();
            digitsTextBox.TextChanged += (_, _) => RefreshPreview();
            extensionTextBox.TextChanged += (_, _) => RefreshPreview();
            keepExtensionCheckBox.Checked += (_, _) => RefreshPreview();
            keepExtensionCheckBox.Unchecked += (_, _) => RefreshPreview();
            RefreshPreview();

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await ExecuteBatchRenameAsync(pane, previewItems);
        }

        private static Grid CreateBatchRenameDialogContent(
            IReadOnlyList<FileEntry> selectedEntries,
            TextBox formatTextBox,
            TextBox startTextBox,
            TextBox digitsTextBox,
            TextBox extensionTextBox,
            CheckBox keepExtensionCheckBox,
            ListView previewListView)
        {
            var root = new Grid
            {
                Width = 1500,
                Height = 680,
                ColumnSpacing = 18,
            };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPanel = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateBatchRenameSettingsPanel(
                    selectedEntries,
                    formatTextBox,
                    startTextBox,
                    digitsTextBox,
                    extensionTextBox,
                    keepExtensionCheckBox),
            };
            root.Children.Add(leftPanel);

            var rightPanel = new Grid
            {
                RowSpacing = 10,
            };
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(rightPanel, 1);
            root.Children.Add(rightPanel);

            var header = new Grid
            {
                Padding = new Thickness(8, 0, 8, 0),
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });

            header.Children.Add(CreatePreviewHeaderText("#", 0));
            header.Children.Add(CreatePreviewHeaderText("檔案", 1));
            header.Children.Add(CreatePreviewHeaderText("新檔名", 2));
            header.Children.Add(CreatePreviewHeaderText("狀態", 3));
            rightPanel.Children.Add(header);

            Grid.SetRow(previewListView, 1);
            rightPanel.Children.Add(previewListView);

            return root;
        }

        private static StackPanel CreateBatchRenameSettingsPanel(
            IReadOnlyList<FileEntry> selectedEntries,
            TextBox formatTextBox,
            TextBox startTextBox,
            TextBox digitsTextBox,
            TextBox extensionTextBox,
            CheckBox keepExtensionCheckBox)
        {
            var panel = new StackPanel { Spacing = 14 };
            panel.Children.Add(new TextBlock
            {
                Text = $"已選取 {selectedEntries.Count} 個項目",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            panel.Children.Add(new TextBlock
            {
                Text = "命名格式可使用 {name} 原名稱、{n} 編號，也可以使用 ## 表示補零編號。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
            });

            AddLabeledControl(panel, "檔案命名規則", formatTextBox);
            AddLabeledControl(panel, "起始編號", startTextBox);
            AddLabeledControl(panel, "編號位數", digitsTextBox);
            panel.Children.Add(keepExtensionCheckBox);
            AddLabeledControl(panel, "強制副檔名", extensionTextBox);

            panel.Children.Add(new TextBlock
            {
                Text = "範例：## -> 01.jpg；Photo_## -> Photo_01.jpg；{name}_{n} -> 原名稱_01.jpg。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 10, 0, 0),
            });

            return panel;
        }

        private static TextBlock CreatePreviewHeaderText(string text, int column)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Opacity = 0.76,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            Grid.SetColumn(textBlock, column);
            return textBlock;
        }

        private static void AddLabeledControl(Panel panel, string label, Control control)
        {
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Opacity = 0.82,
                Margin = new Thickness(0, 8, 0, -6),
            });
            panel.Children.Add(control);
        }

        private static ListView CreateBatchRenamePreviewListView(ObservableCollection<BatchRenamePreviewItem> previewItems)
        {
            var listView = new ListView
            {
                ItemsSource = previewItems,
                SelectionMode = ListViewSelectionMode.None,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 13, 20)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 58, 49, 70)),
                BorderThickness = new Thickness(1),
            };

            listView.ItemTemplate = (DataTemplate)XamlReader.Load(
                """
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Grid Padding="8,7">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="48" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="88" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{Binding Number}" Opacity="0.8" />
                        <TextBlock Grid.Column="1" Text="{Binding OriginalName}" TextTrimming="CharacterEllipsis" />
                        <TextBlock Grid.Column="2" Text="{Binding NewName}" TextTrimming="CharacterEllipsis" />
                        <TextBlock Grid.Column="3" Text="{Binding StatusText}" Foreground="{Binding StatusBrush}" TextTrimming="CharacterEllipsis" />
                    </Grid>
                </DataTemplate>
                """);

            return listView;
        }

        private static void UpdateBatchRenamePreview(
            IReadOnlyList<FileEntry> selectedEntries,
            string currentPath,
            string rawFormat,
            string rawStart,
            string rawDigits,
            bool keepExtension,
            string rawForcedExtension,
            ObservableCollection<BatchRenamePreviewItem> previewItems)
        {
            previewItems.Clear();

            var format = string.IsNullOrWhiteSpace(rawFormat) ? "##" : rawFormat.Trim();
            var isStartValid = int.TryParse(rawStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startNumber);
            var isDigitsValid = int.TryParse(rawDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits);
            digits = Math.Clamp(digits, 1, 12);

            var selectedPathSet = new HashSet<string>(
                selectedEntries.Select(entry => NormalizePath(entry.FullPath)),
                StringComparer.OrdinalIgnoreCase);
            var finalPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var invalidChars = Path.GetInvalidFileNameChars();

            for (var index = 0; index < selectedEntries.Count; index++)
            {
                var entry = selectedEntries[index];
                var itemNumber = index + 1;
                var newName = string.Empty;
                var status = "OK";
                var canApply = true;

                if (!isStartValid)
                {
                    status = "起始編號錯誤";
                    canApply = false;
                }
                else if (!isDigitsValid)
                {
                    status = "位數錯誤";
                    canApply = false;
                }
                else
                {
                    var sequence = (startNumber + index).ToString($"D{digits}", CultureInfo.InvariantCulture);
                    var originalBaseName = entry.IsDirectory ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
                    var extension = ResolveBatchRenameExtension(entry, keepExtension, rawForcedExtension);
                    var nameWithoutExtension = BuildBatchRenameName(format, originalBaseName, sequence);
                    newName = entry.IsDirectory ? nameWithoutExtension : $"{nameWithoutExtension}{extension}";

                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        status = "空名稱";
                        canApply = false;
                    }
                    else if (newName.IndexOfAny(invalidChars) >= 0)
                    {
                        status = "非法字元";
                        canApply = false;
                    }
                    else
                    {
                        var parentPath = Path.GetDirectoryName(entry.FullPath) ?? currentPath;
                        var finalPath = Path.Combine(parentPath, newName);
                        if (!finalPathSet.Add(finalPath))
                        {
                            status = "名稱重複";
                            canApply = false;
                        }
                        else if (!selectedPathSet.Contains(NormalizePath(finalPath)) && (Directory.Exists(finalPath) || File.Exists(finalPath)))
                        {
                            status = "已存在";
                            canApply = false;
                        }
                    }
                }

                previewItems.Add(new BatchRenamePreviewItem(entry, itemNumber, newName, status, canApply));
            }
        }

        private static string ResolveBatchRenameExtension(FileEntry entry, bool keepExtension, string rawForcedExtension)
        {
            if (entry.IsDirectory)
            {
                return string.Empty;
            }

            if (keepExtension)
            {
                return Path.GetExtension(entry.Name);
            }

            var extension = rawForcedExtension.Trim();
            if (string.IsNullOrWhiteSpace(extension))
            {
                return string.Empty;
            }

            return extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
        }

        private static string BuildBatchRenameName(string format, string originalBaseName, string sequence)
        {
            var result = format
                .Replace("{name}", originalBaseName, StringComparison.OrdinalIgnoreCase)
                .Replace("{n}", sequence, StringComparison.OrdinalIgnoreCase)
                .Replace("{index}", sequence, StringComparison.OrdinalIgnoreCase);

            result = ReplaceHashPlaceholders(result, sequence);
            if (!ContainsBatchRenameNumberToken(format))
            {
                result = $"{result}{sequence}";
            }

            return result.Trim();
        }

        private static bool ContainsBatchRenameNumberToken(string format)
        {
            return format.Contains("{n}", StringComparison.OrdinalIgnoreCase)
                || format.Contains("{index}", StringComparison.OrdinalIgnoreCase)
                || format.Contains('#', StringComparison.Ordinal);
        }

        private static string ReplaceHashPlaceholders(string value, string sequence)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != '#')
                {
                    builder.Append(value[index]);
                    continue;
                }

                var start = index;
                while (index < value.Length && value[index] == '#')
                {
                    index++;
                }

                var length = index - start;
                index--;
                builder.Append(sequence.PadLeft(length, '0'));
            }

            return builder.ToString();
        }

        private async Task ExecuteBatchRenameAsync(PaneViewModel pane, IReadOnlyList<BatchRenamePreviewItem> previewItems)
        {
            var renamePlan = previewItems
                .Select(item =>
                {
                    var parentPath = Path.GetDirectoryName(item.Entry.FullPath) ?? pane.CurrentPath;
                    return (item.Entry, TempPath: BuildTemporaryRenamePath(item.Entry.FullPath), FinalPath: Path.Combine(parentPath, item.NewName));
                })
                .ToList();

            try
            {
                foreach (var step in renamePlan)
                {
                    MovePath(step.Entry.FullPath, step.TempPath, step.Entry.IsDirectory);
                }

                foreach (var step in renamePlan)
                {
                    MovePath(step.TempPath, step.FinalPath, step.Entry.IsDirectory);
                }

                RefreshPaneAfterLocalChange(pane);
                RestorePaneSelection(pane, renamePlan.Select(step => step.FinalPath).ToList());
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("批次重新命名失敗", ex.Message);
            }
        }

        private IReadOnlyList<FileEntry> GetSelectedEntriesInDisplayOrder(PaneViewModel pane)
        {
            var selectedPathSet = new HashSet<string>(
                GetSelectedEntries(pane).Select(entry => NormalizePath(entry.FullPath)),
                StringComparer.OrdinalIgnoreCase);

            return pane.Items
                .Where(item => selectedPathSet.Contains(NormalizePath(item.FullPath)))
                .ToList();
        }

        private static string BuildBatchRenameDefaultBaseName(IReadOnlyList<FileEntry> selectedEntries)
        {
            if (selectedEntries.Count == 0)
            {
                return "New Name";
            }

            var firstName = selectedEntries[0].IsDirectory
                ? selectedEntries[0].Name
                : Path.GetFileNameWithoutExtension(selectedEntries[0].Name);

            return string.IsNullOrWhiteSpace(firstName) ? "New Name" : firstName;
        }

        private static int DetectBatchRenameStartNumber(IReadOnlyList<FileEntry> selectedEntries)
        {
            if (TryDetectCommonNumberPattern(selectedEntries, out var detectedStartNumber, out _))
            {
                return detectedStartNumber;
            }

            if (selectedEntries.Count == 0)
            {
                return 1;
            }

            var detectedNumbers = new List<int>();
            foreach (var entry in selectedEntries)
            {
                var baseName = entry.IsDirectory
                    ? entry.Name
                    : Path.GetFileNameWithoutExtension(entry.Name);

                if (!TryExtractTrailingNumber(baseName, out var number, out _))
                {
                    return 1;
                }

                detectedNumbers.Add(number);
            }

            if (detectedNumbers.Count == 0)
            {
                return 1;
            }

            for (var index = 1; index < detectedNumbers.Count; index++)
            {
                if (detectedNumbers[index] != detectedNumbers[index - 1] + 1)
                {
                    return detectedNumbers[0];
                }
            }

            return detectedNumbers[0];
        }

        private static int DetectBatchRenameDigits(IReadOnlyList<FileEntry> selectedEntries, int detectedStartNumber)
        {
            if (TryDetectCommonNumberPattern(selectedEntries, out _, out var detectedDigits))
            {
                return detectedDigits;
            }

            var defaultDigits = Math.Max(2, selectedEntries.Count.ToString(CultureInfo.InvariantCulture).Length);
            if (selectedEntries.Count == 0)
            {
                return defaultDigits;
            }

            var firstEntry = selectedEntries[0];
            var baseName = firstEntry.IsDirectory
                ? firstEntry.Name
                : Path.GetFileNameWithoutExtension(firstEntry.Name);

            if (TryExtractTrailingNumber(baseName, out var number, out var digitLength) &&
                number == detectedStartNumber)
            {
                return Math.Max(defaultDigits, digitLength);
            }

            return defaultDigits;
        }

        private static bool TryExtractTrailingNumber(string text, out int number, out int digitLength)
        {
            number = 0;
            digitLength = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var index = text.Length - 1;
            while (index >= 0 && char.IsDigit(text[index]))
            {
                index--;
            }

            var startIndex = index + 1;
            if (startIndex >= text.Length)
            {
                return false;
            }

            var numericText = text[startIndex..];
            if (!int.TryParse(numericText, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return false;
            }

            digitLength = numericText.Length;
            return true;
        }

        private static bool TryDetectCommonNumberPattern(
            IReadOnlyList<FileEntry> selectedEntries,
            out int startNumber,
            out int digits)
        {
            startNumber = 1;
            digits = 2;

            if (selectedEntries.Count == 0)
            {
                return false;
            }

            var baseNames = selectedEntries
                .Select(entry => entry.IsDirectory ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name))
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (baseNames.Count != selectedEntries.Count)
            {
                return false;
            }

            var prefixLength = GetCommonPrefixLength(baseNames);
            var suffixLength = GetCommonSuffixLength(baseNames, prefixLength);
            ExpandNumericSegmentBoundaries(baseNames, ref prefixLength, ref suffixLength);
            var numberSegments = new List<string>(baseNames.Count);

            foreach (var baseName in baseNames)
            {
                var segmentLength = baseName.Length - prefixLength - suffixLength;
                if (segmentLength <= 0)
                {
                    return false;
                }

                var segment = baseName.Substring(prefixLength, segmentLength);
                if (!int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    return false;
                }

                numberSegments.Add(segment);
            }

            if (!int.TryParse(numberSegments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out startNumber))
            {
                return false;
            }

            for (var index = 1; index < numberSegments.Count; index++)
            {
                if (!int.TryParse(numberSegments[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentNumber) ||
                    currentNumber != startNumber + index)
                {
                    return false;
                }
            }

            digits = Math.Max(2, numberSegments.Max(static segment => segment.Length));
            return true;
        }

        private static void ExpandNumericSegmentBoundaries(
            IReadOnlyList<string> values,
            ref int prefixLength,
            ref int suffixLength)
        {
            while (prefixLength > 0 && AreDigitsAt(values, prefixLength - 1) && AreDigitsAt(values, prefixLength))
            {
                prefixLength--;
            }

            while (suffixLength > 0)
            {
                var boundaryIndex = values[0].Length - suffixLength;
                if (!AreDigitsAt(values, boundaryIndex - 1) || !AreDigitsAt(values, boundaryIndex))
                {
                    break;
                }

                suffixLength--;
            }
        }

        private static bool AreDigitsAt(IReadOnlyList<string> values, int index)
        {
            foreach (var value in values)
            {
                if (index < 0 || index >= value.Length || !char.IsDigit(value[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int GetCommonPrefixLength(IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            var limit = values.Min(static value => value.Length);
            var index = 0;

            while (index < limit)
            {
                var current = values[0][index];
                for (var itemIndex = 1; itemIndex < values.Count; itemIndex++)
                {
                    if (values[itemIndex][index] != current)
                    {
                        return index;
                    }
                }

                index++;
            }

            return index;
        }

        private static int GetCommonSuffixLength(IReadOnlyList<string> values, int protectedPrefixLength)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            var limit = values.Min(value => value.Length - protectedPrefixLength);
            var suffixLength = 0;

            while (suffixLength < limit)
            {
                var current = values[0][^(suffixLength + 1)];
                for (var itemIndex = 1; itemIndex < values.Count; itemIndex++)
                {
                    if (values[itemIndex][^(suffixLength + 1)] != current)
                    {
                        return suffixLength;
                    }
                }

                suffixLength++;
            }

            return suffixLength;
        }

        private static string BuildTemporaryRenamePath(string originalPath)
        {
            var parentPath = Path.GetDirectoryName(originalPath) ?? string.Empty;
            var name = Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.Combine(parentPath, $".nuone-rename-{Guid.NewGuid():N}-{name}");
        }

        private static void MovePath(string sourcePath, string destinationPath, bool isDirectory)
        {
            if (isDirectory)
            {
                Directory.Move(sourcePath, destinationPath);
                return;
            }

            File.Move(sourcePath, destinationPath);
        }

        private void RestorePaneSelection(PaneViewModel pane, IReadOnlyList<string> selectedPaths)
        {
            var listView = ReferenceEquals(pane, LeftPane) ? LeftPaneListView : RightPaneListView;
            listView.SelectedItems.Clear();

            foreach (var entry in pane.Items.Where(item => selectedPaths.Any(path => PathEquals(path, item.FullPath))))
            {
                listView.SelectedItems.Add(entry);
            }

            SyncPaneSelectionFromListView(pane, listView);
            ApplySelectionVisuals(listView);
        }

        private void ApplySelectionVisuals(ListView listView)
        {
            foreach (var item in listView.Items)
            {
                if (listView.ContainerFromItem(item) is ListViewItem listViewItem)
                {
                    ApplySelectionVisualToContainer(listViewItem);
                }
            }
        }

        private void ApplySelectionVisualToContainer(ListViewItem listViewItem)
        {
            if (listViewItem.ContentTemplateRoot is not Border border)
            {
                return;
            }

            var isSelected = listViewItem.IsSelected;
            border.Background = isSelected ? SelectedItemBackgroundBrush : UnselectedItemBackgroundBrush;
            border.BorderBrush = isSelected ? SelectedItemBorderBrush : UnselectedItemBorderBrush;
            border.BorderThickness = isSelected ? SelectedItemBorderThickness : UnselectedItemBorderThickness;
        }

        private static string EnsureUniquePath(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return path;
            }

            var parent = Path.GetDirectoryName(path) ?? string.Empty;
            var extension = Path.GetExtension(path);
            var baseName = string.IsNullOrEmpty(extension)
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(path);

            for (var index = 2; index < 10_000; index++)
            {
                var candidateName = string.IsNullOrEmpty(extension)
                    ? $"{baseName} ({index})"
                    : $"{baseName} ({index}){extension}";
                var candidatePath = Path.Combine(parent, candidateName);
                if (!Directory.Exists(candidatePath) && !File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            throw new IOException("找不到可用的目的地名稱。");
        }

        private static Task ExecuteNativeTransferAsync(IReadOnlyList<string> sourcePaths, string targetDirectory, bool move)
        {
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    ExecuteNativeTransfer(sourcePaths, targetDirectory, move);
                    completion.SetResult(null);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }

        private static void ExecuteNativeTransfer(IReadOnlyList<string> sourcePaths, string targetDirectory, bool move)
        {
            if (sourcePaths.Count == 0)
            {
                return;
            }

            if (!Directory.Exists(targetDirectory))
            {
                throw new DirectoryNotFoundException($"目的地資料夾不存在：{targetDirectory}");
            }

            var existingSourcePaths = sourcePaths
                .Where(path => Directory.Exists(path) || File.Exists(path))
                .ToList();

            if (existingSourcePaths.Count == 0)
            {
                return;
            }

            var operation = new SHFILEOPSTRUCT
            {
                wFunc = move ? FO_MOVE : FO_COPY,
                pFrom = BuildShellMultiString(existingSourcePaths),
                pTo = BuildShellMultiString(new[] { targetDirectory }),
                fFlags = FOF_NOCONFIRMMKDIR,
            };

            var result = SHFileOperation(ref operation);
            if (operation.fAnyOperationsAborted)
            {
                throw new OperationCanceledException();
            }

            if (result != 0)
            {
                throw new Win32Exception(result);
            }
        }

        private static string BuildShellMultiString(IEnumerable<string> paths)
        {
            return string.Join("\0", paths) + "\0\0";
        }
    }
}
