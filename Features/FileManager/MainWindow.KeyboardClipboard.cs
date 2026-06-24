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
        private List<string> _pendingDraggedPaths = new();
        private PaneViewModel? _pendingDragSourcePane;
        private FileEntry? _currentDropHighlightEntry;

        internal void LeftPaneList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            ActivatePane(LeftPane);
            RememberPendingDrag(LeftPane, e);
            PrepareExternalFileDrag(LeftPane, e);
        }

        internal void RightPaneList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            ActivatePane(RightPane);
            RememberPendingDrag(RightPane, e);
            PrepareExternalFileDrag(RightPane, e);
        }

        internal void LeftPaneEntry_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            ActivatePane(LeftPane);
            PrepareDirectEntryDrag(LeftPane, sender as FrameworkElement, args);
        }

        internal void RightPaneEntry_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            ActivatePane(RightPane);
            PrepareDirectEntryDrag(RightPane, sender as FrameworkElement, args);
        }

        internal void LeftPaneList_DragOver(object sender, DragEventArgs e)
        {
            SetCurrentDropHighlight(null);
            HandleDropTargetDragOver(LeftPane, LeftPane.CurrentPath, isDirectoryTarget: true, e);
        }

        internal async void LeftPaneList_Drop(object sender, DragEventArgs e)
        {
            await HandleDropTargetDropAsync(LeftPane, LeftPane.CurrentPath, isDirectoryTarget: true, e);
        }

        internal void RightPaneList_DragOver(object sender, DragEventArgs e)
        {
            SetCurrentDropHighlight(null);
            HandleDropTargetDragOver(RightPane, RightPane.CurrentPath, isDirectoryTarget: true, e);
        }

        internal async void RightPaneList_Drop(object sender, DragEventArgs e)
        {
            await HandleDropTargetDropAsync(RightPane, RightPane.CurrentPath, isDirectoryTarget: true, e);
        }

        internal void LeftPaneFolder_DragOver(object sender, DragEventArgs e)
        {
            var entry = (sender as FrameworkElement)?.DataContext as FileEntry;
            var dropTarget = ResolvePaneRowDropTarget(LeftPane, entry);
            SetCurrentDropHighlight(entry?.IsDirectory == true ? entry : null);
            HandleDropTargetDragOver(LeftPane, dropTarget.targetPath, dropTarget.isDirectoryTarget, e);
        }

        internal void LeftPaneFolder_DragLeave(object sender, DragEventArgs e)
        {
            SetCurrentDropHighlight(null);
        }

        internal async void LeftPaneFolder_Drop(object sender, DragEventArgs e)
        {
            var entry = (sender as FrameworkElement)?.DataContext as FileEntry;
            var dropTarget = ResolvePaneRowDropTarget(LeftPane, entry);
            await HandleDropTargetDropAsync(LeftPane, dropTarget.targetPath, dropTarget.isDirectoryTarget, e);
        }

        internal void RightPaneFolder_DragOver(object sender, DragEventArgs e)
        {
            var entry = (sender as FrameworkElement)?.DataContext as FileEntry;
            var dropTarget = ResolvePaneRowDropTarget(RightPane, entry);
            SetCurrentDropHighlight(entry?.IsDirectory == true ? entry : null);
            HandleDropTargetDragOver(RightPane, dropTarget.targetPath, dropTarget.isDirectoryTarget, e);
        }

        internal void RightPaneFolder_DragLeave(object sender, DragEventArgs e)
        {
            SetCurrentDropHighlight(null);
        }

        internal async void RightPaneFolder_Drop(object sender, DragEventArgs e)
        {
            var entry = (sender as FrameworkElement)?.DataContext as FileEntry;
            var dropTarget = ResolvePaneRowDropTarget(RightPane, entry);
            await HandleDropTargetDropAsync(RightPane, dropTarget.targetPath, dropTarget.isDirectoryTarget, e);
        }

        private void RememberPendingDrag(PaneViewModel pane, DragItemsStartingEventArgs e)
        {
            SetCurrentDropHighlight(null);
            _pendingDragSourcePane = pane;
            _pendingDraggedPaths = e.Items
                .OfType<FileEntry>()
                .Select(static entry => entry.FullPath?.Trim() ?? string.Empty)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            AppendDebugLog(
                "drag-drop-debug.log",
                $"drag-start pane={pane.Name} count={_pendingDraggedPaths.Count} paths={(_pendingDraggedPaths.Count == 0 ? "<empty>" : string.Join(" | ", _pendingDraggedPaths))}");
        }

        private void PrepareDirectEntryDrag(PaneViewModel pane, FrameworkElement? element, DragStartingEventArgs e)
        {
            SetCurrentDropHighlight(null);
            var entry = element?.DataContext as FileEntry;
            if (entry is null || string.IsNullOrWhiteSpace(entry.FullPath) || IsSshPath(entry.FullPath))
            {
                e.Cancel = true;
                AppendDebugLog("drag-drop-debug.log", $"drag-start-direct-cancel pane={pane.Name} reason=invalid-entry");
                return;
            }

            var draggedEntries = GetSelectedEntries(pane)
                .Where(selected => !string.IsNullOrWhiteSpace(selected.FullPath))
                .ToList();
            if (!draggedEntries.Any(selected => PathEquals(selected.FullPath, entry.FullPath)))
            {
                draggedEntries.Clear();
                draggedEntries.Add(entry);
            }

            _pendingDragSourcePane = pane;
            _pendingDraggedPaths = draggedEntries
                .Select(static selected => selected.FullPath.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            AppendDebugLog(
                "drag-drop-debug.log",
                $"drag-start-direct pane={pane.Name} count={_pendingDraggedPaths.Count} paths={string.Join(" | ", _pendingDraggedPaths)}");

            var storageItems = ResolveStorageItemsForTransfer(draggedEntries);
            if (storageItems.Count == 0)
            {
                e.Cancel = true;
                AppendDebugLog("drag-drop-debug.log", $"drag-start-direct-cancel pane={pane.Name} reason=no-storage-item path={entry.FullPath}");
                return;
            }

            e.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
            e.Data.SetStorageItems(storageItems);
            e.Data.SetText(string.Join(Environment.NewLine, _pendingDraggedPaths));
            AppendDebugLog(
                "drag-drop-debug.log",
                $"drag-start-direct-operation pane={pane.Name} requestedOperation={e.Data.RequestedOperation}");
        }

        private (string? targetPath, bool isDirectoryTarget) ResolvePaneRowDropTarget(PaneViewModel pane, FileEntry? entry)
        {
            if (entry?.IsDirectory == true && !string.IsNullOrWhiteSpace(entry.FullPath))
            {
                if (ShouldDropExpandedChildToPaneCurrentDirectory(pane, entry.FullPath))
                {
                    AppendDebugLog(
                        "drag-drop-debug.log",
                        $"resolve-row-drop-target pane={pane.Name} mode=expanded-child-to-current-path current={pane.CurrentPath ?? "<null>"} directoryEntry={entry.FullPath}");
                    return (pane.CurrentPath, true);
                }

                AppendDebugLog(
                    "drag-drop-debug.log",
                    $"resolve-row-drop-target pane={pane.Name} mode=directory-entry target={entry.FullPath}");
                return (entry.FullPath, true);
            }

            AppendDebugLog(
                "drag-drop-debug.log",
                $"resolve-row-drop-target pane={pane.Name} mode=pane-current-path target={pane.CurrentPath ?? "<null>"} row={entry?.FullPath ?? "<null>"}");
            return (pane.CurrentPath, true);
        }

        private bool ShouldDropExpandedChildToPaneCurrentDirectory(PaneViewModel pane, string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(pane.CurrentPath) || string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            var normalizedDirectoryPath = NormalizePath(directoryPath);
            if (PathEquals(pane.CurrentPath, normalizedDirectoryPath))
            {
                return false;
            }

            if (_pendingDraggedPaths.Count == 0)
            {
                return false;
            }

            var directoryPrefix = normalizedDirectoryPath + Path.DirectorySeparatorChar;
            return _pendingDraggedPaths.All(path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                var normalizedSourcePath = NormalizePath(Path.GetFullPath(path.Trim()));
                return normalizedSourcePath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void SetCurrentDropHighlight(FileEntry? entry)
        {
            if (ReferenceEquals(_currentDropHighlightEntry, entry))
            {
                return;
            }

            if (_currentDropHighlightEntry is not null)
            {
                _currentDropHighlightEntry.IsDropTarget = false;
            }

            _currentDropHighlightEntry = entry;

            if (_currentDropHighlightEntry is not null)
            {
                _currentDropHighlightEntry.IsDropTarget = true;
            }
        }

        private void HandleDropTargetDragOver(PaneViewModel pane, string? targetPath, bool isDirectoryTarget, DragEventArgs e)
        {
            e.Handled = true;

            var normalizedTarget = NormalizeLocalDropDirectory(targetPath, isDirectoryTarget);
            if (string.IsNullOrWhiteSpace(normalizedTarget))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = dataViewSupportsMoveOrCopy(e)
                ? DataPackageOperation.Move
                : DataPackageOperation.None;
            AppendDebugLog(
                "drag-drop-debug.log",
                $"drag-over pane={pane.Name} target={normalizedTarget} accepted={e.AcceptedOperation}");
        }

        private async Task HandleDropTargetDropAsync(PaneViewModel targetPane, string? targetPath, bool isDirectoryTarget, DragEventArgs e)
        {
            e.Handled = true;

            var targetDirectory = NormalizeLocalDropDirectory(targetPath, isDirectoryTarget);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                AppendDebugLog(
                    "drag-drop-debug.log",
                    $"drop-skip-invalid-target pane={targetPane.Name} targetPath={targetPath} isDirectoryTarget={isDirectoryTarget}");
                return;
            }

            try
            {
                var sourcePaths = await ResolveDraggedPathsAsync(e.DataView);
                if (sourcePaths.Count == 0)
                {
                    AppendDebugLog(
                        "drag-drop-debug.log",
                        $"drop-skip-no-source pane={targetPane.Name} target={targetDirectory}");
                    return;
                }

                var effectiveSourcePaths = FilterDropSourcePaths(sourcePaths, targetDirectory).ToList();
                if (effectiveSourcePaths.Count == 0)
                {
                    AppendDebugLog(
                        "drag-drop-debug.log",
                        $"drop-skip-no-effective-source pane={targetPane.Name} target={targetDirectory}");
                    return;
                }

                if (TryFindRecursiveDropConflict(effectiveSourcePaths, targetDirectory, out var conflictPath))
                {
                    AppendDebugLog(
                        "drag-drop-debug.log",
                        $"drop-recursive-conflict pane={targetPane.Name} target={targetDirectory} source={conflictPath}");
                    await ShowMessageAsync("搬移失敗", "不能把資料夾搬進自己的子資料夾。");
                    return;
                }

                var sourcePane = _pendingDragSourcePane ?? targetPane;
                AppendDebugLog(
                    "drag-drop-debug.log",
                    $"drop-start sourcePane={sourcePane.Name} targetPane={targetPane.Name} target={targetDirectory} count={effectiveSourcePaths.Count} paths={string.Join(" | ", effectiveSourcePaths)}");
                await CopyOrMovePathsToDirectoryAsync(effectiveSourcePaths, sourcePane, targetPane, targetDirectory, move: true);
                AppendDebugLog(
                    "drag-drop-debug.log",
                    $"drop-complete sourcePane={sourcePane.Name} targetPane={targetPane.Name} target={targetDirectory} count={effectiveSourcePaths.Count}");
            }
            catch (Exception ex)
            {
                AppendDebugLog("drag-drop-debug.log", $"drop-error target={targetDirectory} error={ex}");
                LogBoundaryException(ex, "file drop");
                await ShowMessageAsync("搬移失敗", ex.Message);
            }
            finally
            {
                SetCurrentDropHighlight(null);
                ClearPendingDrag();
            }
        }

        private async Task<List<string>> ResolveDraggedPathsAsync(DataPackageView dataView)
        {
            if (_pendingDraggedPaths.Count > 0)
            {
                return _pendingDraggedPaths.ToList();
            }

            var resolvedPaths = new List<string>();

            if (dataView.Contains(StandardDataFormats.StorageItems))
            {
                try
                {
                    var storageItems = await dataView.GetStorageItemsAsync();
                    resolvedPaths.AddRange(storageItems
                        .Select(static item => item.Path?.Trim() ?? string.Empty)
                        .Where(static path => !string.IsNullOrWhiteSpace(path)));
                }
                catch (Exception ex)
                {
                    AppendDebugLog("drag-drop-debug.log", $"resolve-storage-items-error error={ex}");
                }
            }

            if (resolvedPaths.Count == 0 && dataView.Contains(StandardDataFormats.Text))
            {
                try
                {
                    var text = await dataView.GetTextAsync();
                    resolvedPaths.AddRange(text
                        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
                catch (Exception ex)
                {
                    AppendDebugLog("drag-drop-debug.log", $"resolve-text-error error={ex}");
                }
            }

            return resolvedPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? NormalizeLocalDropDirectory(string? path, bool isDirectoryTarget)
        {
            if (!isDirectoryTarget || string.IsNullOrWhiteSpace(path) || IsSshPath(path))
            {
                return null;
            }

            var trimmedPath = path.Trim();
            return Directory.Exists(trimmedPath)
                ? Path.GetFullPath(trimmedPath)
                : null;
        }

        private static IEnumerable<string> FilterDropSourcePaths(IEnumerable<string> sourcePaths, string targetDirectory)
        {
            foreach (var sourcePath in sourcePaths
                         .Where(static path => !string.IsNullOrWhiteSpace(path))
                         .Select(path => path.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (IsSshPath(sourcePath))
                {
                    continue;
                }

                if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
                {
                    continue;
                }

                var normalizedSourcePath = Path.GetFullPath(sourcePath);
                var normalizedItemPath = normalizedSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedParentPath = Path.GetDirectoryName(normalizedItemPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;

                if (PathEquals(normalizedItemPath, targetDirectory) || PathEquals(normalizedParentPath, targetDirectory))
                {
                    continue;
                }

                yield return normalizedSourcePath;
            }
        }

        private static bool TryFindRecursiveDropConflict(IEnumerable<string> sourcePaths, string targetDirectory, out string conflictPath)
        {
            foreach (var sourcePath in sourcePaths)
            {
                if (!Directory.Exists(sourcePath))
                {
                    continue;
                }

                var normalizedSource = Path.GetFullPath(sourcePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedTarget = targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (normalizedTarget.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    conflictPath = normalizedSource;
                    return true;
                }
            }

            conflictPath = string.Empty;
            return false;
        }

        private void ClearPendingDrag()
        {
            SetCurrentDropHighlight(null);
            _pendingDraggedPaths.Clear();
            _pendingDragSourcePane = null;
        }

        private void RootLayout_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled || _isSettingsDialogOpen)
            {
                return;
            }

            if (_settingsCaptureTarget != ShortcutCaptureTarget.None)
            {
                e.Handled = true;

                if (e.Key is Windows.System.VirtualKey.Tab
                    or Windows.System.VirtualKey.LeftShift
                    or Windows.System.VirtualKey.RightShift
                    or Windows.System.VirtualKey.LeftControl
                    or Windows.System.VirtualKey.RightControl
                    or Windows.System.VirtualKey.LeftMenu
                    or Windows.System.VirtualKey.RightMenu)
                {
                    return;
                }

                var capturedKey = NormalizeCapturedKey(e.Key);
                switch (_settingsCaptureTarget)
                {
                    case ShortcutCaptureTarget.CopyToOtherPane:
                        _editingShortcutSettings.CopyToOtherPaneKey = capturedKey;
                        break;
                    case ShortcutCaptureTarget.MoveToOtherPane:
                        _editingShortcutSettings.MoveToOtherPaneKey = capturedKey;
                        break;
                    case ShortcutCaptureTarget.NavigateUp:
                        _editingShortcutSettings.NavigateUpKey = capturedKey;
                        break;
                    case ShortcutCaptureTarget.CreateFolder:
                        _editingShortcutSettings.CreateFolderKey = capturedKey;
                        break;
                    case ShortcutCaptureTarget.Delete:
                        _editingShortcutSettings.DeleteKey = capturedKey;
                        break;
                }

                _settingsCaptureTarget = ShortcutCaptureTarget.None;
                SyncShortcutText();
                if (HasDuplicateShortcut(_editingShortcutSettings))
                {
                    CaptureHintTextBlock.Text = "單鍵快捷鍵不能使用相同按鍵。";
                    _editingShortcutSettings = CloneShortcutSettings(_shortcutSettings);
                    SyncShortcutText();
                    return;
                }

                _shortcutSettings = CloneShortcutSettings(_editingShortcutSettings);
                SaveShortcutSettingsSafe();
                CaptureHintTextBlock.Text = "已立即儲存快捷鍵。";
                return;
            }

            if (_activeSection == AppSection.Terminal &&
                IsControlModifierPressed() &&
                e.Key == Windows.System.VirtualKey.C &&
                TryInterruptActiveTerminal())
            {
                e.Handled = true;
                return;
            }

            if (_activeSection != AppSection.FileManager)
            {
                return;
            }

            if (IsCreateFolderShortcut(_shortcutSettings.CreateFolderKey, e.Key) && !ShouldIgnorePaneFilterKeyInput())
            {
                e.Handled = true;
                RunFireAndForget(CreateFolderAsync(_activePane), "keyboard create folder");
                return;
            }

            if (IsControlModifierPressed() && e.Key == Windows.System.VirtualKey.C && !ShouldIgnorePaneFilterKeyInput())
            {
                e.Handled = true;
                RunFireAndForget(CopySelectionToClipboardAsync(_activePane, cut: false), "keyboard copy selection");
                return;
            }

            if (IsControlModifierPressed() && e.Key == Windows.System.VirtualKey.X && !ShouldIgnorePaneFilterKeyInput())
            {
                e.Handled = true;
                RunFireAndForget(CopySelectionToClipboardAsync(_activePane, cut: true), "keyboard cut selection");
                return;
            }

            if (IsControlModifierPressed() && e.Key == Windows.System.VirtualKey.V && !ShouldIgnorePaneFilterKeyInput())
            {
                e.Handled = true;
                RunFireAndForget(PasteClipboardItemsAsync(_activePane), "keyboard paste clipboard");
                return;
            }

            if (IsControlModifierPressed() && e.Key == Windows.System.VirtualKey.A && !ShouldIgnorePaneFilterKeyInput())
            {
                e.Handled = true;
                SelectAllInPane(_activePane);
                return;
            }

            if (e.Key == _shortcutSettings.CopyToOtherPaneKey)
            {
                e.Handled = true;
                RunFireAndForget(CopySelectedToOtherPaneAsync(), "keyboard copy to other pane");
                return;
            }

            if (e.Key == _shortcutSettings.MoveToOtherPaneKey)
            {
                e.Handled = true;
                RunFireAndForget(MoveSelectedToOtherPaneAsync(), "keyboard move to other pane");
                return;
            }

            if (e.Key == _shortcutSettings.NavigateUpKey || IsBreakAlias(_shortcutSettings.NavigateUpKey, e.Key))
            {
                e.Handled = true;
                NavigateUp(_activePane);
                return;
            }

            if (e.Key == _shortcutSettings.DeleteKey)
            {
                e.Handled = true;
                RunFireAndForget(DeleteSelectedAsync(), "keyboard delete selection");
                return;
            }

            if (e.Key == Windows.System.VirtualKey.F2)
            {
                e.Handled = true;
                RunFireAndForget(TriggerRenameAsync(), "keyboard rename selection");
                return;
            }

            if (IsControlModifierPressed() || IsAltModifierPressed())
            {
                return;
            }

            if (ShouldIgnorePaneFilterKeyInput())
            {
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                if (_activePane.HasActiveFilter)
                {
                    e.Handled = true;
                    _activePane.ClearFilter();
                    SyncPaneFilterSelection(_activePane);
                }

                return;
            }

            if (e.Key == Windows.System.VirtualKey.Back)
            {
                if (_activePane.HasActiveFilter)
                {
                    e.Handled = true;
                    _activePane.RemoveLastFilterCharacter();
                    SyncPaneFilterSelection(_activePane);
                }

                return;
            }

            if (TryMapSearchCharacter(e.Key, out var searchCharacter))
            {
                e.Handled = true;
                _activePane.AppendFilterCharacter(searchCharacter);
                SyncPaneFilterSelection(_activePane);
            }
        }

        private void SyncPaneFilterSelection(PaneViewModel pane)
        {
            var listView = ReferenceEquals(pane, LeftPane) ? LeftPaneListView : RightPaneListView;
            listView.SelectedItems.Clear();

            if (pane.SelectedItem is not null && pane.Items.Contains(pane.SelectedItem))
            {
                listView.SelectedItems.Add(pane.SelectedItem);
            }

            SyncPaneSelectionFromListView(pane, listView);
            ApplySelectionVisuals(listView);
        }

        private void SelectAllInPane(PaneViewModel pane)
        {
            var listView = ReferenceEquals(pane, LeftPane) ? LeftPaneListView : RightPaneListView;
            listView.SelectedItems.Clear();

            foreach (var entry in pane.Items)
            {
                listView.SelectedItems.Add(entry);
            }

            pane.SelectedItem = pane.Items.LastOrDefault();
            SyncPaneSelectionFromListView(pane, listView);
            ApplySelectionVisuals(listView);
            ScheduleSelectionSizeUpdate(pane, immediate: true);
        }

        private bool ShouldIgnorePaneFilterKeyInput()
        {
            var focusedElement = FocusManager.GetFocusedElement(RootLayout.XamlRoot);
            return focusedElement is TextBox
                or PasswordBox
                or RichEditBox
                or AutoSuggestBox
                or ComboBox;
        }

        private static bool IsCreateFolderShortcut(Windows.System.VirtualKey configuredKey, Windows.System.VirtualKey actualKey)
        {
            if (actualKey != configuredKey)
            {
                return false;
            }

            return IsVirtualKeyPressed(Windows.System.VirtualKey.LeftControl)
                && IsVirtualKeyPressed(Windows.System.VirtualKey.LeftShift)
                || IsVirtualKeyPressed(Windows.System.VirtualKey.LeftControl)
                && IsVirtualKeyPressed(Windows.System.VirtualKey.RightShift)
                || IsVirtualKeyPressed(Windows.System.VirtualKey.RightControl)
                && IsVirtualKeyPressed(Windows.System.VirtualKey.LeftShift)
                || IsVirtualKeyPressed(Windows.System.VirtualKey.RightControl)
                && IsVirtualKeyPressed(Windows.System.VirtualKey.RightShift);
        }

        private static bool IsVirtualKeyPressed(Windows.System.VirtualKey key)
        {
            return (GetKeyState((int)key) & 0x8000) != 0;
        }

        private static bool IsControlModifierPressed()
        {
            return IsVirtualKeyPressed(Windows.System.VirtualKey.LeftControl)
                || IsVirtualKeyPressed(Windows.System.VirtualKey.RightControl);
        }

        private static bool IsShiftModifierPressed()
        {
            return IsVirtualKeyPressed(Windows.System.VirtualKey.LeftShift)
                || IsVirtualKeyPressed(Windows.System.VirtualKey.RightShift);
        }

        private static bool IsAltModifierPressed()
        {
            return IsVirtualKeyPressed(Windows.System.VirtualKey.LeftMenu)
                || IsVirtualKeyPressed(Windows.System.VirtualKey.RightMenu);
        }

        private async Task CopySelectionToClipboardAsync(PaneViewModel pane, bool cut)
        {
            var selectedEntries = GetSelectedEntries(pane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            var storageItems = new List<IStorageItem>();
            foreach (var entry in selectedEntries)
            {
                try
                {
                    if (entry.IsDirectory)
                    {
                        storageItems.Add(await StorageFolder.GetFolderFromPathAsync(entry.FullPath));
                    }
                    else
                    {
                        storageItems.Add(await StorageFile.GetFileFromPathAsync(entry.FullPath));
                    }
                }
                catch
                {
                }
            }

            if (storageItems.Count == 0)
            {
                return;
            }

            var package = new DataPackage();
            package.RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy;
            package.SetStorageItems(storageItems);
            package.SetText(string.Join(Environment.NewLine, selectedEntries.Select(static entry => entry.FullPath)));
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }

        private void PrepareExternalFileDrag(PaneViewModel pane, DragItemsStartingEventArgs e)
        {
            var draggedEntries = e.Items.OfType<FileEntry>().ToList();
            if (draggedEntries.Count == 0)
            {
                draggedEntries = GetSelectedEntries(pane).ToList();
            }

            if (draggedEntries.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            var storageItems = ResolveStorageItemsForTransfer(draggedEntries);
            if (storageItems.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            e.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
            e.Data.SetStorageItems(storageItems);
            e.Data.SetText(string.Join(Environment.NewLine, draggedEntries.Select(static entry => entry.FullPath)));
            AppendDebugLog(
                "drag-drop-debug.log",
                $"drag-start-list-operation pane={pane.Name} requestedOperation={e.Data.RequestedOperation}");
        }

        private static bool dataViewSupportsMoveOrCopy(DragEventArgs e)
        {
            return e.DataView is not null;
        }

        private static List<IStorageItem> ResolveStorageItemsForTransfer(IEnumerable<FileEntry> entries)
        {
            var storageItems = new List<IStorageItem>();

            foreach (var entry in entries)
            {
                try
                {
                    if (entry.IsDirectory)
                    {
                        storageItems.Add(StorageFolder.GetFolderFromPathAsync(entry.FullPath).AsTask().GetAwaiter().GetResult());
                    }
                    else
                    {
                        storageItems.Add(StorageFile.GetFileFromPathAsync(entry.FullPath).AsTask().GetAwaiter().GetResult());
                    }
                }
                catch
                {
                }
            }

            return storageItems;
        }

        private async Task PasteClipboardItemsAsync(PaneViewModel targetPane)
        {
            var targetDirectory = targetPane.CurrentPath?.Trim();
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                return;
            }

            var packageView = Clipboard.GetContent();
            if (packageView.Contains(StandardDataFormats.StorageItems))
            {
                await PasteStorageItemsAsync(packageView, targetPane, targetDirectory);
                return;
            }

            if (packageView.Contains(StandardDataFormats.Bitmap))
            {
                await PasteBitmapAsync(packageView, targetPane, targetDirectory);
                return;
            }

            if (packageView.Contains(StandardDataFormats.Text))
            {
                await PasteTextAsync(packageView, targetPane, targetDirectory);
            }
        }

        private async Task PasteStorageItemsAsync(DataPackageView packageView, PaneViewModel targetPane, string targetDirectory)
        {
            var storageItems = await packageView.GetStorageItemsAsync();
            var sourcePaths = storageItems
                .Select(static item => item.Path)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sourcePaths.Count == 0)
            {
                return;
            }

            var move = packageView.RequestedOperation == DataPackageOperation.Move;
            var actionLabel = move ? "貼上搬移" : "貼上複製";
            var backgroundLabel = BuildTransferBackgroundLabel(sourcePaths, actionLabel);
            var backgroundDetails = BuildTransferBackgroundDetails(sourcePaths, targetDirectory, actionLabel);
            var completionLabel = $"完成：{BuildTransferBackgroundLabel(sourcePaths, actionLabel, includeInProgressSuffix: false)}";
            var backgroundWorkId = BeginBackgroundWork(backgroundLabel, backgroundDetails);

            try
            {
                await ExecuteNativeTransferAsync(sourcePaths, targetDirectory, move);

                await EnqueueOnUiAsync(() =>
                {
                    RefreshPane(LeftPane);
                    RefreshPane(RightPane);

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
                await ShowMessageAsync(move ? "貼上搬移失敗" : "貼上複製失敗", ex.Message);
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId, completionLabel, backgroundDetails);
            }
        }

        private async Task PasteBitmapAsync(DataPackageView packageView, PaneViewModel targetPane, string targetDirectory)
        {
            var bitmapReference = await packageView.GetBitmapAsync();
            if (bitmapReference is null)
            {
                return;
            }

            var targetPath = EnsureUniquePath(Path.Combine(
                targetDirectory,
                $"clipboard-image-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
            var backgroundWorkId = BeginBackgroundWork("貼上圖片中");

            try
            {
                using var sourceStream = await bitmapReference.OpenReadAsync();
                using var managedSource = sourceStream.AsStreamForRead();
                using var targetStream = File.Create(targetPath);
                await managedSource.CopyToAsync(targetStream);

                await EnqueueOnUiAsync(() =>
                {
                    RefreshPaneAfterLocalChange(targetPane);
                    targetPane.SelectedItem = targetPane.Items.FirstOrDefault(item => PathEquals(item.FullPath, targetPath));
                });
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("貼上圖片失敗", ex.Message);
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        private async Task PasteTextAsync(DataPackageView packageView, PaneViewModel targetPane, string targetDirectory)
        {
            var text = await packageView.GetTextAsync();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var targetPath = EnsureUniquePath(Path.Combine(
                targetDirectory,
                $"clipboard-text-{DateTime.Now:yyyyMMdd-HHmmss}.txt"));
            var backgroundWorkId = BeginBackgroundWork("貼上文字中");

            try
            {
                await File.WriteAllTextAsync(targetPath, text, Encoding.UTF8);

                await EnqueueOnUiAsync(() =>
                {
                    RefreshPaneAfterLocalChange(targetPane);
                    targetPane.SelectedItem = targetPane.Items.FirstOrDefault(item => PathEquals(item.FullPath, targetPath));
                });
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("貼上文字失敗", ex.Message);
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        private static bool TryMapSearchCharacter(Windows.System.VirtualKey key, out char value)
        {
            value = default;

            if (key is >= Windows.System.VirtualKey.A and <= Windows.System.VirtualKey.Z)
            {
                value = char.ToLowerInvariant((char)('A' + (key - Windows.System.VirtualKey.A)));
                return true;
            }

            if (key is >= Windows.System.VirtualKey.Number0 and <= Windows.System.VirtualKey.Number9)
            {
                value = (char)('0' + (key - Windows.System.VirtualKey.Number0));
                return true;
            }

            if (key is >= Windows.System.VirtualKey.NumberPad0 and <= Windows.System.VirtualKey.NumberPad9)
            {
                value = (char)('0' + (key - Windows.System.VirtualKey.NumberPad0));
                return true;
            }

            value = key switch
            {
                Windows.System.VirtualKey.Space => ' ',
                Windows.System.VirtualKey.Divide => '/',
                Windows.System.VirtualKey.Subtract => '-',
                Windows.System.VirtualKey.Decimal => '.',
                (Windows.System.VirtualKey)189 => '-',
                (Windows.System.VirtualKey)190 => '.',
                _ => default,
            };

            return value != default;
        }

        private async Task CopySelectedToOtherPaneAsync()
        {
            var sourcePane = _activePane;
            var targetPane = ReferenceEquals(sourcePane, LeftPane) ? RightPane : LeftPane;
            var selectedEntries = GetSelectedEntries(sourcePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            await CopyOrMovePathsAsync(selectedEntries.Select(static entry => entry.FullPath), sourcePane, targetPane, move: false);
        }

        private async Task MoveSelectedToOtherPaneAsync()
        {
            var sourcePane = _activePane;
            var targetPane = ReferenceEquals(sourcePane, LeftPane) ? RightPane : LeftPane;
            var selectedEntries = GetSelectedEntries(sourcePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            await CopyOrMovePathsAsync(selectedEntries.Select(static entry => entry.FullPath).ToList(), sourcePane, targetPane, move: true);
        }

        private async Task DeleteSelectedAsync()
        {
            var selectedEntries = GetSelectedEntries(_activePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            if (selectedEntries.Count == 1)
            {
                await DeletePathAsync(selectedEntries[0].FullPath, _activePane);
                return;
            }

            var confirmed = await ConfirmAsync("刪除項目", $"確定要刪除這 {selectedEntries.Count} 個項目嗎？");
            if (!confirmed)
            {
                return;
            }

            var backgroundWorkId = BeginBackgroundWork($"刪除 {selectedEntries.Count} 個項目中");
            try
            {
                foreach (var entry in selectedEntries.ToList())
                {
                    try
                    {
                        var deleted = await DeletePathCoreAsync(entry.FullPath);
                        if (!deleted)
                        {
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageAsync("刪除失敗", $"{entry.Name}\n{ex.Message}");
                        break;
                    }
                }

                await EnqueueOnUiAsync(() => RefreshPaneAfterLocalChange(_activePane));
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        private static bool DeletePathCore(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return true;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }

            return false;
        }
    }
}
