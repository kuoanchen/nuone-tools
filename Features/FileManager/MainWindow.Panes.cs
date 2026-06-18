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
        private sealed class SshEntrySnapshot
        {
            public string Name { get; init; } = string.Empty;

            public string FullPath { get; init; } = string.Empty;

            public bool IsDirectory { get; init; }

            public string ModifiedText { get; init; } = string.Empty;

            public long? SizeBytes { get; init; }
        }

        private static readonly object SshDebugLogLock = new();

        private void Pane_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(PaneViewModel.CurrentPath), StringComparison.Ordinal))
            {
                if (ReferenceEquals(sender, LeftPane))
                {
                    _leftPaneWatcher.Watch(LeftPane.CurrentPath);
                }
                else if (ReferenceEquals(sender, RightPane))
                {
                    _rightPaneWatcher.Watch(RightPane.CurrentPath);
                }
            }

            if (string.Equals(e.PropertyName, nameof(PaneViewModel.CurrentPath), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.StatusText), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.SummaryText), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.SelectionText), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.SelectedCount), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(PaneViewModel.SelectedItem), StringComparison.Ordinal))
            {
                UpdateSharedStatusBar();
            }
        }

        private void TrySetWindowIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (File.Exists(iconPath))
                {
                    AppWindow.SetIcon(iconPath);
                }
            }
            catch
            {
            }
        }

        private void ApplyInitialWindowPlacement()
        {
            var savedPlacement = LoadSavedWindowPlacement();
            if (savedPlacement is not null &&
                savedPlacement.Width > 0 &&
                savedPlacement.Height > 0)
            {
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    savedPlacement.X,
                    savedPlacement.Y,
                    savedPlacement.Width,
                    savedPlacement.Height));
                return;
            }

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1680, 980));
        }

        private void ConfigureTitleBarInsets()
        {
            TopCommandBarBorder.Margin = new Thickness(0);
        }

        private void SeedSidebar()
        {
        }

        private void LoadDriveCards()
        {
            Drives.Clear();

            foreach (var drive in DriveInfo.GetDrives().Where(static d => d.IsReady))
            {
                var rootPath = NormalizeDriveRootPath(drive.RootDirectory.FullName);
                if (_hiddenDrivePaths.Contains(rootPath))
                {
                    continue;
                }

                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var used = Math.Max(total - free, 0);
                var usage = total == 0 ? 0 : Math.Round((double)used / total * 100, 1);
                var remainingPercent = total == 0 ? 0 : Math.Round((double)free / total * 100, 1);

                Drives.Add(new DriveShortcut
                {
                    Name = $"{drive.Name.TrimEnd(Path.DirectorySeparatorChar)}",
                    RootPath = rootPath,
                    Summary = $"{FormatDriveSizeInGb(free)}GB / {FormatDriveSizeInGb(total)}GB",
                    UsagePercent = usage,
                });
            }
        }

        private static string FormatDriveSizeInGb(long bytes)
        {
            var gib = bytes / 1024d / 1024d / 1024d;
            return Math.Round(gib, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string NormalizeDriveRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var root = Path.GetPathRoot(path.Trim()) ?? path.Trim();
            return root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
        }

        private string ResolveInitialLeftPath()
        {
            var savedPaths = LoadSavedPanePaths();
            if (!string.IsNullOrWhiteSpace(savedPaths.LeftPath) && IsNavigableDirectoryPath(savedPaths.LeftPath))
            {
                return savedPaths.LeftPath;
            }

            if (Directory.Exists(Environment.CurrentDirectory))
            {
                return Environment.CurrentDirectory;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private string ResolveInitialRightPath(string leftPath)
        {
            var savedPaths = LoadSavedPanePaths();
            if (!string.IsNullOrWhiteSpace(savedPaths.RightPath) && IsNavigableDirectoryPath(savedPaths.RightPath))
            {
                return savedPaths.RightPath;
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(documents) && !PathEquals(documents, leftPath))
            {
                return documents;
            }

            return Path.GetPathRoot(leftPath) ?? leftPath;
        }

        internal static bool PathEquals(string left, string right)
        {
            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizePath(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static bool IsNavigableDirectoryPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return Directory.Exists(path) ||
                IsSshPath(path) ||
                TryEnumerateWslDistributions(path, out _) ||
                TryEnumerateUncServerShares(path, out _);
        }

        internal static bool IsSshPath(string? path)
        {
            return TryParseSshPath(path, out _, out _);
        }

        internal static bool TryParseSshPath(string? path, out string connection, out string remotePath)
        {
            connection = string.Empty;
            remotePath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            const string prefix = "ssh://";
            var trimmed = path.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var remainder = trimmed[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(remainder))
            {
                return false;
            }

            var slashIndex = remainder.IndexOf('/');
            connection = slashIndex >= 0
                ? remainder[..slashIndex].Trim()
                : remainder.Trim();
            if (string.IsNullOrWhiteSpace(connection))
            {
                return false;
            }

            remotePath = slashIndex >= 0
                ? remainder[slashIndex..].Trim()
                : ".";
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                remotePath = ".";
            }

            if (!string.Equals(remotePath, ".", StringComparison.Ordinal) &&
                !remotePath.StartsWith("/", StringComparison.Ordinal))
            {
                remotePath = "/" + remotePath;
            }

            return true;
        }

        internal static string BuildSshPath(string connection, string remotePath)
        {
            var normalizedRemotePath = string.IsNullOrWhiteSpace(remotePath) ? "/" : remotePath.Trim();
            if (!normalizedRemotePath.StartsWith("/", StringComparison.Ordinal))
            {
                normalizedRemotePath = "/" + normalizedRemotePath;
            }

            return string.Equals(normalizedRemotePath, "/", StringComparison.Ordinal)
                ? $"ssh://{connection}/"
                : $"ssh://{connection}{normalizedRemotePath}";
        }

        internal static string GetSshParentPath(string path)
        {
            if (!TryParseSshPath(path, out var connection, out var remotePath))
            {
                return path;
            }

            var normalizedRemotePath = remotePath.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalizedRemotePath) || string.Equals(normalizedRemotePath, string.Empty, StringComparison.Ordinal))
            {
                normalizedRemotePath = "/";
            }

            if (string.Equals(normalizedRemotePath, "/", StringComparison.Ordinal))
            {
                return BuildSshPath(connection, "/");
            }

            var lastSlash = normalizedRemotePath.LastIndexOf('/');
            var parentPath = lastSlash <= 0
                ? "/"
                : normalizedRemotePath[..lastSlash];
            return BuildSshPath(connection, parentPath);
        }

        internal static bool TryLoadSshDirectory(
            string path,
            out string normalizedPath,
            out IReadOnlyList<FileEntry> entries,
            out string errorMessage)
        {
            normalizedPath = path;
            entries = Array.Empty<FileEntry>();
            errorMessage = string.Empty;

            if (!TryParseSshPath(path, out var connection, out var remotePath))
            {
                errorMessage = "SSH 路徑格式不正確。請使用 ssh://host/path。";
                AppendSshDebugLog($"TryLoadSshDirectory parse-failed path={path}");
                return false;
            }

            try
            {
                AppendSshDebugLog($"TryLoadSshDirectory start path={path} connection={connection} remotePath={remotePath}");
                const string pathMarker = "__NUONE_PWD__";
                const string entriesMarker = "__NUONE_ENTRIES__";
                var script = new StringBuilder();
                script.AppendLine("cd -- \"$1\" || exit 1");
                script.Append("printf '%s\\n' '");
                script.Append(pathMarker);
                script.AppendLine("'");
                script.AppendLine("pwd");
                script.Append("printf '\\n%s\\n' '");
                script.Append(entriesMarker);
                script.AppendLine("'");
                script.AppendLine("find . -mindepth 1 -maxdepth 1 -printf '%y\\t%f\\t%TY-%Tm-%Td %TH:%TM\\t%s\\n'");

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ssh.exe",
                        RedirectStandardInput = true,
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
                process.StartInfo.ArgumentList.Add("sh");
                process.StartInfo.ArgumentList.Add("-s");
                process.StartInfo.ArgumentList.Add("--");
                process.StartInfo.ArgumentList.Add(remotePath);

                if (!process.Start())
                {
                    errorMessage = "無法啟動 ssh.exe。";
                    AppendSshDebugLog($"TryLoadSshDirectory start-process-failed connection={connection} remotePath={remotePath}");
                    return false;
                }

                process.StandardInput.Write(script.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                var stdError = process.StandardError.ReadToEnd().Trim();
                AppendSshDebugLog(
                    $"TryLoadSshDirectory started connection={connection} remotePath={remotePath} " +
                    $"outputPreview={BuildLogPreview(output)} stderrPreview={BuildLogPreview(stdError)}");
                if (!process.WaitForExit(8000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    errorMessage = "SSH 目錄讀取逾時。";
                    AppendSshDebugLog($"TryLoadSshDirectory timeout connection={connection} remotePath={remotePath}");
                    return false;
                }

                AppendSshDebugLog(
                    $"TryLoadSshDirectory exit connection={connection} remotePath={remotePath} " +
                    $"exitCode={process.ExitCode} stderrPreview={BuildLogPreview(stdError)} outputPreview={BuildLogPreview(output)}");

                if (process.ExitCode != 0)
                {
                    errorMessage = string.IsNullOrWhiteSpace(stdError)
                        ? $"SSH 命令失敗（ExitCode {process.ExitCode}）。"
                        : stdError;
                    AppendSshDebugLog($"TryLoadSshDirectory nonzero-exit error={errorMessage}");
                    return false;
                }

                var normalizedOutput = output.Replace("\r\n", "\n", StringComparison.Ordinal);
                var pathMarkerIndex = normalizedOutput.IndexOf($"{pathMarker}\n", StringComparison.Ordinal);
                var entriesMarkerIndex = normalizedOutput.IndexOf($"\n{entriesMarker}\n", StringComparison.Ordinal);
                if (pathMarkerIndex < 0 || entriesMarkerIndex < 0 || entriesMarkerIndex <= pathMarkerIndex)
                {
                    var outputPreview = normalizedOutput.Trim();
                    if (outputPreview.Length > 240)
                    {
                        outputPreview = outputPreview[..240];
                    }

                    errorMessage = string.IsNullOrWhiteSpace(outputPreview)
                        ? "SSH 回應格式無法辨識。"
                        : $"SSH 回應格式無法辨識：{outputPreview}";
                    AppendSshDebugLog($"TryLoadSshDirectory invalid-format error={errorMessage}");
                    return false;
                }

                var canonicalPathStart = pathMarkerIndex + pathMarker.Length + 1;
                var canonicalPath = normalizedOutput[canonicalPathStart..entriesMarkerIndex].Trim();
                if (string.IsNullOrWhiteSpace(canonicalPath))
                {
                    errorMessage = "SSH 回應缺少遠端路徑。";
                    AppendSshDebugLog("TryLoadSshDirectory missing-canonical-path");
                    return false;
                }

                normalizedPath = BuildSshPath(connection, string.IsNullOrWhiteSpace(canonicalPath) ? remotePath : canonicalPath);

                var result = new List<FileEntry>();
                var entriesText = normalizedOutput[(entriesMarkerIndex + entriesMarker.Length + 2)..];
                if (!string.IsNullOrWhiteSpace(entriesText))
                {
                    foreach (var rawLine in entriesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var columns = rawLine.Split('\t');
                        if (columns.Length < 4)
                        {
                            continue;
                        }

                        var entryType = columns[0];
                        var name = columns[1];
                        var modifiedText = columns[2];
                        var isDirectory = string.Equals(entryType, "d", StringComparison.Ordinal);
                        long? sizeBytes = long.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize)
                            ? parsedSize
                            : null;
                        var childRemotePath = string.Equals(canonicalPath, "/", StringComparison.Ordinal)
                            ? $"/{name}"
                            : $"{canonicalPath.TrimEnd('/')}/{name}";
                        result.Add(FileEntry.FromRemoteEntry(
                            name,
                            BuildSshPath(connection, childRemotePath),
                            isDirectory,
                            modifiedText,
                            sizeBytes));
                    }
                }

                entries = result
                    .OrderByDescending(static entry => entry.IsDirectory)
                    .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                AppendSshDebugLog(
                    $"TryLoadSshDirectory success normalizedPath={normalizedPath} entries={entries.Count}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                AppendSshDebugLog($"TryLoadSshDirectory exception {ex}");
                return false;
            }
        }

        internal static void AppendSshDebugLog(string message)
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                var logPath = Path.Combine(ConfigDirectoryPath, "ssh-debug.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
                lock (SshDebugLogLock)
                {
                    File.AppendAllText(logPath, line, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        internal static void AppendDebugLog(string fileName, string message)
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                var logPath = Path.Combine(ConfigDirectoryPath, fileName);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
                lock (SshDebugLogLock)
                {
                    File.AppendAllText(logPath, line, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private static string BuildLogPreview(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var normalized = value
                .Replace("\r\n", "\\n", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Trim();

            return normalized.Length > 320
                ? normalized[..320]
                : normalized;
        }

        internal static bool IsWslVirtualRootPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = NormalizePath(path.Trim());
            return string.Equals(normalized, @"\\wsl$", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, @"\\wsl.localhost", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsWslPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = NormalizePath(path.Trim());
            return normalized.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase) ||
                IsWslVirtualRootPath(normalized);
        }

        internal static bool IsWslDistributionPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = NormalizePath(path.Trim());
            if (!normalized.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var segments = normalized[2..]
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 2;
        }

        internal static bool TryEnumerateWslDistributions(string? path, out IReadOnlyList<string> distributionPaths)
        {
            distributionPaths = Array.Empty<string>();
            if (!IsWslVirtualRootPath(path))
            {
                return false;
            }

            var root = NormalizePath(path!.Trim());
            var distributionNames = EnumerateWslDistributionNames();
            if (distributionNames.Count == 0)
            {
                return false;
            }

            distributionPaths = distributionNames
                .Select(name => $@"{root}\{name}")
                .ToArray();
            return true;
        }

        private static bool TryReadSshDirectorySnapshot(
            string path,
            out string normalizedPath,
            out IReadOnlyList<SshEntrySnapshot> entries,
            out string errorMessage)
        {
            normalizedPath = path;
            entries = Array.Empty<SshEntrySnapshot>();
            errorMessage = string.Empty;

            if (!TryParseSshPath(path, out var connection, out var remotePath))
            {
                errorMessage = "SSH 路徑格式不正確。請使用 ssh://host/path。";
                AppendSshDebugLog($"TryReadSshDirectorySnapshot parse-failed path={path}");
                return false;
            }

            try
            {
                AppendSshDebugLog($"TryReadSshDirectorySnapshot start path={path} connection={connection} remotePath={remotePath}");
                const string pathMarker = "__NUONE_PWD__";
                const string entriesMarker = "__NUONE_ENTRIES__";
                var script = new StringBuilder();
                script.AppendLine("cd -- \"$1\" || exit 1");
                script.Append("printf '%s\\n' '");
                script.Append(pathMarker);
                script.AppendLine("'");
                script.AppendLine("pwd");
                script.Append("printf '\\n%s\\n' '");
                script.Append(entriesMarker);
                script.AppendLine("'");
                script.AppendLine("find . -mindepth 1 -maxdepth 1 -printf '%y\\t%f\\t%TY-%Tm-%Td %TH:%TM\\t%s\\n'");

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ssh.exe",
                        RedirectStandardInput = true,
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
                process.StartInfo.ArgumentList.Add("sh");
                process.StartInfo.ArgumentList.Add("-s");
                process.StartInfo.ArgumentList.Add("--");
                process.StartInfo.ArgumentList.Add(remotePath);

                if (!process.Start())
                {
                    errorMessage = "無法啟動 ssh.exe。";
                    AppendSshDebugLog($"TryReadSshDirectorySnapshot start-process-failed connection={connection} remotePath={remotePath}");
                    return false;
                }

                process.StandardInput.Write(script.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                var stdError = process.StandardError.ReadToEnd().Trim();
                AppendSshDebugLog(
                    $"TryReadSshDirectorySnapshot started connection={connection} remotePath={remotePath} " +
                    $"outputPreview={BuildLogPreview(output)} stderrPreview={BuildLogPreview(stdError)}");

                if (!process.WaitForExit(8000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    errorMessage = "SSH 目錄讀取逾時。";
                    AppendSshDebugLog($"TryReadSshDirectorySnapshot timeout connection={connection} remotePath={remotePath}");
                    return false;
                }

                AppendSshDebugLog(
                    $"TryReadSshDirectorySnapshot exit connection={connection} remotePath={remotePath} " +
                    $"exitCode={process.ExitCode} stderrPreview={BuildLogPreview(stdError)} outputPreview={BuildLogPreview(output)}");

                if (process.ExitCode != 0)
                {
                    errorMessage = string.IsNullOrWhiteSpace(stdError)
                        ? $"SSH 命令失敗（ExitCode {process.ExitCode}）。"
                        : stdError;
                    AppendSshDebugLog($"TryReadSshDirectorySnapshot nonzero-exit error={errorMessage}");
                    return false;
                }

                var normalizedOutput = output.Replace("\r\n", "\n", StringComparison.Ordinal);
                var pathMarkerIndex = normalizedOutput.IndexOf($"{pathMarker}\n", StringComparison.Ordinal);
                var entriesMarkerIndex = normalizedOutput.IndexOf($"\n{entriesMarker}\n", StringComparison.Ordinal);
                if (pathMarkerIndex < 0 || entriesMarkerIndex < 0 || entriesMarkerIndex <= pathMarkerIndex)
                {
                    var outputPreview = normalizedOutput.Trim();
                    if (outputPreview.Length > 240)
                    {
                        outputPreview = outputPreview[..240];
                    }

                    errorMessage = string.IsNullOrWhiteSpace(outputPreview)
                        ? "SSH 回應格式無法辨識。"
                        : $"SSH 回應格式無法辨識：{outputPreview}";
                    AppendSshDebugLog($"TryReadSshDirectorySnapshot invalid-format error={errorMessage}");
                    return false;
                }

                var canonicalPathStart = pathMarkerIndex + pathMarker.Length + 1;
                var canonicalPath = normalizedOutput[canonicalPathStart..entriesMarkerIndex].Trim();
                if (string.IsNullOrWhiteSpace(canonicalPath))
                {
                    errorMessage = "SSH 回應缺少遠端路徑。";
                    AppendSshDebugLog("TryReadSshDirectorySnapshot missing-canonical-path");
                    return false;
                }

                normalizedPath = BuildSshPath(connection, string.IsNullOrWhiteSpace(canonicalPath) ? remotePath : canonicalPath);

                var result = new List<SshEntrySnapshot>();
                var entriesText = normalizedOutput[(entriesMarkerIndex + entriesMarker.Length + 2)..];
                if (!string.IsNullOrWhiteSpace(entriesText))
                {
                    foreach (var rawLine in entriesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var columns = rawLine.Split('\t');
                        if (columns.Length < 4)
                        {
                            continue;
                        }

                        var entryType = columns[0];
                        var name = columns[1];
                        var modifiedText = columns[2];
                        var isDirectory = string.Equals(entryType, "d", StringComparison.Ordinal);
                        long? sizeBytes = long.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize)
                            ? parsedSize
                            : null;
                        var childRemotePath = string.Equals(canonicalPath, "/", StringComparison.Ordinal)
                            ? $"/{name}"
                            : $"{canonicalPath.TrimEnd('/')}/{name}";

                        result.Add(new SshEntrySnapshot
                        {
                            Name = name,
                            FullPath = BuildSshPath(connection, childRemotePath),
                            IsDirectory = isDirectory,
                            ModifiedText = modifiedText,
                            SizeBytes = sizeBytes,
                        });
                    }
                }

                entries = result
                    .OrderByDescending(static entry => entry.IsDirectory)
                    .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                AppendSshDebugLog(
                    $"TryReadSshDirectorySnapshot success normalizedPath={normalizedPath} entries={entries.Count}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                AppendSshDebugLog($"TryReadSshDirectorySnapshot exception {ex}");
                return false;
            }
        }

        private static IReadOnlyList<string> EnumerateWslDistributionNames()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wsl.exe",
                        Arguments = "-l -q",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                if (!process.Start())
                {
                    return Array.Empty<string>();
                }

                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(3000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    return Array.Empty<string>();
                }

                if (process.ExitCode != 0)
                {
                    return Array.Empty<string>();
                }

                return output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(static line => SanitizeWslDistributionName(line))
                    .Where(static line => !string.IsNullOrWhiteSpace(line))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static line => line, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string SanitizeWslDistributionName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Trim()
                .Trim('\uFEFF', '\0')
                .Replace("\0", string.Empty, StringComparison.Ordinal);
        }

        internal static bool TryEnumerateUncServerShares(string? path, out IReadOnlyList<string> sharePaths)
        {
            sharePaths = Array.Empty<string>();
            if (!TryGetUncServerName(path, out var serverName))
            {
                return false;
            }

            sharePaths = EnumerateUncServerShares(serverName);
            return sharePaths.Count > 0;
        }

        private static bool TryGetUncServerName(string? path, out string serverName)
        {
            serverName = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var trimmed = path.Trim();
            if (!trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            var normalized = NormalizePath(trimmed);
            if (normalized.Length <= 2)
            {
                return false;
            }

            var segments = normalized[2..]
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length != 1)
            {
                return false;
            }

            serverName = segments[0];
            return !string.IsNullOrWhiteSpace(serverName);
        }

        private static IReadOnlyList<string> EnumerateUncServerShares(string serverName)
        {
            var results = new List<string>();
            nint buffer = nint.Zero;

            try
            {
                var resumeHandle = 0;
                var status = NetShareEnum(
                    $@"\\{serverName}",
                    1,
                    out buffer,
                    -1,
                    out var entriesRead,
                    out _,
                    ref resumeHandle);

                if (status != 0 || buffer == nint.Zero || entriesRead <= 0)
                {
                    return results;
                }

                var current = buffer;
                var itemSize = Marshal.SizeOf<SHARE_INFO_1>();
                for (var index = 0; index < entriesRead; index++)
                {
                    var info = Marshal.PtrToStructure<SHARE_INFO_1>(current);
                    current += itemSize;

                    if (string.IsNullOrWhiteSpace(info.shi1_netname))
                    {
                        continue;
                    }

                    if (info.shi1_type != ShareType.DiskTree)
                    {
                        continue;
                    }

                    results.Add($@"\\{serverName}\{info.shi1_netname}");
                }

                return results
                    .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
            finally
            {
                if (buffer != nint.Zero)
                {
                    NetApiBufferFree(buffer);
                }
            }
        }

        private void OpenInPane(PaneViewModel pane, string path)
        {
            if (IsSshPath(path))
            {
                _ = OpenSshPathInPaneAsync(pane, path, rememberCurrent: true);
                ActivatePane(pane);
                return;
            }

            pane.NavigateTo(path);
            ActivatePane(pane);
        }

        private void RefreshPane(PaneViewModel pane)
        {
            if (IsSshPath(pane.CurrentPath))
            {
                _ = OpenSshPathInPaneAsync(pane, pane.CurrentPath, rememberCurrent: false);
                return;
            }

            pane.Refresh();
            LoadDriveCards();
        }

        private void RefreshPaneAfterLocalChange(PaneViewModel pane)
        {
            GetPaneWatcher(pane).SuppressRefreshFor(PaneWatcherSuppressInterval);
            RefreshPane(pane);
        }

        private PaneDirectoryWatcher GetPaneWatcher(PaneViewModel pane)
        {
            return ReferenceEquals(pane, LeftPane)
                ? _leftPaneWatcher
                : _rightPaneWatcher;
        }

        private void NavigateUp(PaneViewModel pane)
        {
            if (IsSshPath(pane.CurrentPath))
            {
                _ = OpenSshPathInPaneAsync(pane, GetSshParentPath(pane.CurrentPath), rememberCurrent: true);
                ActivatePane(pane);
                return;
            }

            pane.NavigateUp();
        }

        private void NavigateBack(PaneViewModel pane)
        {
            pane.GoBack();
        }

        private async Task NavigateToEditablePathAsync(PaneViewModel pane, string? rawPath = null)
        {
            ActivatePane(pane);

            var requestedPath = (rawPath ?? pane.EditablePath)?.Trim();
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                pane.EditablePath = pane.CurrentPath;
                return;
            }

            if (!IsNavigableDirectoryPath(requestedPath))
            {
                pane.EditablePath = pane.CurrentPath;
                await ShowMessageAsync("路徑不存在", requestedPath);
                return;
            }

            if (IsSshPath(requestedPath))
            {
                await OpenSshPathInPaneAsync(pane, requestedPath, rememberCurrent: true);
                ActivatePane(pane);
                return;
            }

            OpenInPane(pane, requestedPath);
        }

        private async Task OpenSshPathInPaneAsync(PaneViewModel pane, string path, bool rememberCurrent)
        {
            ActivatePane(pane);
            var loadingPath = path.Trim();
            var requestVersion = 0;
            AppendSshDebugLog($"OpenSshPathInPaneAsync start pane={pane.Name} path={loadingPath} rememberCurrent={rememberCurrent}");
            await EnqueueOnUiAsync(() =>
            {
                requestVersion = pane.BeginLoad(loadingPath, "讀取遠端 Linux 目錄中...");
                AppendSshDebugLog($"OpenSshPathInPaneAsync begin-load pane={pane.Name} path={loadingPath} requestVersion={requestVersion}");
            });

            try
            {
                var loadResult = await Task.Run(() =>
                {
                    if (!TryReadSshDirectorySnapshot(loadingPath, out var normalizedPath, out var entries, out var errorMessage))
                    {
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorMessage)
                            ? "無法載入遠端 Linux 目錄。"
                            : errorMessage);
                    }

                    return (normalizedPath, entries);
                });

                await EnqueueOnUiAsync(() =>
                {
                    var uiEntries = loadResult.entries
                        .Select(entry => FileEntry.FromRemoteEntry(
                            entry.Name,
                            entry.FullPath,
                            entry.IsDirectory,
                            entry.ModifiedText,
                            entry.SizeBytes))
                        .ToArray();

                    AppendSshDebugLog(
                        $"OpenSshPathInPaneAsync apply-success pane={pane.Name} path={loadingPath} " +
                        $"normalizedPath={loadResult.normalizedPath} requestVersion={requestVersion} entries={uiEntries.Length}");
                    pane.ApplyLoadedEntries(loadResult.normalizedPath, uiEntries, rememberCurrent, requestVersion);
                    LoadDriveCards();
                });
            }
            catch (Exception ex)
            {
                AppendSshDebugLog(
                    $"OpenSshPathInPaneAsync apply-error pane={pane.Name} path={loadingPath} " +
                    $"requestVersion={requestVersion} error={ex}");
                await EnqueueOnUiAsync(() => pane.ApplyLoadError(loadingPath, ex.Message, requestVersion));
            }
        }

        private void ActivatePane(PaneViewModel pane)
        {
            _activePane = pane;
            UpdateActivePaneVisuals();
            UpdateSharedStatusBar();
        }

        private void UpdateActivePaneVisuals()
        {
            var isLeftActive = ReferenceEquals(_activePane, LeftPane);

            ApplyPaneVisualState(
                LeftPaneBorder,
                LeftPathTextBox,
                isLeftActive);

            ApplyPaneVisualState(
                RightPaneBorder,
                RightPathTextBox,
                !isLeftActive);
        }

        private void ApplyPaneVisualState(
            Border paneBorder,
            TextBox pathTextBox,
            bool isActive)
        {
            var activeBorder = ParseColor("#BF4CFF");
            var inactiveBorder = GetBrushColor("PanelStrokeBrush", "#3A3146");
            var activeFill = GetBrushColor("InputAltBrush", "#231E2B");
            var inactiveFill = GetBrushColor("InputBrush", "#1B1621");

            paneBorder.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBorder);
            paneBorder.BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
            pathTextBox.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBorder);
            pathTextBox.Background = new SolidColorBrush(isActive ? activeFill : inactiveFill);
        }

        private void OpenSelectedInExplorer()
        {
            var selectedEntries = GetSelectedEntries(_activePane);
            if (selectedEntries.Count == 0)
            {
                return;
            }

            foreach (var entry in selectedEntries)
            {
                OpenPath(entry.FullPath);
            }
        }

        internal void RefreshAll_Click(object sender, RoutedEventArgs e)
        {
            RefreshPane(LeftPane);
            RefreshPane(RightPane);
        }

        private void RefreshLeft_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            RefreshPane(LeftPane);
        }

        private void RefreshRight_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
            RefreshPane(RightPane);
        }

        private void NavigateUpLeft_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            NavigateUp(LeftPane);
        }

        private void NavigateUpRight_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
            NavigateUp(RightPane);
        }

        private void BackLeft_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
            NavigateBack(LeftPane);
        }

        private void BackRight_Click(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
            NavigateBack(RightPane);
        }

        internal async void LeftPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await NavigateToEditablePathAsync(LeftPane, (sender as TextBox)?.Text);
            }
        }

        internal async void RightPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await NavigateToEditablePathAsync(RightPane, (sender as TextBox)?.Text);
            }
        }

        internal void LeftPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        internal void RightPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        internal void LeftPaneContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        internal void RightPaneContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        internal void LeftPaneList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(LeftPane);
        }

        internal void LeftPaneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncPaneSelectionFromListView(LeftPane, LeftPaneListView);
            ApplySelectionVisuals(LeftPaneListView);
            ScheduleSelectionSizeUpdate(LeftPane);
        }

        internal void RightPaneList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ActivatePane(RightPane);
        }

        internal void RightPaneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncPaneSelectionFromListView(RightPane, RightPaneListView);
            ApplySelectionVisuals(RightPaneListView);
            ScheduleSelectionSizeUpdate(RightPane);
        }

        internal void PaneListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem listViewItem)
            {
                ApplySelectionVisualToContainer(listViewItem);
            }
        }

        internal void ClearPaneFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: PaneViewModel pane })
            {
                pane.ClearFilter();
                SyncPaneFilterSelection(pane);
            }
        }

        internal void DriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path })
            {
                OpenInPane(_activePane, path);
            }
        }

        internal void DriveShortcut_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: DriveShortcut drive } element)
            {
                return;
            }

            e.Handled = true;

            var flyout = new MenuFlyout();
            var hideItem = new MenuFlyoutItem
            {
                Text = $"隱藏 {drive.Name}",
                Tag = drive.RootPath,
            };
            hideItem.Click += HideDriveShortcut_Click;
            flyout.Items.Add(hideItem);
            flyout.ShowAt(element);
        }

        private void HideDriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: string rootPath })
            {
                return;
            }

            var normalizedRootPath = NormalizeDriveRootPath(rootPath);
            if (string.IsNullOrWhiteSpace(normalizedRootPath))
            {
                return;
            }

            _hiddenDrivePaths.Add(normalizedRootPath);
            SaveShortcutSettingsSafe();
            LoadDriveCards();
            UpdateDriveSectionMenuState();
        }

        internal void DriveRestoreFlyout_Opening(object sender, object e)
        {
            if (sender is not MenuFlyout flyout)
            {
                return;
            }

            flyout.Items.Clear();

            var hiddenDrives = _hiddenDrivePaths
                .Select(path => new
                {
                    Name = path.TrimEnd(Path.DirectorySeparatorChar),
                    RootPath = path,
                })
                .OrderBy(static drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hiddenDrives.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = "沒有已隱藏的磁碟機",
                    IsEnabled = false,
                });
                return;
            }

            foreach (var drive in hiddenDrives)
            {
                var restoreItem = new MenuFlyoutItem
                {
                    Text = $"恢復 {drive.Name}",
                    Tag = drive.RootPath,
                };
                restoreItem.Click += RestoreDriveShortcut_Click;
                flyout.Items.Add(restoreItem);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());
            var restoreAllItem = new MenuFlyoutItem { Text = "恢復全部磁碟機" };
            restoreAllItem.Click += RestoreAllDriveShortcuts_Click;
            flyout.Items.Add(restoreAllItem);
        }

        private void RestoreDriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: string rootPath })
            {
                return;
            }

            _hiddenDrivePaths.Remove(NormalizeDriveRootPath(rootPath));
            SaveShortcutSettingsSafe();
            LoadDriveCards();
            UpdateDriveSectionMenuState();
        }

        private void RestoreAllDriveShortcuts_Click(object sender, RoutedEventArgs e)
        {
            if (_hiddenDrivePaths.Count == 0)
            {
                return;
            }

            _hiddenDrivePaths.Clear();
            SaveShortcutSettingsSafe();
            LoadDriveCards();
            UpdateDriveSectionMenuState();
        }

        private void UpdateDriveSectionMenuState()
        {
            if (DriveSectionMenuButton is not null)
            {
                DriveSectionMenuButton.Opacity = _hiddenDrivePaths.Count > 0 ? 1 : 0.82;
            }
        }

        private void RefreshSelectionSizeDisplays()
        {
            ScheduleSelectionSizeUpdate(LeftPane, immediate: true);
            ScheduleSelectionSizeUpdate(RightPane, immediate: true);
        }

        private void ScheduleSelectionSizeUpdate(PaneViewModel pane, bool immediate = false)
        {
            var selectedEntries = GetSelectedEntries(pane);
            pane.UpdateSelectionText(BuildSelectionSummary(selectedEntries));

            var timer = ReferenceEquals(pane, LeftPane) ? _leftSelectionSizeTimer : _rightSelectionSizeTimer;
            var cancellationTokenSource = ReferenceEquals(pane, LeftPane)
                ? Interlocked.Exchange(ref _leftSelectionSizeCts, null)
                : Interlocked.Exchange(ref _rightSelectionSizeCts, null);

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();

            timer.Stop();
            if (immediate)
            {
                _ = UpdateSelectionSizeAsync(pane);
                return;
            }

            timer.Start();
        }

        private void LeftSelectionSizeTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            _ = UpdateSelectionSizeAsync(LeftPane);
        }

        private void RightSelectionSizeTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            _ = UpdateSelectionSizeAsync(RightPane);
        }
    }
}
