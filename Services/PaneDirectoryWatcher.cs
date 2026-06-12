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
        private readonly PaneViewModel _pane;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Action<PaneViewModel> _refreshAction;
        private readonly DispatcherQueueTimer _debounceTimer;
        private FileSystemWatcher? _watcher;
        private string _watchedPath = string.Empty;
        private DateTimeOffset _suppressRefreshUntil = DateTimeOffset.MinValue;
        private bool _isDisposed;

        public PaneDirectoryWatcher(
            PaneViewModel pane,
            DispatcherQueue dispatcherQueue,
            Action<PaneViewModel> refreshAction,
            TimeSpan debounceInterval)
        {
            _pane = pane;
            _dispatcherQueue = dispatcherQueue;
            _refreshAction = refreshAction;

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

            var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
            if (string.Equals(_watchedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            StopWatching();

            if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
            {
                _watchedPath = string.Empty;
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(normalizedPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.CreationTime
                        | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += Watcher_Changed;
                _watcher.Created += Watcher_Changed;
                _watcher.Deleted += Watcher_Changed;
                _watcher.Renamed += Watcher_Renamed;
                _watcher.Error += Watcher_Error;
                _watchedPath = normalizedPath;
            }
            catch
            {
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

            _dispatcherQueue.TryEnqueue(() => _debounceTimer.Stop());
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            ScheduleRefresh();
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            ScheduleRefresh();
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            var currentPath = _watchedPath;
            _dispatcherQueue.TryEnqueue(() => Watch(currentPath));
        }

        private void ScheduleRefresh()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed)
                {
                    return;
                }

                if (DateTimeOffset.UtcNow < _suppressRefreshUntil)
                {
                    return;
                }

                _debounceTimer.Stop();
                _debounceTimer.Start();
            });
        }

        private void DebounceTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();

            if (_isDisposed || string.IsNullOrWhiteSpace(_pane.CurrentPath))
            {
                return;
            }

            if (!string.Equals(_watchedPath, Path.GetFullPath(_pane.CurrentPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (DateTimeOffset.UtcNow < _suppressRefreshUntil)
            {
                return;
            }

            _refreshAction(_pane);
        }

        private void StopWatching()
        {
            _debounceTimer.Stop();

            if (_watcher is null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= Watcher_Changed;
            _watcher.Created -= Watcher_Changed;
            _watcher.Deleted -= Watcher_Changed;
            _watcher.Renamed -= Watcher_Renamed;
            _watcher.Error -= Watcher_Error;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
