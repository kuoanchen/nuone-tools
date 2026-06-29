using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
        private void ResetEditableShortcutSettings()
        {
            _isUpdatingSettingsUi = true;
            _settingsCaptureTarget = ShortcutCaptureTarget.None;
            _editingShortcutSettings = CloneShortcutSettings(_shortcutSettings);
            SyncShortcutText();
            ShowHiddenSystemItemsToggle.IsOn = _editingShortcutSettings.ShowHiddenSystemItems;
            ShowSelectedFileSizeToggle.IsOn = _editingShortcutSettings.ShowSelectedFileSize;
            ShowSelectedFolderSizeToggle.IsOn = _editingShortcutSettings.ShowSelectedFolderSize;
            SyncThemeModeSelection();
            SyncDefaultTerminalShellSelection();
            SyncDefaultTerminalWorkingDirectoryModeSelection();
            DefaultTerminalCustomWorkingDirectoryTextBox.Text = _editingShortcutSettings.DefaultTerminalCustomWorkingDirectory;
            UpdateDefaultTerminalCustomWorkingDirectoryVisibility();
            UpdateFileBunkerSettingsUi();
            UpdateLoggingSettingsUi();
            UpdateAppUpdateUi();
            CaptureHintTextBlock.Text = "按「修改」後，再按下實體鍵，會立即套用。";
            _isUpdatingSettingsUi = false;
        }

        private void UpdateFileBunkerSettingsUi()
        {
            if (FileBunkerInputEndpointTextBox is null ||
                FileBunkerOutputEndpointTextBox is null ||
                FileBunkerClientIdTextBox is null ||
                FileBunkerKeyLengthTextBox is null ||
                FileBunkerDaysToExpirationTextBox is null ||
                FileBunkerDaysToPurgeTextBox is null ||
                FileBunkerApiKeyTextBox is null)
            {
                return;
            }

            _isUpdatingFileBunkerUi = true;
            try
            {
                FileBunkerInputEndpointTextBox.Text = _fileBunkerSettings.InputEndpoint;
                FileBunkerOutputEndpointTextBox.Text = _fileBunkerSettings.OutputEndpointBase;
                FileBunkerClientIdTextBox.Text = _fileBunkerSettings.ClientId;
                FileBunkerKeyLengthTextBox.Text = _fileBunkerSettings.KeyLength.ToString(CultureInfo.InvariantCulture);
                FileBunkerDaysToExpirationTextBox.Text = _fileBunkerSettings.DaysToExpiration.ToString(CultureInfo.InvariantCulture);
                FileBunkerDaysToPurgeTextBox.Text = _fileBunkerSettings.DaysToPurge.ToString(CultureInfo.InvariantCulture);
                FileBunkerApiKeyTextBox.Text = _fileBunkerSettings.ApiKey;
            }
            finally
            {
                _isUpdatingFileBunkerUi = false;
            }
        }

        private void UpdateLoggingSettingsUi()
        {
            if (LogDirectoryPathTextBox is null ||
                LastLocalBackupTextBlock is null)
            {
                return;
            }

            _isUpdatingLoggingUi = true;
            try
            {
                LogDirectoryPathTextBox.Text = NormalizeLogDirectoryPath(_loggingSettings.LogDirectoryPath);
                LastLocalBackupTextBlock.Text = $"最後一次備份：{_lastLocalBackupText}";
            }
            finally
            {
                _isUpdatingLoggingUi = false;
            }
        }

        private void UpdateAppUpdateUi()
        {
            if (CurrentAppVersionTextBlock is null ||
                AppUpdateManifestUrlTextBlock is null ||
                AppUpdateStatusTextBlock is null ||
                LatestAppVersionTextBlock is null ||
                LastAppUpdateCheckTextBlock is null ||
                AppUpdateReleaseNotesTextBlock is null ||
                CheckForUpdatesButton is null ||
                AppUpdateActionButtonsPanel is null ||
                OpenUpdateDownloadButton is null ||
                CopyUpdateDownloadUrlButton is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_appUpdateState.CurrentVersionText))
            {
                _appUpdateState.CurrentVersionText = GetCurrentAppVersion();
            }

            CurrentAppVersionTextBlock.Text = _appUpdateState.CurrentVersionText;
            AppUpdateManifestUrlTextBlock.Text = _appUpdateState.ManifestUrl;
            AppUpdateStatusTextBlock.Text = _appUpdateState.StatusText;
            LatestAppVersionTextBlock.Text = $"最新版本：{_appUpdateState.LatestVersionText}";
            LastAppUpdateCheckTextBlock.Text = $"上次檢查：{_appUpdateState.LastCheckedText}";
            AppUpdateReleaseNotesTextBlock.Text = _appUpdateState.ReleaseNotes;
            AppUpdateReleaseNotesTextBlock.Visibility = string.IsNullOrWhiteSpace(_appUpdateState.ReleaseNotes)
                ? Visibility.Collapsed
                : Visibility.Visible;
            CheckForUpdatesButton.IsEnabled = !_appUpdateState.IsChecking && !_appUpdateState.IsInstalling;
            CheckForUpdatesButton.Content = _appUpdateState.IsInstalling
                ? "更新中..."
                : _appUpdateState.IsChecking
                    ? "檢查中..."
                    : "檢查更新";
            var hasDownloadAction = !_appUpdateState.IsInstalling &&
                                    _appUpdateState.IsUpdateAvailable &&
                                    !string.IsNullOrWhiteSpace(_appUpdateState.DownloadUrl);
            AppUpdateActionButtonsPanel.Visibility = hasDownloadAction
                ? Visibility.Visible
                : Visibility.Collapsed;
            OpenUpdateDownloadButton.IsEnabled = hasDownloadAction;
            CopyUpdateDownloadUrlButton.IsEnabled = hasDownloadAction;
        }

        internal async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(isAutomatic: false);
        }

        internal async void OpenUpdateDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var downloadUrl = _appUpdateState.DownloadUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                await ShowMessageAsync("下載新版失敗", "目前沒有可用的下載連結。");
                return;
            }

            try
            {
                using var response = await SharedHttpClient.GetAsync(_appUpdateState.ManifestUrl);
                response.EnsureSuccessStatusCode();

                var rawManifest = await response.Content.ReadAsStringAsync();
                var manifest = DeserializeAppUpdateManifest(rawManifest);
                var resolvedDownloadUrl = ResolveUpdateDownloadUrl(_appUpdateState.ManifestUrl, manifest.Package?.Url);
                var latestVersion = string.IsNullOrWhiteSpace(manifest.Version)
                    ? _appUpdateState.LatestVersionText
                    : manifest.Version.Trim();
                var currentVersion = string.IsNullOrWhiteSpace(_appUpdateState.CurrentVersionText)
                    ? GetCurrentAppVersion()
                    : _appUpdateState.CurrentVersionText;

                if (string.IsNullOrWhiteSpace(resolvedDownloadUrl))
                {
                    await ShowMessageAsync("下載新版失敗", "目前沒有可用的下載連結。");
                    return;
                }

                await InstallAppUpdateAsync(manifest, resolvedDownloadUrl, latestVersion, currentVersion);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("下載新版失敗", ex.Message);
            }
        }

        internal async void CopyUpdateDownloadUrlButton_Click(object sender, RoutedEventArgs e)
        {
            var downloadUrl = _appUpdateState.DownloadUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                await ShowMessageAsync("複製下載連結失敗", "目前沒有可用的下載連結。");
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(downloadUrl);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
            CaptureHintTextBlock.Text = "已複製新版下載連結。";
        }

        private async Task CheckForUpdatesAsync(bool isAutomatic)
        {
            if (_appUpdateState.IsChecking)
            {
                return;
            }

            _appUpdateState.IsChecking = true;
            _appUpdateState.CurrentVersionText = GetCurrentAppVersion();
            _appUpdateState.StatusText = "檢查更新中...";
            UpdateAppUpdateUi();

            try
            {
                AppLogging.Information("App update check started Automatic={IsAutomatic}", isAutomatic);
                using var response = await SharedHttpClient.GetAsync(_appUpdateState.ManifestUrl);
                response.EnsureSuccessStatusCode();

                var rawManifest = await response.Content.ReadAsStringAsync();
                var manifest = DeserializeAppUpdateManifest(rawManifest);
                var resolvedDownloadUrl = ResolveUpdateDownloadUrl(_appUpdateState.ManifestUrl, manifest.Package?.Url);
                var currentVersion = _appUpdateState.CurrentVersionText;
                var latestVersion = string.IsNullOrWhiteSpace(manifest.Version)
                    ? currentVersion
                    : manifest.Version.Trim();
                var isUpdateAvailable = CompareAppVersions(latestVersion, currentVersion) > 0;

                _appUpdateState.LatestVersionText = latestVersion;
                _appUpdateState.ReleaseNotes = BuildAppUpdateReleaseNotesText(manifest, resolvedDownloadUrl);
                _appUpdateState.DownloadUrl = resolvedDownloadUrl;
                _appUpdateState.IsUpdateAvailable = isUpdateAvailable;
                _appUpdateState.LastCheckedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _appUpdateState.StatusText = isUpdateAvailable
                    ? isAutomatic
                        ? $"發現新版本：{latestVersion}，等待你確認下載與重啟。"
                        : $"發現新版本：{latestVersion}，請確認是否下載與安裝。"
                    : "目前已是最新版本";
                UpdateAppUpdateUi();
                AppLogging.Information(
                    "App update check completed Automatic={IsAutomatic} CurrentVersion={CurrentVersion} LatestVersion={LatestVersion} UpdateAvailable={IsUpdateAvailable}",
                    isAutomatic,
                    currentVersion,
                    latestVersion,
                    isUpdateAvailable);

                if (isAutomatic && isUpdateAvailable)
                {
                    var shouldDownload = await ConfirmAsync(
                        "發現新版",
                        $"目前版本是 {currentVersion}，發現新版 {latestVersion}。要現在下載更新包嗎？");
                    if (!shouldDownload)
                    {
                        _appUpdateState.StatusText = $"發現新版本：{latestVersion}，你已取消本次更新。";
                        UpdateAppUpdateUi();
                        return;
                    }

                    var updateInstalled = await InstallAppUpdateAsync(
                        manifest,
                        resolvedDownloadUrl,
                        latestVersion,
                        currentVersion,
                        skipDownloadConfirmation: true);
                    if (updateInstalled)
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _appUpdateState.IsUpdateAvailable = false;
                _appUpdateState.LastCheckedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _appUpdateState.StatusText = $"檢查更新失敗：{ex.Message}";
                _appUpdateState.ReleaseNotes = string.Empty;
                AppLogging.Error(ex, "App update check failed Automatic={IsAutomatic}", isAutomatic);

                if (!isAutomatic)
                {
                    await ShowMessageAsync("檢查更新失敗", ex.Message);
                }
            }
            finally
            {
                _appUpdateState.IsChecking = false;
                if (!_isClosingForAppUpdate)
                {
                    UpdateAppUpdateUi();
                }
            }
        }

        private async Task<bool> InstallAppUpdateAsync(
            AppUpdateManifest manifest,
            string resolvedDownloadUrl,
            string latestVersion,
            string currentVersion,
            bool skipDownloadConfirmation = false)
        {
            if (string.IsNullOrWhiteSpace(resolvedDownloadUrl))
            {
                return false;
            }

            var currentExecutablePath = GetCurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(currentExecutablePath) || !File.Exists(currentExecutablePath))
            {
                return false;
            }

            var installDirectory = Path.GetDirectoryName(currentExecutablePath);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return false;
            }

            if (IsDevelopmentInstallDirectory(installDirectory))
            {
                _appUpdateState.StatusText = "發現新版本，但目前是開發版執行目錄，已略過自動套用。";
                UpdateAppUpdateUi();
                return false;
            }

            if (!CanWriteToDirectory(installDirectory))
            {
                _appUpdateState.StatusText = "發現新版本，但目前安裝目錄不可寫入，請用手動下載更新。";
                UpdateAppUpdateUi();
                return false;
            }

            var updateRoot = Path.Combine(
                Path.GetTempPath(),
                "nuone-tools-update",
                latestVersion,
                Guid.NewGuid().ToString("N"));
            var downloadFileName = GetDownloadFileName(manifest, resolvedDownloadUrl);
            var downloadPath = Path.Combine(updateRoot, downloadFileName);
            var extractDirectory = Path.Combine(updateRoot, "extract");

            try
            {
                if (!skipDownloadConfirmation &&
                    !await ConfirmAsync(
                        "下載新版",
                        $"目前版本是 {currentVersion}，發現新版 {latestVersion}。要現在下載更新包嗎？"))
                {
                    _appUpdateState.StatusText = $"已取消下載 {latestVersion}。";
                    UpdateAppUpdateUi();
                    return false;
                }

                Directory.CreateDirectory(updateRoot);
                Directory.CreateDirectory(extractDirectory);

                _appUpdateState.IsInstalling = true;
                _appUpdateState.StatusText = "下載新版中...";
                UpdateAppUpdateUi();
                AppLogging.Information(
                    "App update install started CurrentVersion={CurrentVersion} LatestVersion={LatestVersion} DownloadUrl={DownloadUrl}",
                    currentVersion,
                    latestVersion,
                    resolvedDownloadUrl);

                using (var response = await SharedHttpClient.GetAsync(resolvedDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    await using var responseStream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await responseStream.CopyToAsync(fileStream);
                }

                ValidateDownloadedUpdateHash(manifest.Package?.Sha256, downloadPath);

                _appUpdateState.StatusText = "解壓更新中...";
                UpdateAppUpdateUi();

                ZipFile.ExtractToDirectory(downloadPath, extractDirectory, overwriteFiles: true);
                var payloadDirectory = ResolveAppUpdatePayloadDirectory(extractDirectory, Path.GetFileName(currentExecutablePath));

                _appUpdateState.IsInstalling = false;
                _appUpdateState.StatusText = $"新版 {latestVersion} 已下載完成，等待安裝確認。";
                UpdateAppUpdateUi();

                if (!await ConfirmAsync(
                        "安裝並重新啟動",
                        $"新版 {latestVersion} 已下載完成。要現在關閉 Nuone Tools、套用更新並重新啟動嗎？"))
                {
                    _appUpdateState.StatusText = $"新版 {latestVersion} 已下載完成，尚未安裝。";
                    CaptureHintTextBlock.Text = $"新版 {latestVersion} 已下載完成，等待你確認安裝。";
                    UpdateAppUpdateUi();
                    return false;
                }

                _appUpdateState.IsInstalling = true;
                _appUpdateState.StatusText = "套用更新中...";
                UpdateAppUpdateUi();

                var installerScriptPath = Path.Combine(updateRoot, "apply-update.ps1");
                File.WriteAllText(installerScriptPath, BuildAppUpdateInstallerScript(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = updateRoot,
                };
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(installerScriptPath);
                startInfo.ArgumentList.Add("-InstallDirectory");
                startInfo.ArgumentList.Add(installDirectory);
                startInfo.ArgumentList.Add("-PayloadDirectory");
                startInfo.ArgumentList.Add(payloadDirectory);
                startInfo.ArgumentList.Add("-CurrentProcessId");
                startInfo.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("-ExecutableName");
                startInfo.ArgumentList.Add(Path.GetFileName(currentExecutablePath));

                Process.Start(startInfo);
                AppLogging.Information(
                    "App update install scheduled CurrentVersion={CurrentVersion} LatestVersion={LatestVersion} InstallDirectory={InstallDirectory} PayloadDirectory={PayloadDirectory}",
                    currentVersion,
                    latestVersion,
                    installDirectory,
                    payloadDirectory);
                _appUpdateState.StatusText = $"已確認安裝 {latestVersion}，完成後會重新啟動。";
                CaptureHintTextBlock.Text = $"已開始安裝 {latestVersion}，接著會重新啟動。";
                UpdateAppUpdateUi();
                _isClosingForAppUpdate = true;
                Close();
                return true;
            }
            catch (Exception ex)
            {
                _appUpdateState.IsInstalling = false;
                _appUpdateState.StatusText = $"自動更新失敗：{ex.Message}";
                AppLogging.Error(ex, "App update install failed CurrentVersion={CurrentVersion} LatestVersion={LatestVersion} DownloadUrl={DownloadUrl}", currentVersion, latestVersion, resolvedDownloadUrl);
                UpdateAppUpdateUi();
                return false;
            }
        }

        private static string GetCurrentAppVersion()
        {
            var informationalVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var plusIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
                return plusIndex >= 0
                    ? informationalVersion[..plusIndex]
                    : informationalVersion;
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        }

        private static AppUpdateManifest DeserializeAppUpdateManifest(string rawManifest)
        {
            var normalizedManifest = NormalizeManifestPayload(rawManifest);
            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            var manifest = JsonSerializer.Deserialize<AppUpdateManifest>(normalizedManifest, serializerOptions);
            if (manifest is null)
            {
                throw new InvalidOperationException("manifest.json 無法解析。");
            }

            return manifest;
        }

        private static string NormalizeManifestPayload(string rawManifest)
        {
            var candidate = (rawManifest ?? string.Empty).Trim().TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(candidate))
            {
                throw new InvalidOperationException("manifest.json 是空的。");
            }

            if (candidate.StartsWith("\"", StringComparison.Ordinal))
            {
                var unwrapped = JsonSerializer.Deserialize<string>(candidate);
                candidate = (unwrapped ?? string.Empty).Trim().TrimStart('\uFEFF');
            }

            return candidate;
        }

        private static string ResolveUpdateDownloadUrl(string manifestUrl, string? packageUrl)
        {
            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(packageUrl.Trim(), UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri))
            {
                return packageUrl.Trim();
            }

            return new Uri(manifestUri, packageUrl.Trim()).ToString();
        }

        private static string GetCurrentExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                return Environment.ProcessPath!;
            }

            var mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModulePath))
            {
                return mainModulePath;
            }

            return Path.Combine(AppContext.BaseDirectory, "nuone-tools.exe");
        }

        private static string GetDownloadFileName(AppUpdateManifest manifest, string resolvedDownloadUrl)
        {
            var packageFileName = manifest.Package?.Filename?.Trim();
            if (!string.IsNullOrWhiteSpace(packageFileName))
            {
                return packageFileName;
            }

            if (Uri.TryCreate(resolvedDownloadUrl, UriKind.Absolute, out var absoluteUri))
            {
                var leafName = Path.GetFileName(absoluteUri.LocalPath);
                if (!string.IsNullOrWhiteSpace(leafName))
                {
                    return leafName;
                }
            }

            return "nuone-tools-update.zip";
        }

        private static void ValidateDownloadedUpdateHash(string? expectedHash, string downloadPath)
        {
            var normalizedExpectedHash = (expectedHash ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedExpectedHash))
            {
                return;
            }

            using var stream = File.OpenRead(downloadPath);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(hash, normalizedExpectedHash.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("下載的更新包 SHA256 驗證失敗。");
            }
        }

        private static bool IsDevelopmentInstallDirectory(string installDirectory)
        {
            var normalized = Path.GetFullPath(installDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalized.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveAppUpdatePayloadDirectory(string extractDirectory, string executableName)
        {
            if (File.Exists(Path.Combine(extractDirectory, executableName)))
            {
                return extractDirectory;
            }

            var candidate = Directory
                .EnumerateDirectories(extractDirectory, "*", SearchOption.AllDirectories)
                .FirstOrDefault(directory => File.Exists(Path.Combine(directory, executableName)));

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }

            throw new InvalidOperationException($"更新包中找不到 {executableName}。");
        }

        private static bool CanWriteToDirectory(string directoryPath)
        {
            try
            {
                Directory.CreateDirectory(directoryPath);
                var probePath = Path.Combine(directoryPath, $".nuone-tools-write-test-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probePath, "ok");
                File.Delete(probePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildAppUpdateInstallerScript()
        {
            return """
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDirectory,
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory,
    [Parameter(Mandatory = $true)]
    [int]$CurrentProcessId,
    [Parameter(Mandatory = $true)]
    [string]$ExecutableName
)

$ErrorActionPreference = 'Stop'
$LogPath = Join-Path $PSScriptRoot 'apply-update.log'

function Write-UpdateLog {
    param([string]$Message)
    Add-Content -LiteralPath $LogPath -Value ("{0} {1}" -f [DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss.fff'), $Message)
}

try {
    Write-UpdateLog "Updater started. Payload=$PayloadDirectory Install=$InstallDirectory ProcessId=$CurrentProcessId Executable=$ExecutableName"

    while (Get-Process -Id $CurrentProcessId -ErrorAction SilentlyContinue) {
        Start-Sleep -Milliseconds 500
    }

    Write-UpdateLog "Source process exited. Starting robocopy."
    & robocopy $PayloadDirectory $InstallDirectory /MIR /R:3 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    Write-UpdateLog "Robocopy finished. ExitCode=$LASTEXITCODE"
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed with exit code $LASTEXITCODE"
    }

    $targetPath = Join-Path $InstallDirectory $ExecutableName
    Write-UpdateLog "Starting updated app. Path=$targetPath"
    $process = Start-Process -FilePath $targetPath -WorkingDirectory $InstallDirectory -PassThru
    Write-UpdateLog "Updated app started. ProcessId=$($process.Id)"
    Start-Sleep -Seconds 1
    Write-UpdateLog "Cleaning updater folder."
    Remove-Item -LiteralPath $PSScriptRoot -Recurse -Force -ErrorAction SilentlyContinue
}
catch {
    Write-UpdateLog ("Updater failed. " + $_.Exception.ToString())
    throw
}
""";
        }

        private static int CompareAppVersions(string left, string right)
        {
            if (Version.TryParse(left, out var leftVersion) &&
                Version.TryParse(right, out var rightVersion))
            {
                return leftVersion.CompareTo(rightVersion);
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildAppUpdateReleaseNotesText(AppUpdateManifest manifest, string resolvedDownloadUrl)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
            {
                lines.Add($"更新內容：{manifest.ReleaseNotes.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(manifest.PublishedAt))
            {
                lines.Add($"發佈時間：{FormatAppUpdatePublishedAt(manifest.PublishedAt)}");
            }

            if (manifest.Mandatory)
            {
                lines.Add("這個版本標記為必要更新。");
            }

            if (!string.IsNullOrWhiteSpace(resolvedDownloadUrl))
            {
                lines.Add($"下載位置：{resolvedDownloadUrl}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatAppUpdatePublishedAt(string? publishedAt)
        {
            var value = publishedAt?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return value;
        }

        private void SyncThemeModeSelection()
        {
            if (ThemeModeComboBox is null)
            {
                return;
            }

            foreach (var item in ThemeModeComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag
                    && string.Equals(tag, _editingShortcutSettings.ThemeMode.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    ThemeModeComboBox.SelectedItem = item;
                    return;
                }
            }

            ThemeModeComboBox.SelectedIndex = 0;
        }

        private void SyncDefaultTerminalShellSelection()
        {
            if (DefaultTerminalShellComboBox is null)
            {
                return;
            }

            foreach (var item in DefaultTerminalShellComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag
                    && string.Equals(tag, _editingShortcutSettings.DefaultTerminalShellKind.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    DefaultTerminalShellComboBox.SelectedItem = item;
                    return;
                }
            }

            DefaultTerminalShellComboBox.SelectedIndex = 0;
        }

        private void SyncDefaultTerminalWorkingDirectoryModeSelection()
        {
            if (DefaultTerminalWorkingDirectoryModeComboBox is null)
            {
                return;
            }

            foreach (var item in DefaultTerminalWorkingDirectoryModeComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag
                    && string.Equals(tag, _editingShortcutSettings.DefaultTerminalWorkingDirectoryMode.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    DefaultTerminalWorkingDirectoryModeComboBox.SelectedItem = item;
                    return;
                }
            }

            DefaultTerminalWorkingDirectoryModeComboBox.SelectedIndex = 0;
        }

        private void UpdateDefaultTerminalCustomWorkingDirectoryVisibility()
        {
            if (DefaultTerminalCustomWorkingDirectoryTextBox is null)
            {
                return;
            }

            DefaultTerminalCustomWorkingDirectoryTextBox.Visibility =
                _editingShortcutSettings.DefaultTerminalWorkingDirectoryMode == ToolbarWorkingDirectoryMode.CustomPath
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        internal void ShowGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.General);
        }

        internal void ShowAppearanceSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Appearance);
        }

        internal void ShowAccountSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Account);
        }

        internal void ShowShortcutSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Shortcuts);
        }

        internal void ShowToolbarSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsSection(SettingsSection.Toolbar);
        }

        internal async void AddToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = await ShowToolbarCommandEditorAsync(null);
                if (item is null)
                {
                    return;
                }

                ToolbarCommands.Add(item);
                SaveToolbarCommandsSafe();
                AddSyncSettingsNotification("工具列設定已更新", $"新增工具列按鈕：{item.Title}");
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "add toolbar command");
                await ShowMessageAsync("新增工具列按鈕失敗", ex.Message);
            }
        }

        internal async void EditToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement { Tag: ToolbarCommandItem item })
                {
                    return;
                }

                var editedItem = await ShowToolbarCommandEditorAsync(item);
                if (editedItem is null)
                {
                    return;
                }

                item.Title = editedItem.Title;
                item.Command = editedItem.Command;
                item.IconPath = editedItem.IconPath;
                item.IconGlyph = editedItem.IconGlyph;
                item.NodeDockerUser = editedItem.NodeDockerUser;
                item.NodeDockerHost = editedItem.NodeDockerHost;
                item.NodeDockerRemoteDirectory = editedItem.NodeDockerRemoteDirectory;
                item.NodeDockerLaunchMode = editedItem.NodeDockerLaunchMode;
                item.TerminalShellKind = editedItem.TerminalShellKind;
                item.TerminalWorkingDirectoryMode = editedItem.TerminalWorkingDirectoryMode;
                item.TerminalCustomWorkingDirectory = editedItem.TerminalCustomWorkingDirectory;
                item.TerminalLaunchArguments = editedItem.TerminalLaunchArguments;
                SaveToolbarCommandsSafe();
                AddSyncSettingsNotification("工具列設定已更新", $"編輯工具列按鈕：{item.Title}");
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "edit toolbar command");
                await ShowMessageAsync("編輯工具列按鈕失敗", ex.Message);
            }
        }

        internal async void DeleteToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement { Tag: ToolbarCommandItem item })
                {
                    return;
                }

                var confirmed = await ConfirmAsync("刪除工具列按鈕", $"確定要刪除「{item.Title}」嗎？");
                if (!confirmed)
                {
                    return;
                }

                ToolbarCommands.Remove(item);
                SaveToolbarCommandsSafe();
                AddSyncSettingsNotification("工具列設定已更新", $"刪除工具列按鈕：{item.Title}");
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "delete toolbar command");
                await ShowMessageAsync("刪除工具列按鈕失敗", ex.Message);
            }
        }

        internal void ToolbarCommandsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            SyncToolbarCommandsOrder(sender);
            SaveToolbarCommandsSafe();
        }

        private async void ToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ToolbarCommandItem item })
            {
                return;
            }

            await ExecuteToolbarCommandAsync(item);
        }

        internal async void TopToolbarListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not ToolbarCommandItem item)
            {
                return;
            }

            await ExecuteToolbarCommandAsync(item);
        }

        internal void ToolbarItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(GetBrushColor("InputAltBrush", "#231E2B"));
                border.BorderBrush = new SolidColorBrush(GetBrushColor("PanelStrokeBrush", "#3A3146"));
            }
        }

        internal void ToolbarItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
                border.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
        }

        internal async void ToolbarIconPresenter_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Grid presenter)
                {
                    return;
                }

                await RefreshToolbarIconPresenterAsync(presenter);
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "toolbar icon presenter loaded");
            }
        }

        internal async void ToolbarIconPresenter_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            try
            {
                if (sender is not Grid presenter)
                {
                    return;
                }

                await RefreshToolbarIconPresenterAsync(presenter);
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "toolbar icon presenter data-context changed");
            }
        }

        internal void ToolbarIconSummary_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBlock { Tag: ToolbarCommandItem item } textBlock)
            {
                return;
            }

            textBlock.Text = string.IsNullOrWhiteSpace(item.IconPath)
                ? $"Glyph：{item.DisplayGlyph}"
                : $"圖示檔案：{item.IconPath}";
        }

        private static async Task UpdateToolbarIconVisualAsync(Image image, FontIcon fontIcon, string? iconPath, string glyph)
        {
            var imageSource = ToolbarCommandItem.CreateIconImageSource(iconPath);
            if (imageSource is null && ToolbarCommandItem.IsExecutableIconSource(iconPath))
            {
                imageSource = await ToolbarCommandItem.CreateShellIconImageSourceAsync(iconPath);
            }

            if (imageSource is not null)
            {
                image.Source = imageSource;
                image.Visibility = Visibility.Visible;
                fontIcon.Visibility = Visibility.Collapsed;
                return;
            }

            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            fontIcon.Visibility = Visibility.Visible;
            fontIcon.Glyph = glyph;
        }

        private static async Task RefreshToolbarIconPresenterAsync(Grid presenter)
        {
            if (presenter.Tag is not ToolbarCommandItem item ||
                presenter.Children.Count < 2 ||
                presenter.Children[0] is not Image image ||
                presenter.Children[1] is not FontIcon fontIcon)
            {
                return;
            }

            var expectedItem = item;

            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            fontIcon.Visibility = Visibility.Visible;
            fontIcon.Glyph = expectedItem.DisplayGlyph;

            var imageSource = ToolbarCommandItem.CreateIconImageSource(expectedItem.IconPath);
            if (imageSource is null && ToolbarCommandItem.IsExecutableIconSource(expectedItem.IconPath))
            {
                imageSource = await ToolbarCommandItem.CreateShellIconImageSourceAsync(expectedItem.IconPath);
            }

            if (!ReferenceEquals(presenter.Tag, expectedItem))
            {
                return;
            }

            if (imageSource is not null)
            {
                image.Source = imageSource;
                image.Visibility = Visibility.Visible;
                fontIcon.Visibility = Visibility.Collapsed;
                return;
            }

            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            fontIcon.Visibility = Visibility.Visible;
            fontIcon.Glyph = expectedItem.DisplayGlyph;
        }

        private void SyncToolbarCommandsOrder(ListViewBase sender)
        {
            var orderedItems = sender.Items
                .OfType<ToolbarCommandItem>()
                .ToList();

            if (orderedItems.Count == 0 || orderedItems.Count != ToolbarCommands.Count)
            {
                return;
            }

            var currentOrder = ToolbarCommands.ToList();
            if (currentOrder.SequenceEqual(orderedItems))
            {
                return;
            }

            ToolbarCommands.Clear();
            foreach (var orderedItem in orderedItems)
            {
                ToolbarCommands.Add(orderedItem);
            }
        }

        private void SwitchToSettingsSection(SettingsSection section)
        {
            _activeSettingsSection = section;
            UpdateSettingsSectionVisuals();
            UpdateSharedStatusBar();
        }

        private void UpdateSettingsSectionVisuals()
        {
            var isGeneral = _activeSettingsSection == SettingsSection.General;
            var isAccount = _activeSettingsSection == SettingsSection.Account;
            var isAppearance = _activeSettingsSection == SettingsSection.Appearance;
            var isShortcuts = _activeSettingsSection == SettingsSection.Shortcuts;
            var isToolbar = _activeSettingsSection == SettingsSection.Toolbar;

            GeneralSettingsContent.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
            AccountSettingsContent.Visibility = isAccount ? Visibility.Visible : Visibility.Collapsed;
            AppearanceSettingsContent.Visibility = isAppearance ? Visibility.Visible : Visibility.Collapsed;
            ShortcutSettingsContent.Visibility = isShortcuts ? Visibility.Visible : Visibility.Collapsed;
            ToolbarSettingsContent.Visibility = isToolbar ? Visibility.Visible : Visibility.Collapsed;

            ApplySettingsNavState(GeneralSettingsNavBorder, GeneralSettingsNavText, isGeneral);
            ApplySettingsNavState(AccountSettingsNavBorder, AccountSettingsNavText, isAccount);
            ApplySettingsNavState(AppearanceSettingsNavBorder, AppearanceSettingsNavText, isAppearance);
            ApplySettingsNavState(ShortcutSettingsNavBorder, ShortcutSettingsNavText, isShortcuts);
            ApplySettingsNavState(ToolbarSettingsNavBorder, ToolbarSettingsNavText, isToolbar);

            SettingsPageTitle.Text = _activeSettingsSection switch
            {
                SettingsSection.Account => "帳號",
                SettingsSection.Appearance => "外觀",
                SettingsSection.Shortcuts => "快捷鍵",
                SettingsSection.Toolbar => "工具列",
                _ => "一般",
            };

            SettingsPageDescription.Text = _activeSettingsSection switch
            {
                SettingsSection.Account => "連接 Nuone 後端帳號。登入成功後會把 API、email 與 token 狀態儲存到本機 config。",
                SettingsSection.Appearance => "調整 Nuone Tools 的視覺偏好。變更後會立即儲存到本機 config。",
                SettingsSection.Shortcuts => "設定常用鍵盤快捷鍵。變更後會立即儲存到本機 config。",
                SettingsSection.Toolbar => "管理上方工具列按鈕。變更後會立即儲存到本機 config。",
                _ => "設定檔案顯示、內建終端機預設、診斷 log 目錄與常用鍵盤快捷鍵。變更後會立即儲存到本機 config。",
            };
        }

        private void RootLayout_ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (_shortcutSettings.ThemeMode == AppThemeMode.System)
            {
                ApplyThemePalette(GetEffectiveTheme());
            }
        }

        internal void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSettingsUi || ThemeModeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            {
                return;
            }

            if (!Enum.TryParse<AppThemeMode>(tag, ignoreCase: true, out var themeMode))
            {
                return;
            }

            _editingShortcutSettings.ThemeMode = themeMode;
            _shortcutSettings.ThemeMode = themeMode;
            ApplyThemePreference();
            SaveShortcutSettingsSafe();
            AddSyncSettingsNotification("外觀設定已更新", $"主題模式：{themeMode}");
        }

        private void ApplyThemePreference()
        {
            if (RootLayout is null)
            {
                return;
            }

            RootLayout.RequestedTheme = _shortcutSettings.ThemeMode switch
            {
                AppThemeMode.Dark => ElementTheme.Dark,
                AppThemeMode.Light => ElementTheme.Light,
                _ => ElementTheme.Default,
            };

            ApplyThemePalette(GetEffectiveTheme());
        }

        private ElementTheme GetEffectiveTheme()
        {
            return _shortcutSettings.ThemeMode switch
            {
                AppThemeMode.Dark => ElementTheme.Dark,
                AppThemeMode.Light => ElementTheme.Light,
                _ when RootLayout.ActualTheme == ElementTheme.Light => ElementTheme.Light,
                _ => ElementTheme.Dark,
            };
        }

        private void ApplyThemePalette(ElementTheme theme)
        {
            var isLight = theme == ElementTheme.Light;

            SetBrushColor("AppBackgroundBrush", isLight ? "#F5F2FA" : "#141119");
            SetBrushColor("PanelBrush", isLight ? "#FFFFFF" : "#211C28");
            SetBrushColor("PanelAltBrush", isLight ? "#F0EBF7" : "#191520");
            SetBrushColor("PanelStrokeBrush", isLight ? "#D8CCE8" : "#3A3146");
            SetBrushColor("AppRailBrush", isLight ? "#ECE6F4" : "#0F2025");
            SetBrushColor("AppRailBorderBrush", isLight ? "#D4CADE" : "#1F3138");
            SetBrushColor("AppRailActiveBrush", isLight ? "#FFFFFF" : "#2D2835");
            SetBrushColor("AppRailActiveBorderBrush", isLight ? "#CDBCE4" : "#4A3E58");
            SetBrushColor("AppRailFooterBrush", isLight ? "#F5F0FB" : "#171622");
            SetBrushColor("AppRailFooterBorderBrush", isLight ? "#D8CCE8" : "#2F2740");
            SetBrushColor("CardBrush", isLight ? "#F7F3FB" : "#2A252F");
            SetBrushColor("InputBrush", isLight ? "#FFFFFF" : "#1B1621");
            SetBrushColor("InputAltBrush", isLight ? "#FFFFFF" : "#231E2B");
            SetBrushColor("AccentBrush", isLight ? "#A53AF4" : "#BF4CFF");
            SetBrushColor("AccentSoftBrush", isLight ? "#8B35C4" : "#8B35C4");
            SetBrushColor("TextPrimaryBrush", isLight ? "#1E1528" : "#F6F2FF");
            SetBrushColor("TextSecondaryBrush", isLight ? "#6E617F" : "#B9AECF");
            SetBrushColor("SuccessBrush", "#74D89B");
            SetBrushColor("WarningBrush", "#D09A00");

            if (TryGetAppResource("AppBackgroundBrush", out var appBackgroundResource)
                && appBackgroundResource is SolidColorBrush appBackgroundBrush)
            {
                RootLayout.Background = appBackgroundBrush;
            }

            SelectedItemBackgroundBrush.Color = ParseColor(isLight ? "#E7CCFF" : "#5B147E");
            SelectedItemBorderBrush.Color = ParseColor(isLight ? "#B24DF1" : "#8C3CBC");
            UnselectedItemBackgroundBrush.Color = Colors.Transparent;
            UnselectedItemBorderBrush.Color = Colors.Transparent;

            UpdateActivePaneVisuals();
            UpdateAppSectionVisuals();
            UpdateSettingsSectionVisuals();
        }

        private void SetBrushColor(string resourceKey, string colorHex)
        {
            if (TryGetAppResource(resourceKey, out var resource) && resource is SolidColorBrush brush)
            {
                brush.Color = ParseColor(colorHex);
            }
        }

        private bool TryGetAppResource(string resourceKey, out object? resource)
        {
            if (RootLayout.Resources.TryGetValue(resourceKey, out resource))
            {
                return true;
            }

            var appResources = Application.Current.Resources;
            if (appResources.TryGetValue(resourceKey, out resource))
            {
                return true;
            }

            for (var index = appResources.MergedDictionaries.Count - 1; index >= 0; index--)
            {
                if (appResources.MergedDictionaries[index].TryGetValue(resourceKey, out resource))
                {
                    return true;
                }
            }

            resource = null;
            return false;
        }

        private void ApplyThemeToDialog(ContentDialog dialog)
        {
            dialog.RequestedTheme = GetEffectiveTheme() switch
            {
                ElementTheme.Light => ElementTheme.Light,
                _ => ElementTheme.Dark,
            };
        }

        private Windows.UI.Color GetBrushColor(string resourceKey, string fallbackHex)
        {
            if (TryGetAppResource(resourceKey, out var resource) && resource is SolidColorBrush brush)
            {
                return brush.Color;
            }

            return ParseColor(fallbackHex);
        }

        private static Windows.UI.Color ParseColor(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return Colors.Transparent;
            }

            if (colorHex.Length == 7)
            {
                return ColorHelper.FromArgb(
                    255,
                    Convert.ToByte(colorHex[1..3], 16),
                    Convert.ToByte(colorHex[3..5], 16),
                    Convert.ToByte(colorHex[5..7], 16));
            }

            return ColorHelper.FromArgb(
                Convert.ToByte(colorHex[1..3], 16),
                Convert.ToByte(colorHex[3..5], 16),
                Convert.ToByte(colorHex[5..7], 16),
                Convert.ToByte(colorHex[7..9], 16));
        }

        private void ApplySettingsNavState(Border border, TextBlock textBlock, bool isSelected)
        {
            var selectedBackground = GetBrushColor("CardBrush", "#2A252F");
            var selectedBorder = GetBrushColor("PanelStrokeBrush", "#3A3146");
            var transparent = ColorHelper.FromArgb(0, 0, 0, 0);
            var primaryText = GetBrushColor("TextPrimaryBrush", "#F6F2FF");
            var secondaryText = GetBrushColor("TextSecondaryBrush", "#B9AECF");

            border.Background = new SolidColorBrush(isSelected ? selectedBackground : transparent);
            border.BorderBrush = new SolidColorBrush(isSelected ? selectedBorder : transparent);
            textBlock.Foreground = new SolidColorBrush(isSelected ? primaryText : secondaryText);
            textBlock.FontWeight = isSelected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        internal void EditCopyShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.CopyToOtherPane, CopyShortcutTextBox, "複製到另一個 Pane");
        }

        internal void EditMoveShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.MoveToOtherPane, MoveShortcutTextBox, "移動到另一個 Pane");
        }

        internal void EditNavigateUpShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.NavigateUp, NavigateUpShortcutTextBox, "上一層");
        }

        internal void EditCreateFolderShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.CreateFolder, CreateFolderShortcutTextBox, "新增資料夾");
        }

        internal void EditDeleteShortcut_Click(object sender, RoutedEventArgs e)
        {
            BeginShortcutCapture(ShortcutCaptureTarget.Delete, DeleteShortcutTextBox, "刪除");
        }

        private void BeginShortcutCapture(ShortcutCaptureTarget target, TextBox textBox, string label)
        {
            _settingsCaptureTarget = target;
            textBox.Text = "請按任意鍵...";
            CaptureHintTextBlock.Text = $"正在擷取「{label}」快捷鍵...";
            _ = RootLayout.Focus(FocusState.Programmatic);
        }

        internal void ShowHiddenSystemItemsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSettingsUi || ShowHiddenSystemItemsToggle is null)
            {
                return;
            }

            _editingShortcutSettings.ShowHiddenSystemItems = ShowHiddenSystemItemsToggle.IsOn;
            _shortcutSettings.ShowHiddenSystemItems = ShowHiddenSystemItemsToggle.IsOn;
            SaveShortcutSettingsSafe();
            AddSyncSettingsNotification(
                "一般設定已更新",
                $"顯示隱藏與系統項目：{(ShowHiddenSystemItemsToggle.IsOn ? "開啟" : "關閉")}");
            ApplySettingsToPanes();
            RefreshPane(LeftPane);
            RefreshPane(RightPane);
            CaptureHintTextBlock.Text = "已立即儲存顯示設定。";
        }

        internal void ShowSelectedFileSizeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSettingsUi || ShowSelectedFileSizeToggle is null)
            {
                return;
            }

            _editingShortcutSettings.ShowSelectedFileSize = ShowSelectedFileSizeToggle.IsOn;
            _shortcutSettings.ShowSelectedFileSize = ShowSelectedFileSizeToggle.IsOn;
            SaveShortcutSettingsSafe();
            AddSyncSettingsNotification(
                "一般設定已更新",
                $"顯示已選檔案大小：{(ShowSelectedFileSizeToggle.IsOn ? "開啟" : "關閉")}");
            RefreshSelectionSizeDisplays();
            CaptureHintTextBlock.Text = "已立即儲存檔案大小顯示設定。";
        }

        internal void ShowSelectedFolderSizeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSettingsUi || ShowSelectedFolderSizeToggle is null)
            {
                return;
            }

            _editingShortcutSettings.ShowSelectedFolderSize = ShowSelectedFolderSizeToggle.IsOn;
            _shortcutSettings.ShowSelectedFolderSize = ShowSelectedFolderSizeToggle.IsOn;
            SaveShortcutSettingsSafe();
            AddSyncSettingsNotification(
                "一般設定已更新",
                $"顯示已選資料夾大小：{(ShowSelectedFolderSizeToggle.IsOn ? "開啟" : "關閉")}");
            RefreshSelectionSizeDisplays();
            CaptureHintTextBlock.Text = "已立即儲存資料夾大小顯示設定。";
        }

        internal void DefaultTerminalShellComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSettingsUi || DefaultTerminalShellComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            {
                return;
            }

            if (!Enum.TryParse<TerminalShellKind>(tag, ignoreCase: true, out var shellKind))
            {
                return;
            }

            _editingShortcutSettings.DefaultTerminalShellKind = shellKind;
            _shortcutSettings.DefaultTerminalShellKind = shellKind;
            SaveShortcutSettingsSafe();
            AddSyncSettingsNotification("終端機設定已更新", $"預設 shell：{shellKind}");
            CaptureHintTextBlock.Text = "已立即儲存內建終端機預設 shell。";
        }

        internal void DefaultTerminalWorkingDirectoryModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSettingsUi || DefaultTerminalWorkingDirectoryModeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            {
                return;
            }

            if (!Enum.TryParse<ToolbarWorkingDirectoryMode>(tag, ignoreCase: true, out var workingDirectoryMode))
            {
                return;
            }

            _editingShortcutSettings.DefaultTerminalWorkingDirectoryMode = workingDirectoryMode;
            _shortcutSettings.DefaultTerminalWorkingDirectoryMode = workingDirectoryMode;
            UpdateDefaultTerminalCustomWorkingDirectoryVisibility();
            SaveShortcutSettingsSafe();
            AddSyncSettingsNotification("終端機設定已更新", $"預設工作目錄模式：{workingDirectoryMode}");
            CaptureHintTextBlock.Text = "已立即儲存內建終端機預設工作目錄。";
        }

        internal void DefaultTerminalCustomWorkingDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingSettingsUi || DefaultTerminalCustomWorkingDirectoryTextBox is null)
            {
                return;
            }

            var customPath = DefaultTerminalCustomWorkingDirectoryTextBox.Text.Trim();
            _editingShortcutSettings.DefaultTerminalCustomWorkingDirectory = customPath;
            _shortcutSettings.DefaultTerminalCustomWorkingDirectory = customPath;
            SaveShortcutSettingsSafe();
            CaptureHintTextBlock.Text = "已立即儲存內建終端機自訂工作目錄。";
        }

        internal void FileBunkerInputEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFileBunkerUi || FileBunkerInputEndpointTextBox is null)
            {
                return;
            }

            _fileBunkerSettings.InputEndpoint = FileBunkerInputEndpointTextBox.Text.Trim();
            SaveShortcutSettingsSafe();
        }

        internal void FileBunkerOutputEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFileBunkerUi || FileBunkerOutputEndpointTextBox is null)
            {
                return;
            }

            _fileBunkerSettings.OutputEndpointBase = FileBunkerOutputEndpointTextBox.Text.Trim();
            SaveShortcutSettingsSafe();
        }

        internal void FileBunkerClientIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFileBunkerUi || FileBunkerClientIdTextBox is null)
            {
                return;
            }

            _fileBunkerSettings.ClientId = FileBunkerClientIdTextBox.Text.Trim();
            SaveShortcutSettingsSafe();
        }

        internal void FileBunkerKeyLengthTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFileBunkerUi || FileBunkerKeyLengthTextBox is null)
            {
                return;
            }

            if (int.TryParse(FileBunkerKeyLengthTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                _fileBunkerSettings.KeyLength = value;
                SaveShortcutSettingsSafe();
            }
        }

        internal void FileBunkerDaysToExpirationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFileBunkerUi || FileBunkerDaysToExpirationTextBox is null)
            {
                return;
            }

            if (int.TryParse(FileBunkerDaysToExpirationTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                _fileBunkerSettings.DaysToExpiration = value;
                SaveShortcutSettingsSafe();
            }
        }

        internal void FileBunkerDaysToPurgeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFileBunkerUi || FileBunkerDaysToPurgeTextBox is null)
            {
                return;
            }

            if (int.TryParse(FileBunkerDaysToPurgeTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                _fileBunkerSettings.DaysToPurge = value;
                SaveShortcutSettingsSafe();
            }
        }

        internal void FileBunkerApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFileBunkerUi || FileBunkerApiKeyTextBox is null)
            {
                return;
            }

            _fileBunkerSettings.ApiKey = FileBunkerApiKeyTextBox.Text.Trim();
            SaveShortcutSettingsSafe();
        }

        internal void LogDirectoryPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingLoggingUi || LogDirectoryPathTextBox is null)
            {
                return;
            }

            _loggingSettings.LogDirectoryPath = NormalizeLogDirectoryPath(LogDirectoryPathTextBox.Text);
            ApplyConfiguredLogDirectoryPath(_loggingSettings.LogDirectoryPath);
            SaveShortcutSettingsSafe();
            CaptureHintTextBlock.Text = $"已立即儲存 log 目錄：{_loggingSettings.LogDirectoryPath}";
        }

        internal void UseDefaultLogDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            _loggingSettings.LogDirectoryPath = DefaultLogDirectoryPath;
            ApplyConfiguredLogDirectoryPath(_loggingSettings.LogDirectoryPath);
            UpdateLoggingSettingsUi();
            SaveShortcutSettingsSafe();
            CaptureHintTextBlock.Text = $"已切回預設 log 目錄：{_loggingSettings.LogDirectoryPath}";
        }

        internal async void OpenLogDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logDirectoryPath = NormalizeLogDirectoryPath(_loggingSettings.LogDirectoryPath);
                Directory.CreateDirectory(logDirectoryPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{logDirectoryPath}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("開啟 log 目錄失敗", ex.Message);
            }
        }

        internal async void BackupLocalSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetAuthenticatedSettingsSyncContext(out _))
            {
                await ShowMessageAsync("備份本機資料失敗", "請先登入 Nuone 帳號。");
                return;
            }

            var backgroundWorkId = BeginBackgroundWork("備份本機資料中");
            string? completionRecord = null;
            string? completionDetails = null;

            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                _lastLocalBackupText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                SaveLocalSettings();
                var settingsJson = await GetCurrentLocalSettingsJsonAsync();
                await UploadLocalSettingsJsonAsync(settingsJson, recordNotification: false);
                UpdateLoggingSettingsUi();
                CaptureHintTextBlock.Text = "已手動備份本機資料。";
                completionRecord = "完成：備份本機資料";
                completionDetails = $"已備份 settings-local.json（{GetLocalSettingsDeviceName()}）";
            }
            catch (Exception ex)
            {
                CaptureHintTextBlock.Text = $"本機資料備份失敗：{ex.Message}";
                completionRecord = "失敗：備份本機資料";
                completionDetails = ex.Message;
                await ShowMessageAsync("備份本機資料失敗", ex.Message);
            }
            finally
            {
                CompleteBackgroundWork(
                    backgroundWorkId,
                    completionRecord,
                    completionDetails,
                    persistToLocalHistory: false);
            }
        }

        internal void AccountApiUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingAccountUi || AccountApiUrlTextBox is null)
            {
                return;
            }

            _accountSettings.ApiBaseUrl = AccountApiUrlTextBox.Text.Trim();
            SaveLocalSettingsSafe();
        }

        internal void AccountEmailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingAccountUi || AccountEmailTextBox is null)
            {
                return;
            }

            _accountSettings.Email = AccountEmailTextBox.Text.Trim();
            SaveLocalSettingsSafe();
        }

        internal async void LoginAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAccountLoginRunning)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_accountSettings.Token) && !_isAccountReloginFieldsVisible)
            {
                _isAccountReloginFieldsVisible = true;
                UpdateAccountSettingsUi();
                return;
            }

            var apiBaseUrl = NormalizeApiBaseUrl(AccountApiUrlTextBox?.Text);
            var email = AccountEmailTextBox?.Text.Trim() ?? string.Empty;
            var password = AccountPasswordBox?.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                await ShowMessageAsync("帳號登入失敗", "API 位址不能為空。");
                return;
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await ShowMessageAsync("帳號登入失敗", "請輸入 email 與 password。");
                return;
            }

            _isAccountLoginRunning = true;
            _accountSettings.ApiBaseUrl = apiBaseUrl;
            _accountSettings.Email = email;
            _accountSettings.LastStatusText = "登入中...";
            UpdateAccountSettingsUi();
            SaveLocalSettingsSafe();

            var backgroundWorkId = BeginBackgroundWork("帳號登入中");

            try
            {
                var result = await LoginToIdentityAsync(apiBaseUrl, email, password);

                _accountSettings.Token = result.Token;
                _accountSettings.UserDisplayName = result.DisplayName;
                _accountSettings.ServiceAccountsSummary = result.ServiceAccountsSummary;
                _accountSettings.PayloadJson = result.PayloadJson;
                _accountSettings.ServiceAccountsJson = result.ServiceAccountsJson;
                _accountSettings.LastLoginText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _accountSettings.LastStatusText = "登入成功";
                _isAccountReloginFieldsVisible = false;
                if (AccountPasswordBox is not null)
                {
                    AccountPasswordBox.Password = string.Empty;
                }

                SaveLocalSettingsSafe();
                UpdateAccountSettingsUi();
                UpdateSharedStatusBar();
                AddSyncSettingsNotification("帳號設定已更新", $"登入 Nuone 帳號：{_accountSettings.UserDisplayName}");

                try
                {
                    await DownloadLatestSyncSettingsAsync("login");
                }
                catch (Exception syncEx)
                {
                    AppendDebugLog("settings-sync-debug.log", $"login-sync-failed message={syncEx.Message} detail={syncEx}");
                    AddNotificationHistoryRecord(NotificationHistoryScope.Sync, "同步", "同步設定下載失敗", $"login：{syncEx.Message}", showWindowsToast: false);
                }
            }
            catch (Exception ex)
            {
                _accountSettings.Token = string.Empty;
                _accountSettings.UserDisplayName = string.Empty;
                _accountSettings.ServiceAccountsSummary = string.Empty;
                _accountSettings.PayloadJson = string.Empty;
                _accountSettings.ServiceAccountsJson = string.Empty;
                _accountSettings.LastStatusText = $"登入失敗：{ex.Message}";
                SaveLocalSettingsSafe();
                UpdateAccountSettingsUi();
                UpdateSharedStatusBar();
                await ShowMessageAsync("帳號登入失敗", ex.Message);
            }
            finally
            {
                _isAccountLoginRunning = false;
                UpdateAccountSettingsUi();
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        internal void ClearAccountSessionButton_Click(object sender, RoutedEventArgs e)
        {
            _accountSettings.Token = string.Empty;
            _accountSettings.UserDisplayName = string.Empty;
            _accountSettings.ServiceAccountsSummary = string.Empty;
            _accountSettings.PayloadJson = string.Empty;
            _accountSettings.ServiceAccountsJson = string.Empty;
            _accountSettings.LastStatusText = "已清除本機登入狀態";
            _isAccountReloginFieldsVisible = false;
            if (AccountPasswordBox is not null)
            {
                AccountPasswordBox.Password = string.Empty;
            }

            SaveLocalSettingsSafe();
            UpdateAccountSettingsUi();
            UpdateSharedStatusBar();
            AddSyncSettingsNotification("帳號設定已更新", "已清除本機登入狀態");
        }

        private void AddSyncSettingsNotification(string summary, string details)
        {
            AddNotificationHistoryRecord(NotificationHistoryScope.Sync, "設定", summary, details, showWindowsToast: false);
        }

        private void SaveSettingsPage_Click(object sender, RoutedEventArgs e)
        {
            if (HasDuplicateShortcut(_editingShortcutSettings))
            {
                CaptureHintTextBlock.Text = "單鍵快捷鍵不能使用相同按鍵。";
                return;
            }

            _editingShortcutSettings.ShowHiddenSystemItems = ShowHiddenSystemItemsToggle.IsOn;
            _shortcutSettings = CloneShortcutSettings(_editingShortcutSettings);
            SaveShortcutSettingsSafe();
            ApplySettingsToPanes();
            RefreshPane(LeftPane);
            RefreshPane(RightPane);
            CaptureHintTextBlock.Text = "已儲存快捷鍵設定。";
            SwitchToAppSection(AppSection.FileManager);
        }

        private void CancelSettingsPage_Click(object sender, RoutedEventArgs e)
        {
            ResetEditableShortcutSettings();
            SwitchToAppSection(AppSection.FileManager);
        }

        internal void CustomGroupsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            SaveCustomGroupsSafe();
        }

        internal void GroupedPathsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            SaveCustomGroupsSafe();
        }

        private static bool HasDuplicateShortcut(ShortcutSettings settings)
        {
            var keys = new[]
            {
                settings.CopyToOtherPaneKey,
                settings.MoveToOtherPaneKey,
                settings.NavigateUpKey,
                settings.DeleteKey,
            };

            return keys.Distinct().Count() != keys.Length;
        }

        private void SyncShortcutText()
        {
            CopyShortcutTextBox.Text = FormatShortcutKey(_editingShortcutSettings.CopyToOtherPaneKey);
            MoveShortcutTextBox.Text = FormatShortcutKey(_editingShortcutSettings.MoveToOtherPaneKey);
            NavigateUpShortcutTextBox.Text = FormatShortcutKey(_editingShortcutSettings.NavigateUpKey);
            CreateFolderShortcutTextBox.Text = FormatCreateFolderShortcutKey(_editingShortcutSettings.CreateFolderKey);
            DeleteShortcutTextBox.Text = FormatShortcutKey(_editingShortcutSettings.DeleteKey);
        }

        private void ApplySettingsToPanes()
        {
            LeftPane.ShowHiddenSystemItems = _shortcutSettings.ShowHiddenSystemItems;
            RightPane.ShowHiddenSystemItems = _shortcutSettings.ShowHiddenSystemItems;
        }

        private static ShortcutSettings CloneShortcutSettings(ShortcutSettings source)
        {
            return new ShortcutSettings
            {
                CopyToOtherPaneKey = source.CopyToOtherPaneKey,
                MoveToOtherPaneKey = source.MoveToOtherPaneKey,
                NavigateUpKey = source.NavigateUpKey,
                CreateFolderKey = source.CreateFolderKey,
                DeleteKey = source.DeleteKey,
                ThemeMode = source.ThemeMode,
                ShowSelectedFileSize = source.ShowSelectedFileSize,
                ShowSelectedFolderSize = source.ShowSelectedFolderSize,
                ShowHiddenSystemItems = source.ShowHiddenSystemItems,
                DefaultTerminalShellKind = source.DefaultTerminalShellKind,
                DefaultTerminalWorkingDirectoryMode = source.DefaultTerminalWorkingDirectoryMode,
                DefaultTerminalCustomWorkingDirectory = source.DefaultTerminalCustomWorkingDirectory,
            };
        }
    }
}
