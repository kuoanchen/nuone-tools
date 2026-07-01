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
    public sealed class PaneDirectoryWatcher : IDisposable
    {
        private const string WatcherLogFileName = "file-operation-timing.log";
        private static readonly TimeSpan TransientFileChangedThrottleInterval = TimeSpan.FromSeconds(2);
        private readonly PaneViewModel _pane;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Action<PaneViewModel> _refreshAction;
        private readonly Action<PaneViewModel, IReadOnlyList<(string Kind, string Path, string? OldPath)>> _applyChangeAction;
        private readonly DispatcherQueueTimer _debounceTimer;
        private FileSystemWatcher? _watcher;
        private string _watchedPath = string.Empty;
        private DateTimeOffset _suppressRefreshUntil = DateTimeOffset.MinValue;
        private bool _isDisposed;
        private readonly List<(string Kind, string Path, string? OldPath)> _pendingChanges = new();
        private readonly Dictionary<string, DateTimeOffset> _lastTransientChangedAt = new(StringComparer.OrdinalIgnoreCase);

        public PaneDirectoryWatcher(
            PaneViewModel pane,
            DispatcherQueue dispatcherQueue,
            Action<PaneViewModel> refreshAction,
            Action<PaneViewModel, IReadOnlyList<(string Kind, string Path, string? OldPath)>> applyChangeAction,
            TimeSpan debounceInterval)
        {
            _pane = pane;
            _dispatcherQueue = dispatcherQueue;
            _refreshAction = refreshAction;
            _applyChangeAction = applyChangeAction;

            _debounceTimer = dispatcherQueue.CreateTimer();
            _debounceTimer.Interval = debounceInterval;
            _debounceTimer.IsRepeating = false;
            _debounceTimer.Tick += DebounceTimer_Tick;
        }

        public void Watch(string? path)
        {
            if (_isDisposed)
            {
                return;
            }

            if (MainWindow.IsWslPath(path) || MainWindow.IsSshPath(path))
            {
                AppendWatcherLog($"watch-skip pane={_pane.Name} path={path} reason=virtual-or-ssh");
                StopWatching();
                _watchedPath = string.Empty;
                return;
            }

            var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
            if (string.Equals(_watchedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AppendWatcherLog($"watch-change pane={_pane.Name} oldPath={_watchedPath} newPath={normalizedPath}");
            StopWatching();

            if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
            {
                _watchedPath = string.Empty;
                AppendWatcherLog($"watch-clear pane={_pane.Name} path={normalizedPath} reason=missing-or-empty");
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(normalizedPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.CreationTime
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += Watcher_Changed;
                _watcher.Created += Watcher_Changed;
                _watcher.Deleted += Watcher_Changed;
                _watcher.Renamed += Watcher_Renamed;
                _watcher.Error += Watcher_Error;
                _watchedPath = normalizedPath;
                AppendWatcherLog($"watch-start pane={_pane.Name} path={_watchedPath}");
            }
            catch
            {
                AppendWatcherLog($"watch-error pane={_pane.Name} path={normalizedPath} action=start");
                StopWatching();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _debounceTimer.Stop();
            _debounceTimer.Tick -= DebounceTimer_Tick;
            StopWatching();
        }

        public void SuppressRefreshFor(TimeSpan duration)
        {
            if (_isDisposed)
            {
                return;
            }

            var until = DateTimeOffset.UtcNow.Add(duration);
            if (until > _suppressRefreshUntil)
            {
                _suppressRefreshUntil = until;
            }

            AppendWatcherLog(
                $"suppress pane={_pane.Name} path={_watchedPath} durationMs={duration.TotalMilliseconds} untilUtc={_suppressRefreshUntil:O}");

            _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        _debounceTimer.Stop();
                    }
                    catch (Exception ex)
                    {
                        MainWindow.LogBoundaryException(ex, "pane watcher suppress refresh");
                    }
                });
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            ScheduleRefresh(e.ChangeType.ToString(), e.FullPath, null);
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            ScheduleRefresh("Renamed", e.FullPath, e.OldFullPath);
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            var currentPath = _watchedPath;
            AppendWatcherLog($"watcher-error pane={_pane.Name} path={currentPath} error={e.GetException()}");
            _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        Watch(currentPath);
                    }
                    catch (Exception ex)
                    {
                        MainWindow.LogBoundaryException(ex, "pane watcher error restart");
                    }
                });
        }

        private void ScheduleRefresh(string triggerKind, string triggerPath, string? oldPath)
        {
            _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (_isDisposed)
                        {
                            return;
                        }

                        if (DateTimeOffset.UtcNow < _suppressRefreshUntil)
                        {
                            AppendWatcherLog(
                                $"schedule-skip-suppressed pane={_pane.Name} watchedPath={_watchedPath} trigger={triggerKind} triggerPath={triggerPath} suppressUntilUtc={_suppressRefreshUntil:O}");
                            return;
                        }

                        if (ShouldThrottleChangedEvent(triggerKind, triggerPath, out var throttleRemaining))
                        {
                            AppendWatcherLog(
                                $"schedule-skip-throttled pane={_pane.Name} watchedPath={_watchedPath} trigger={triggerKind} triggerPath={triggerPath} remainingMs={throttleRemaining.TotalMilliseconds}");
                            return;
                        }

                        _pendingChanges.Add((triggerKind, triggerPath, oldPath));
                        _debounceTimer.Stop();
                        _debounceTimer.Start();
                        AppendWatcherLog(
                            $"schedule-start pane={_pane.Name} watchedPath={_watchedPath} trigger={triggerKind} triggerPath={triggerPath} oldPath={oldPath ?? "<null>"} pendingCount={_pendingChanges.Count} debounceMs={_debounceTimer.Interval.TotalMilliseconds}");
                    }
                    catch (Exception ex)
                    {
                        MainWindow.LogBoundaryException(ex, "pane watcher schedule refresh");
                    }
                });
        }

        private void DebounceTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            try
            {
                sender.Stop();
                var pendingChanges = _pendingChanges.ToList();
                _pendingChanges.Clear();
                AppendWatcherLog(
                    $"debounce-tick pane={_pane.Name} watchedPath={_watchedPath} currentPath={_pane.CurrentPath} pendingCount={pendingChanges.Count}");

                if (_isDisposed || string.IsNullOrWhiteSpace(_pane.CurrentPath))
                {
                    AppendWatcherLog($"debounce-skip pane={_pane.Name} reason=disposed-or-empty-current-path");
                    return;
                }

                if (!string.Equals(_watchedPath, Path.GetFullPath(_pane.CurrentPath), StringComparison.OrdinalIgnoreCase))
                {
                    AppendWatcherLog(
                        $"debounce-skip pane={_pane.Name} reason=path-changed watchedPath={_watchedPath} currentPath={_pane.CurrentPath}");
                    return;
                }

                if (DateTimeOffset.UtcNow < _suppressRefreshUntil)
                {
                    AppendWatcherLog(
                        $"debounce-skip pane={_pane.Name} reason=suppressed watchedPath={_watchedPath} suppressUntilUtc={_suppressRefreshUntil:O}");
                    return;
                }

                AppendWatcherLog(
                    $"debounce-apply-change pane={_pane.Name} currentPath={_pane.CurrentPath} pendingCount={pendingChanges.Count} changes={(pendingChanges.Count == 0 ? "<empty>" : string.Join(" || ", pendingChanges.Select(change => $"{change.Kind}:{change.Path}:{change.OldPath ?? "<null>"}")))}");
                _applyChangeAction(_pane, pendingChanges);
            }
            catch (Exception ex)
            {
                AppendWatcherLog(
                    $"debounce-fallback-refresh pane={_pane.Name} pendingCount={_pendingChanges.Count} error={ex}");
                _refreshAction(_pane);
                MainWindow.LogBoundaryException(ex, "pane watcher debounce tick");
            }
        }

        private void StopWatching()
        {
            _debounceTimer.Stop();
            _pendingChanges.Clear();
            _lastTransientChangedAt.Clear();

            if (_watcher is null)
            {
                return;
            }

            AppendWatcherLog($"watch-stop pane={_pane.Name} path={_watchedPath}");
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= Watcher_Changed;
            _watcher.Created -= Watcher_Changed;
            _watcher.Deleted -= Watcher_Changed;
            _watcher.Renamed -= Watcher_Renamed;
            _watcher.Error -= Watcher_Error;
            _watcher.Dispose();
            _watcher = null;
        }

        private static void AppendWatcherLog(string message)
        {
            MainWindow.AppendDebugLog(WatcherLogFileName, $"watcher {message}");
        }

        private bool ShouldThrottleChangedEvent(string triggerKind, string triggerPath, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (!string.Equals(triggerKind, "Changed", StringComparison.Ordinal) ||
                !IsTransientDownloadPath(triggerPath))
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (_lastTransientChangedAt.TryGetValue(triggerPath, out var lastAt))
            {
                var elapsed = now - lastAt;
                if (elapsed < TransientFileChangedThrottleInterval)
                {
                    remaining = TransientFileChangedThrottleInterval - elapsed;
                    return true;
                }
            }

            _lastTransientChangedAt[triggerPath] = now;
            return false;
        }

        private static bool IsTransientDownloadPath(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".part", StringComparison.OrdinalIgnoreCase);
        }
    }
}
