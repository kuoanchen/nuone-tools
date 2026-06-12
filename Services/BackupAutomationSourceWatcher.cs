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
    public sealed class BackupAutomationSourceWatcher : IDisposable
    {
        private readonly BackupAutomationProfile _profile;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Action _triggerAction;
        private readonly DispatcherQueueTimer _debounceTimer;
        private FileSystemWatcher? _watcher;
        private string _watchRoot = string.Empty;
        private string? _fileFilter;
        private bool _isDisposed;

        public BackupAutomationSourceWatcher(
            BackupAutomationProfile profile,
            DispatcherQueue dispatcherQueue,
            Action triggerAction,
            TimeSpan debounceInterval)
        {
            _profile = profile;
            _dispatcherQueue = dispatcherQueue;
            _triggerAction = triggerAction;
            _debounceTimer = dispatcherQueue.CreateTimer();
            _debounceTimer.Interval = debounceInterval;
            _debounceTimer.IsRepeating = false;
            _debounceTimer.Tick += DebounceTimer_Tick;
        }

        public void Start()
        {
            if (_isDisposed)
            {
                return;
            }

            StopInternal();

            var sourcePath = _profile.SourcePath?.Trim();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            try
            {
                if (File.Exists(sourcePath))
                {
                    _watchRoot = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? string.Empty;
                    _fileFilter = Path.GetFileName(sourcePath);
                    if (string.IsNullOrWhiteSpace(_watchRoot) || string.IsNullOrWhiteSpace(_fileFilter))
                    {
                        return;
                    }

                    _watcher = new FileSystemWatcher(_watchRoot)
                    {
                        Filter = _fileFilter,
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName
                            | NotifyFilters.LastWrite
                            | NotifyFilters.CreationTime
                            | NotifyFilters.Size,
                        EnableRaisingEvents = true,
                    };
                }
                else if (Directory.Exists(sourcePath))
                {
                    _watchRoot = Path.GetFullPath(sourcePath);
                    _fileFilter = null;
                    _watcher = new FileSystemWatcher(_watchRoot)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName
                            | NotifyFilters.DirectoryName
                            | NotifyFilters.LastWrite
                            | NotifyFilters.CreationTime
                            | NotifyFilters.Size,
                        EnableRaisingEvents = true,
                    };
                }
                else
                {
                    _watchRoot = string.Empty;
                    _fileFilter = null;
                    return;
                }

                _watcher.Changed += Watcher_Changed;
                _watcher.Created += Watcher_Changed;
                _watcher.Deleted += Watcher_Changed;
                _watcher.Renamed += Watcher_Renamed;
                _watcher.Error += Watcher_Error;
            }
            catch
            {
                StopInternal();
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
            StopInternal();
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            ScheduleTrigger();
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            ScheduleTrigger();
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            _dispatcherQueue.TryEnqueue(Start);
        }

        private void ScheduleTrigger()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed)
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

            if (_isDisposed || !_profile.IsEnabled || _profile.Mode != BackupAutomationMode.Mirror)
            {
                return;
            }

            _triggerAction();
        }

        private void StopInternal()
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
            _watchRoot = string.Empty;
            _fileFilter = null;
        }
    }
}
