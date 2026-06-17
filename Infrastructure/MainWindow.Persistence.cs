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
        private void LoadShortcutSettings()
        {
            _shortcutSettings = ShortcutSettings.CreateDefault();

            if (!File.Exists(SettingsConfigPath))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(SettingsConfigPath));
                document.RootElement.TryGetProperty("copyToOtherPaneKey", out var copyProperty);
                if (copyProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.CopyToOtherPaneKey), out copyProperty);
                }

                document.RootElement.TryGetProperty("moveToOtherPaneKey", out var moveProperty);
                if (moveProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.MoveToOtherPaneKey), out moveProperty);
                }

                document.RootElement.TryGetProperty("navigateUpKey", out var navigateUpProperty);
                if (navigateUpProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.NavigateUpKey), out navigateUpProperty);
                }

                document.RootElement.TryGetProperty("createFolderKey", out var createFolderProperty);
                if (createFolderProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.CreateFolderKey), out createFolderProperty);
                }

                document.RootElement.TryGetProperty("deleteKey", out var deleteProperty);
                if (deleteProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.DeleteKey), out deleteProperty);
                }

                document.RootElement.TryGetProperty("showHiddenSystemItems", out var showHiddenSystemItemsProperty);
                if (showHiddenSystemItemsProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.ShowHiddenSystemItems), out showHiddenSystemItemsProperty);
                }

                var hiddenDrivePaths = ReadStringListSetting(
                    document.RootElement,
                    "hiddenDrivePaths",
                    nameof(ShortcutSettingsConfig.HiddenDrivePaths));
                _hiddenDrivePaths.Clear();
                foreach (var path in hiddenDrivePaths.Select(NormalizeDriveRootPath).Where(static path => !string.IsNullOrWhiteSpace(path)))
                {
                    _hiddenDrivePaths.Add(path);
                }

                document.RootElement.TryGetProperty("themeMode", out var themeModeProperty);
                if (themeModeProperty.ValueKind == JsonValueKind.Undefined)
                {
                    document.RootElement.TryGetProperty(nameof(ShortcutSettingsConfig.ThemeMode), out themeModeProperty);
                }

                var showSelectedFileSizeProperty = ReadProperty(
                    document.RootElement,
                    "showSelectedFileSize",
                    nameof(ShortcutSettingsConfig.ShowSelectedFileSize));
                var showSelectedFolderSizeProperty = ReadProperty(
                    document.RootElement,
                    "showSelectedFolderSize",
                    nameof(ShortcutSettingsConfig.ShowSelectedFolderSize));
                var defaultTerminalShellKindProperty = ReadProperty(
                    document.RootElement,
                    "defaultTerminalShellKind",
                    nameof(ShortcutSettingsConfig.DefaultTerminalShellKind));
                var defaultTerminalWorkingDirectoryModeProperty = ReadProperty(
                    document.RootElement,
                    "defaultTerminalWorkingDirectoryMode",
                    nameof(ShortcutSettingsConfig.DefaultTerminalWorkingDirectoryMode));
                var defaultTerminalCustomWorkingDirectory = ReadStringSetting(
                    document.RootElement,
                    "defaultTerminalCustomWorkingDirectory",
                    nameof(ShortcutSettingsConfig.DefaultTerminalCustomWorkingDirectory));

                _shortcutSettings = new ShortcutSettings
                {
                    CopyToOtherPaneKey = ReadShortcutKey(copyProperty, ShortcutSettings.DefaultCopyToOtherPaneKey),
                    MoveToOtherPaneKey = ReadShortcutKey(moveProperty, ShortcutSettings.DefaultMoveToOtherPaneKey),
                    NavigateUpKey = ReadShortcutKey(navigateUpProperty, ShortcutSettings.DefaultNavigateUpKey),
                    CreateFolderKey = ReadShortcutKey(createFolderProperty, ShortcutSettings.DefaultCreateFolderKey),
                    DeleteKey = ReadShortcutKey(deleteProperty, ShortcutSettings.DefaultDeleteKey),
                    ThemeMode = ReadThemeMode(themeModeProperty, ShortcutSettings.DefaultThemeMode),
                    ShowSelectedFileSize = ReadBooleanSetting(showSelectedFileSizeProperty, ShortcutSettings.DefaultShowSelectedFileSize),
                    ShowSelectedFolderSize = ReadBooleanSetting(showSelectedFolderSizeProperty, ShortcutSettings.DefaultShowSelectedFolderSize),
                    ShowHiddenSystemItems = ReadBooleanSetting(showHiddenSystemItemsProperty, ShortcutSettings.DefaultShowHiddenSystemItems),
                    DefaultTerminalShellKind = ReadEnumSetting(defaultTerminalShellKindProperty, ShortcutSettings.DefaultTerminalShellKindValue),
                    DefaultTerminalWorkingDirectoryMode = ReadEnumSetting(defaultTerminalWorkingDirectoryModeProperty, ShortcutSettings.DefaultTerminalWorkingDirectoryModeValue),
                    DefaultTerminalCustomWorkingDirectory = defaultTerminalCustomWorkingDirectory,
                };
            }
            catch
            {
                _shortcutSettings = ShortcutSettings.CreateDefault();
                _hiddenDrivePaths.Clear();
            }
        }

        private void LoadAccountSettings()
        {
            _accountSettings = AccountSettingsState.CreateDefault();

            try
            {
                if (!File.Exists(SettingsConfigPath))
                {
                    return;
                }

                var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsConfigPath), JsonOptions);
                if (settings?.Account is null)
                {
                    return;
                }

                _accountSettings = new AccountSettingsState
                {
                    ApiBaseUrl = NormalizeApiBaseUrl(settings.Account.ApiBaseUrl),
                    Email = settings.Account.Email?.Trim() ?? string.Empty,
                    Token = settings.Account.Token ?? string.Empty,
                    UserDisplayName = settings.Account.UserDisplayName ?? string.Empty,
                    ServiceAccountsSummary = settings.Account.ServiceAccountsSummary ?? string.Empty,
                    PayloadJson = settings.Account.PayloadJson ?? string.Empty,
                    ServiceAccountsJson = settings.Account.ServiceAccountsJson ?? string.Empty,
                    LastLoginText = string.IsNullOrWhiteSpace(settings.Account.LastLoginText) ? "尚未登入" : settings.Account.LastLoginText,
                    LastStatusText = string.IsNullOrWhiteSpace(settings.Account.LastStatusText)
                        ? (string.IsNullOrWhiteSpace(settings.Account.Token) ? "尚未登入" : "已載入本機登入狀態")
                        : settings.Account.LastStatusText,
                };
            }
            catch
            {
                _accountSettings = AccountSettingsState.CreateDefault();
            }
        }

        private void LoadFileBunkerSettings()
        {
            _fileBunkerSettings = FileBunkerSettingsState.CreateDefault();

            try
            {
                if (!File.Exists(SettingsConfigPath))
                {
                    return;
                }

                var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsConfigPath), JsonOptions);
                if (settings?.FileBunker is null)
                {
                    return;
                }

                _fileBunkerSettings = new FileBunkerSettingsState
                {
                    InputEndpoint = settings.FileBunker.InputEndpoint?.Trim() ?? "https://filein.filebunker.com",
                    OutputEndpointBase = settings.FileBunker.OutputEndpointBase?.Trim() ?? "https://out.filebunker.com",
                    ApiKey = settings.FileBunker.ApiKey ?? string.Empty,
                    KeyLength = settings.FileBunker.KeyLength <= 0 ? 64 : settings.FileBunker.KeyLength,
                    ClientId = settings.FileBunker.ClientId?.Trim() ?? string.Empty,
                    DaysToExpiration = settings.FileBunker.DaysToExpiration <= 0 ? 3650 : settings.FileBunker.DaysToExpiration,
                    DaysToPurge = settings.FileBunker.DaysToPurge <= 0 ? 20 : settings.FileBunker.DaysToPurge,
                };
            }
            catch
            {
                _fileBunkerSettings = FileBunkerSettingsState.CreateDefault();
            }
        }

        private void SaveCustomGroups()
        {
            Directory.CreateDirectory(ConfigDirectoryPath);
            SaveAppSettings();
        }

        private async void SaveCustomGroupsSafe()
        {
            try
            {
                SaveCustomGroups();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("儲存分組失敗", ex.Message);
            }
        }

        private void SaveShortcutSettings()
        {
            Directory.CreateDirectory(ConfigDirectoryPath);
            SaveAppSettings();
        }

        private async void SaveToolbarCommandsSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveAppSettings();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("儲存工具列失敗", ex.Message);
            }
        }

        private void LoadToolbarCommands()
        {
            ToolbarCommands.Clear();

            try
            {
                if (!File.Exists(SettingsConfigPath))
                {
                    return;
                }

                var settings = JsonSerializer.Deserialize<ShortcutSettingsConfig>(File.ReadAllText(SettingsConfigPath), JsonOptions);
                if (settings?.ToolbarCommands is null)
                {
                    return;
                }

                foreach (var config in settings.ToolbarCommands)
                {
                    if (string.IsNullOrWhiteSpace(config.Title) || string.IsNullOrWhiteSpace(config.Command))
                    {
                        continue;
                    }

                    ToolbarCommands.Add(new ToolbarCommandItem
                    {
                        Id = config.Id == Guid.Empty ? Guid.NewGuid() : config.Id,
                        Title = config.Title,
                        Command = config.Command,
                        IconPath = config.IconPath,
                        IconGlyph = config.IconGlyph,
                        NodeDockerUser = config.NodeDockerUser,
                        NodeDockerHost = config.NodeDockerHost,
                        NodeDockerRemoteDirectory = config.NodeDockerRemoteDirectory,
                        NodeDockerLaunchMode = config.NodeDockerLaunchMode,
                        TerminalShellKind = config.TerminalShellKind,
                        TerminalWorkingDirectoryMode = config.TerminalWorkingDirectoryMode,
                        TerminalCustomWorkingDirectory = config.TerminalCustomWorkingDirectory,
                        TerminalLaunchArguments = config.TerminalLaunchArguments,
                    });
                }
            }
            catch
            {
            }

        }

        private void LoadNotificationHistories()
        {
            lock (_notificationHistoryLock)
            {
                _localNotificationHistory.Clear();
                _syncNotificationHistory.Clear();
                _localNotificationHistory.AddRange(LoadNotificationHistoryFile(LocalNotificationHistoryPath, NotificationHistoryScope.LocalOnly));
                _syncNotificationHistory.AddRange(LoadNotificationHistoryFile(SyncNotificationHistoryPath, NotificationHistoryScope.Sync));
            }
        }

        private static List<NotificationHistoryRecord> LoadNotificationHistoryFile(string path, NotificationHistoryScope scope)
        {
            if (!File.Exists(path))
            {
                return new List<NotificationHistoryRecord>();
            }

            try
            {
                var records = JsonSerializer.Deserialize<List<NotificationHistoryRecord>>(File.ReadAllText(path), JsonOptions);
                if (records is null)
                {
                    return new List<NotificationHistoryRecord>();
                }

                return records
                    .Where(static record => !string.IsNullOrWhiteSpace(record.Summary))
                    .Select(record => new NotificationHistoryRecord
                    {
                        Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                        Scope = record.Scope,
                        Category = record.Category?.Trim() ?? string.Empty,
                        Summary = record.Summary?.Trim() ?? string.Empty,
                        Details = record.Details ?? string.Empty,
                        CreatedAtUtc = string.IsNullOrWhiteSpace(record.CreatedAtUtc)
                            ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                            : record.CreatedAtUtc,
                        DeviceName = record.DeviceName?.Trim() ?? string.Empty,
                    })
                    .Select(record =>
                    {
                        if (record.Scope == NotificationHistoryScope.LocalOnly && scope == NotificationHistoryScope.Sync)
                        {
                            record.Scope = NotificationHistoryScope.Sync;
                        }
                        else if (record.Scope == NotificationHistoryScope.Sync && scope == NotificationHistoryScope.LocalOnly)
                        {
                            record.Scope = NotificationHistoryScope.LocalOnly;
                        }

                        return record;
                    })
                    .Take(200)
                    .ToList();
            }
            catch
            {
                return new List<NotificationHistoryRecord>();
            }
        }

        private async Task ExecuteToolbarCommandAsync(ToolbarCommandItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Command))
            {
                await ShowMessageAsync("無法執行工具列按鈕", "這個工具列按鈕尚未設定 command。");
                return;
            }

            if (IsBuiltInToolbarCommand(item.Command))
            {
                await ExecuteBuiltInToolbarCommandAsync(item);
                return;
            }

            var currentPath = _activePane.CurrentPath?.Trim();
            if (string.IsNullOrWhiteSpace(currentPath) || !IsNavigableDirectoryPath(currentPath))
            {
                await ShowMessageAsync("無法執行工具列按鈕", "目前作用中的 Pane 路徑無效。");
                return;
            }

            var backgroundWorkId = BeginBackgroundWork($"執行 {item.Title} 中");

            try
            {
                await Task.Run(() => ExecuteToolbarCommandCore(item.Command, currentPath));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("執行工具列按鈕失敗", $"{item.Title}\n{ex.Message}");
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId);
            }
        }

        private static void ExecuteToolbarCommandCore(string command, string workingPath)
        {
            var normalizedPath = NormalizePath(workingPath);
            var shellCommand = $"/d /c pushd \"{normalizedPath}\" && {command}";

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = shellCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                throw new InvalidOperationException("無法啟動命令列處理程序。");
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"命令結束代碼 {process.ExitCode}。");
            }
        }

        private void UpdateAccountSettingsUi()
        {
            if (AccountApiUrlTextBox is null ||
                AccountEmailTextBox is null ||
                AccountPasswordBox is null ||
                LoginAccountButton is null ||
                ClearAccountSessionButton is null ||
                AccountConnectionStatusTextBlock is null ||
                AccountUserTextBlock is null ||
                AccountServiceAccountsTextBlock is null ||
                AccountLastLoginTextBlock is null ||
                AccountTokenTextBlock is null)
            {
                return;
            }

            _isUpdatingAccountUi = true;
            try
            {
                AccountApiUrlTextBox.Text = NormalizeApiBaseUrl(_accountSettings.ApiBaseUrl);
                AccountEmailTextBox.Text = _accountSettings.Email;

                AccountApiUrlTextBox.IsEnabled = !_isAccountLoginRunning;
                AccountEmailTextBox.IsEnabled = !_isAccountLoginRunning;
                AccountPasswordBox.IsEnabled = !_isAccountLoginRunning;
                LoginAccountButton.IsEnabled = !_isAccountLoginRunning;
                LoginAccountButton.Content = _isAccountLoginRunning ? "登入中..." : "登入";
                ClearAccountSessionButton.IsEnabled = !_isAccountLoginRunning;

                var connected = !string.IsNullOrWhiteSpace(_accountSettings.Token);
                AccountConnectionStatusTextBlock.Text = connected
                    ? $"已連線 · {_accountSettings.LastStatusText}"
                    : _accountSettings.LastStatusText;
                AccountUserTextBlock.Text = string.IsNullOrWhiteSpace(_accountSettings.UserDisplayName)
                    ? _accountSettings.Email
                    : _accountSettings.UserDisplayName;
                AccountServiceAccountsTextBlock.Text = string.IsNullOrWhiteSpace(_accountSettings.ServiceAccountsSummary)
                    ? "尚未取得"
                    : _accountSettings.ServiceAccountsSummary;
                AccountLastLoginTextBlock.Text = _accountSettings.LastLoginText;
                AccountTokenTextBlock.Text = MaskToken(_accountSettings.Token);
            }
            finally
            {
                _isUpdatingAccountUi = false;
            }
        }

        private static string NormalizeApiBaseUrl(string? rawValue)
        {
            var value = rawValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "https://api.nuone.cl";
            }

            return value.TrimEnd('/');
        }

        private static string BuildIdentityLoginUrl(string apiBaseUrl)
        {
            return $"{NormalizeApiBaseUrl(apiBaseUrl)}/identity/login";
        }

        private static string MaskToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "尚未登入";
            }

            var value = token.Trim();
            if (value.Length <= 20)
            {
                return value;
            }

            return $"{value[..10]} ... {value[^10..]}";
        }

        private async Task<AccountLoginResult> LoginToIdentityAsync(string apiBaseUrl, string email, string password)
        {
            var requestBody = new
            {
                email = email.Trim(),
                password,
                remember = true,
                clientApp = "nuone-tools",
                environment = "desktop",
                platform = "windows",
                host = NormalizeApiBaseUrl(apiBaseUrl),
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildIdentityLoginUrl(apiBaseUrl))
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("User-Agent", "nuone-tools/1.202606.1");
            request.Headers.TryAddWithoutValidation("x-client-app", "nuone-tools");
            request.Headers.TryAddWithoutValidation("x-environment", "desktop");

            using var response = await SharedHttpClient.SendAsync(request);
            var rawResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var apiErrorMessage = ExtractApiErrorMessage(rawResponse);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiErrorMessage)
                    ? $"登入失敗，HTTP {(int)response.StatusCode}"
                    : apiErrorMessage);
            }

            using var document = JsonDocument.Parse(rawResponse);
            var root = document.RootElement;
            var token = root.TryGetProperty("token", out var tokenProperty) && tokenProperty.ValueKind == JsonValueKind.String
                ? tokenProperty.GetString()?.Trim() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("登入成功，但 API 沒有回傳 token。");
            }

            var payloadJson = root.TryGetProperty("payload", out var payloadProperty)
                ? payloadProperty.GetRawText()
                : string.Empty;
            var serviceAccountsJson = root.TryGetProperty("serviceAccounts", out var serviceAccountsProperty)
                ? serviceAccountsProperty.GetRawText()
                : string.Empty;

            return new AccountLoginResult
            {
                Token = token,
                PayloadJson = payloadJson,
                ServiceAccountsJson = serviceAccountsJson,
                DisplayName = ExtractAccountDisplayName(root, email),
                ServiceAccountsSummary = SummarizeServiceAccounts(root),
            };
        }

        private static string ExtractApiErrorMessage(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(rawResponse);
                var root = document.RootElement;
                if (root.TryGetProperty("message", out var messageProperty) && messageProperty.ValueKind == JsonValueKind.String)
                {
                    return messageProperty.GetString()?.Trim() ?? string.Empty;
                }

                if (root.TryGetProperty("error", out var errorProperty) && errorProperty.ValueKind == JsonValueKind.String)
                {
                    return errorProperty.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch
            {
            }

            return rawResponse.Trim();
        }
        private static string ExtractAccountDisplayName(JsonElement root, string fallbackEmail)
        {
            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                if (payload.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                {
                    var name = nameProperty.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }

                if (payload.TryGetProperty("email", out var emailProperty) && emailProperty.ValueKind == JsonValueKind.String)
                {
                    var email = emailProperty.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        return email;
                    }
                }
            }

            return fallbackEmail;
        }

        private static string SummarizeServiceAccounts(JsonElement root)
        {
            if (!root.TryGetProperty("serviceAccounts", out var property) || property.ValueKind != JsonValueKind.Array)
            {
                return "0 個服務帳號";
            }

            var count = property.GetArrayLength();
            if (count == 0)
            {
                return "0 個服務帳號";
            }

            var labels = new List<string>();
            foreach (var item in property.EnumerateArray().Take(3))
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var label = string.Empty;
                    if (item.TryGetProperty("code", out var codeProperty) && codeProperty.ValueKind == JsonValueKind.String)
                    {
                        label = codeProperty.GetString()?.Trim() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(label) &&
                        item.TryGetProperty("name", out var nameProperty) &&
                        nameProperty.ValueKind == JsonValueKind.String)
                    {
                        label = nameProperty.GetString()?.Trim() ?? string.Empty;
                    }

                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        labels.Add(label);
                    }
                }
            }

            return labels.Count == 0
                ? $"{count} 個服務帳號"
                : $"{count} 個服務帳號 · {string.Join(", ", labels)}";
        }

        private void SaveAppSettings()
        {
            var settings = new ShortcutSettingsConfig
            {
                CopyToOtherPaneKey = _shortcutSettings.CopyToOtherPaneKey,
                MoveToOtherPaneKey = _shortcutSettings.MoveToOtherPaneKey,
                NavigateUpKey = _shortcutSettings.NavigateUpKey,
                CreateFolderKey = _shortcutSettings.CreateFolderKey,
                DeleteKey = _shortcutSettings.DeleteKey,
                ThemeMode = _shortcutSettings.ThemeMode,
                ShowSelectedFileSize = _shortcutSettings.ShowSelectedFileSize,
                ShowSelectedFolderSize = _shortcutSettings.ShowSelectedFolderSize,
                ShowHiddenSystemItems = _shortcutSettings.ShowHiddenSystemItems,
                DefaultTerminalShellKind = _shortcutSettings.DefaultTerminalShellKind,
                DefaultTerminalWorkingDirectoryMode = _shortcutSettings.DefaultTerminalWorkingDirectoryMode,
                DefaultTerminalCustomWorkingDirectory = _shortcutSettings.DefaultTerminalCustomWorkingDirectory,
                HiddenDrivePaths = _hiddenDrivePaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                LeftPanePath = LeftPane.CurrentPath,
                RightPanePath = RightPane.CurrentPath,
                WindowPlacement = BuildWindowPlacementConfig(),
                Account = BuildAccountSettingsConfig(),
                FileBunker = BuildFileBunkerSettingsConfig(),
                ToolbarCommands = BuildToolbarCommandConfigs(),
                BackupAutomations = BuildBackupAutomationConfigs(),
                AutoExtractProfiles = BuildAutoExtractConfigs(),
                Groups = BuildGroupConfigs(),
            };

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsConfigPath, json);
        }

        private void SaveNotificationHistories()
        {
            Directory.CreateDirectory(ConfigDirectoryPath);

            List<NotificationHistoryRecord> localRecords;
            List<NotificationHistoryRecord> syncRecords;
            lock (_notificationHistoryLock)
            {
                localRecords = _localNotificationHistory.ToList();
                syncRecords = _syncNotificationHistory.ToList();
            }

            File.WriteAllText(LocalNotificationHistoryPath, JsonSerializer.Serialize(localRecords, JsonOptions));
            File.WriteAllText(SyncNotificationHistoryPath, JsonSerializer.Serialize(syncRecords, JsonOptions));
        }

        private void SaveNotificationHistoriesSafe()
        {
            try
            {
                SaveNotificationHistories();
            }
            catch
            {
            }
        }

        private WindowPlacementConfig BuildWindowPlacementConfig()
        {
            return new WindowPlacementConfig
            {
                X = AppWindow.Position.X,
                Y = AppWindow.Position.Y,
                Width = AppWindow.Size.Width,
                Height = AppWindow.Size.Height,
            };
        }

        private AccountSettingsConfig BuildAccountSettingsConfig()
        {
            return new AccountSettingsConfig
            {
                ApiBaseUrl = NormalizeApiBaseUrl(_accountSettings.ApiBaseUrl),
                Email = _accountSettings.Email,
                Token = _accountSettings.Token,
                UserDisplayName = _accountSettings.UserDisplayName,
                ServiceAccountsSummary = _accountSettings.ServiceAccountsSummary,
                PayloadJson = _accountSettings.PayloadJson,
                ServiceAccountsJson = _accountSettings.ServiceAccountsJson,
                LastLoginText = _accountSettings.LastLoginText,
                LastStatusText = _accountSettings.LastStatusText,
            };
        }

        private FileBunkerSettingsConfig BuildFileBunkerSettingsConfig()
        {
            return new FileBunkerSettingsConfig
            {
                InputEndpoint = _fileBunkerSettings.InputEndpoint,
                OutputEndpointBase = _fileBunkerSettings.OutputEndpointBase,
                ApiKey = _fileBunkerSettings.ApiKey,
                KeyLength = Math.Max(1, _fileBunkerSettings.KeyLength),
                ClientId = _fileBunkerSettings.ClientId,
                DaysToExpiration = Math.Max(1, _fileBunkerSettings.DaysToExpiration),
                DaysToPurge = Math.Max(1, _fileBunkerSettings.DaysToPurge),
            };
        }

        private List<ToolbarCommandConfig> BuildToolbarCommandConfigs()
        {
            return ToolbarCommands
                .Select(item => new ToolbarCommandConfig
                {
                    Id = item.Id,
                    Title = item.Title,
                    Command = item.Command,
                    IconPath = item.IconPath,
                    IconGlyph = item.IconGlyph,
                    NodeDockerUser = item.NodeDockerUser,
                    NodeDockerHost = item.NodeDockerHost,
                    NodeDockerRemoteDirectory = item.NodeDockerRemoteDirectory,
                    NodeDockerLaunchMode = item.NodeDockerLaunchMode,
                    TerminalShellKind = item.TerminalShellKind,
                    TerminalWorkingDirectoryMode = item.TerminalWorkingDirectoryMode,
                    TerminalCustomWorkingDirectory = item.TerminalCustomWorkingDirectory,
                    TerminalLaunchArguments = item.TerminalLaunchArguments,
                })
                .ToList();
        }

        private List<PathGroupConfig> BuildGroupConfigs()
        {
            return CustomGroups
                .Select(group => new PathGroupConfig
                {
                    Title = group.Title,
                    Items = group.Items
                        .Select(item => new GroupedPathItemConfig
                        {
                            Title = item.Title,
                            Path = item.Path,
                        })
                        .ToList(),
                })
                .ToList();
        }

        private List<BackupAutomationConfig> BuildBackupAutomationConfigs()
        {
            return BackupAutomations
                .Select(profile => new BackupAutomationConfig
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    JobType = profile.JobType,
                    SourcePath = profile.SourcePath,
                    DestinationPath = profile.DestinationPath,
                    Mode = profile.Mode,
                    MongoToolPath = profile.MongoToolPath,
                    MongoConnectionString = profile.MongoConnectionString,
                    MongoDatabaseName = profile.MongoDatabaseName,
                    MongoUseGzip = profile.MongoUseGzip,
                    MongoUseArchive = profile.MongoUseArchive,
                    MongoRetentionCount = Math.Max(1, profile.MongoRetentionCount),
                    ScheduleType = profile.ScheduleType,
                    IntervalMinutes = Math.Max(1, profile.IntervalMinutes),
                    ScheduleTimeText = profile.ScheduleTimeText,
                    WeeklyDaysMask = profile.WeeklyDaysMask,
                    RunMissedOnStartup = profile.RunMissedOnStartup,
                    IsEnabled = profile.IsEnabled,
                    LastRunText = profile.LastRunText,
                    LastResultText = profile.LastResultText,
                })
                .ToList();
        }

        private List<AutoExtractProfileConfig> BuildAutoExtractConfigs()
        {
            return AutoExtractProfiles
                .Select(profile => new AutoExtractProfileConfig
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    WatchPath = profile.WatchPath,
                    ExtractorPath = profile.ExtractorPath,
                    ExtensionFilter = profile.ExtensionFilter,
                    Passwords = profile.Passwords
                        .Select(item => item.Value.Trim())
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.Ordinal)
                        .ToList(),
                    IsEnabled = profile.IsEnabled,
                    LastRunText = profile.LastRunText,
                    LastResultText = profile.LastResultText,
                })
                .ToList();
        }

        private async void SaveShortcutSettingsSafe()
        {
            try
            {
                SaveShortcutSettings();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("儲存設定失敗", ex.Message);
            }
        }

        private void SaveAutomationProfilesSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveAppSettings();
            }
            catch
            {
            }
        }

        private void SaveAutoExtractProfilesSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveAppSettings();
            }
            catch
            {
            }
        }


        private void SavePanePathsSafe()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                SaveAppSettings();
            }
            catch
            {
            }
        }

        private static WindowPlacementConfig? LoadSavedWindowPlacement()
        {
            try
            {
                if (!File.Exists(SettingsConfigPath))
                {
                    return null;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(SettingsConfigPath));
                var root = document.RootElement;
                var placementProperty = ReadProperty(root, "windowPlacement", nameof(ShortcutSettingsConfig.WindowPlacement));
                if (placementProperty.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var x = ReadIntSetting(placementProperty, "x", nameof(WindowPlacementConfig.X), 0);
                var y = ReadIntSetting(placementProperty, "y", nameof(WindowPlacementConfig.Y), 0);
                var width = ReadIntSetting(placementProperty, "width", nameof(WindowPlacementConfig.Width), 0);
                var height = ReadIntSetting(placementProperty, "height", nameof(WindowPlacementConfig.Height), 0);

                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                return new WindowPlacementConfig
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                };
            }
            catch
            {
                return null;
            }
        }

        private static (string LeftPath, string RightPath) LoadSavedPanePaths()
        {
            try
            {
                if (!File.Exists(SettingsConfigPath))
                {
                    return default;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(SettingsConfigPath));
                var root = document.RootElement;

                var leftPath = ReadStringSetting(root, "leftPanePath", nameof(ShortcutSettingsConfig.LeftPanePath));
                var rightPath = ReadStringSetting(root, "rightPanePath", nameof(ShortcutSettingsConfig.RightPanePath));
                return (leftPath, rightPath);
            }
            catch
            {
                return default;
            }
        }

        private static bool AddPathToGroup(PathGroup group, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsNavigableDirectoryPath(path))
            {
                return false;
            }

            if (group.Items.Any(item => PathEquals(item.Path, path)))
            {
                return false;
            }

            group.Items.Add(new GroupedPathItem
            {
                Title = GetDisplayName(path),
                Path = path,
                ParentGroup = group,
            });

            return true;
        }

        internal static string GetDisplayName(string path)
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return path;
            }

            var segments = trimmed
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                return trimmed;
            }

            return segments[^1];
        }

        private static Windows.System.VirtualKey NormalizeCapturedKey(Windows.System.VirtualKey key)
        {
            return key == Windows.System.VirtualKey.Cancel
                ? Windows.System.VirtualKey.Pause
                : key;
        }

        private static bool IsBreakAlias(Windows.System.VirtualKey configuredKey, Windows.System.VirtualKey actualKey)
        {
            return configuredKey == Windows.System.VirtualKey.Pause && actualKey == Windows.System.VirtualKey.Cancel;
        }

        private static Windows.System.VirtualKey ReadShortcutKey(JsonElement property, Windows.System.VirtualKey fallback)
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue) && Enum.IsDefined(typeof(Windows.System.VirtualKey), numericValue))
            {
                return (Windows.System.VirtualKey)numericValue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var rawValue = property.GetString();
                if (Enum.TryParse<Windows.System.VirtualKey>(rawValue, true, out var enumValue))
                {
                    return NormalizeCapturedKey(enumValue);
                }

                if (string.Equals(rawValue, "Pause / Break", StringComparison.OrdinalIgnoreCase))
                {
                    return Windows.System.VirtualKey.Pause;
                }
            }

            return fallback;
        }

        private static bool ReadBooleanSetting(JsonElement property, bool fallback)
        {
            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
                _ => fallback,
            };
        }

        private static JsonElement ReadProperty(JsonElement root, string camelCaseName, string propertyName)
        {
            if (root.TryGetProperty(camelCaseName, out var property))
            {
                return property;
            }

            if (root.TryGetProperty(propertyName, out property))
            {
                return property;
            }

            return default;
        }

        private static AppThemeMode ReadThemeMode(JsonElement property, AppThemeMode fallback)
        {
            if (property.ValueKind == JsonValueKind.String
                && Enum.TryParse<AppThemeMode>(property.GetString(), true, out var themeMode))
            {
                return themeMode;
            }

            if (property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out var numericValue)
                && Enum.IsDefined(typeof(AppThemeMode), numericValue))
            {
                return (AppThemeMode)numericValue;
            }

            return fallback;
        }

        private static TEnum ReadEnumSetting<TEnum>(JsonElement property, TEnum fallback)
            where TEnum : struct, Enum
        {
            if (property.ValueKind == JsonValueKind.String
                && Enum.TryParse<TEnum>(property.GetString(), true, out var enumValue))
            {
                return enumValue;
            }

            if (property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out var numericValue)
                && Enum.IsDefined(typeof(TEnum), numericValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
            }

            return fallback;
        }

        private static string ReadStringSetting(JsonElement root, string camelCaseName, string propertyName)
        {
            if (root.TryGetProperty(camelCaseName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static int ReadIntSetting(JsonElement root, string camelCaseName, string propertyName, int fallback)
        {
            if (root.TryGetProperty(camelCaseName, out var property))
            {
                return ReadIntValue(property, fallback);
            }

            if (root.TryGetProperty(propertyName, out property))
            {
                return ReadIntValue(property, fallback);
            }

            return fallback;
        }

        private static List<string> ReadStringListSetting(JsonElement root, string camelCaseName, string propertyName)
        {
            if (root.TryGetProperty(camelCaseName, out var property))
            {
                return ReadStringListValue(property);
            }

            if (root.TryGetProperty(propertyName, out property))
            {
                return ReadStringListValue(property);
            }

            return new List<string>();
        }

        private static List<string> ReadStringListValue(JsonElement property)
        {
            if (property.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return property.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()?.Trim() ?? string.Empty)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        private static int ReadIntValue(JsonElement property, int fallback)
        {
            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt32(out var value) => value,
                JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
                _ => fallback,
            };
        }
    }
}
