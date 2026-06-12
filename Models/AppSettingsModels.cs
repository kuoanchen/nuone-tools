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
    public enum AppSection
    {
        FileManager,
        Automation,
        Settings,
    }

    public enum SettingsSection
    {
        General,
        Account,
        Appearance,
        Shortcuts,
        Toolbar,
        AutoExtract,
    }

    public enum ShortcutCaptureTarget
    {
        None,
        CopyToOtherPane,
        MoveToOtherPane,
        NavigateUp,
        CreateFolder,
        Delete,
    }

    public enum AppThemeMode
    {
        System,
        Dark,
        Light,
    }

    public enum BackupAutomationMode
    {
        Copy,
        Mirror,
    }

    public sealed class ShortcutSettings
    {
        public const Windows.System.VirtualKey DefaultCopyToOtherPaneKey = Windows.System.VirtualKey.F5;
        public const Windows.System.VirtualKey DefaultMoveToOtherPaneKey = Windows.System.VirtualKey.F6;
        public const Windows.System.VirtualKey DefaultNavigateUpKey = Windows.System.VirtualKey.Pause;
        public const Windows.System.VirtualKey DefaultCreateFolderKey = Windows.System.VirtualKey.N;
        public const Windows.System.VirtualKey DefaultDeleteKey = Windows.System.VirtualKey.Delete;
        public const AppThemeMode DefaultThemeMode = AppThemeMode.System;
        public const bool DefaultShowSelectedFileSize = true;
        public const bool DefaultShowSelectedFolderSize = true;
        public const bool DefaultShowHiddenSystemItems = false;

        public Windows.System.VirtualKey CopyToOtherPaneKey { get; set; } = DefaultCopyToOtherPaneKey;

        public Windows.System.VirtualKey MoveToOtherPaneKey { get; set; } = DefaultMoveToOtherPaneKey;

        public Windows.System.VirtualKey NavigateUpKey { get; set; } = DefaultNavigateUpKey;

        public Windows.System.VirtualKey CreateFolderKey { get; set; } = DefaultCreateFolderKey;

        public Windows.System.VirtualKey DeleteKey { get; set; } = DefaultDeleteKey;

        public AppThemeMode ThemeMode { get; set; } = DefaultThemeMode;

        public bool ShowSelectedFileSize { get; set; } = DefaultShowSelectedFileSize;

        public bool ShowSelectedFolderSize { get; set; } = DefaultShowSelectedFolderSize;

        public bool ShowHiddenSystemItems { get; set; } = DefaultShowHiddenSystemItems;

        public static ShortcutSettings CreateDefault()
        {
            return new ShortcutSettings();
        }
    }

    public sealed class ShortcutSettingsConfig
    {
        public Windows.System.VirtualKey CopyToOtherPaneKey { get; set; } = ShortcutSettings.DefaultCopyToOtherPaneKey;

        public Windows.System.VirtualKey MoveToOtherPaneKey { get; set; } = ShortcutSettings.DefaultMoveToOtherPaneKey;

        public Windows.System.VirtualKey NavigateUpKey { get; set; } = ShortcutSettings.DefaultNavigateUpKey;

        public Windows.System.VirtualKey CreateFolderKey { get; set; } = ShortcutSettings.DefaultCreateFolderKey;

        public Windows.System.VirtualKey DeleteKey { get; set; } = ShortcutSettings.DefaultDeleteKey;

        public AppThemeMode ThemeMode { get; set; } = ShortcutSettings.DefaultThemeMode;

        public bool ShowSelectedFileSize { get; set; } = ShortcutSettings.DefaultShowSelectedFileSize;

        public bool ShowSelectedFolderSize { get; set; } = ShortcutSettings.DefaultShowSelectedFolderSize;

        public bool ShowHiddenSystemItems { get; set; } = ShortcutSettings.DefaultShowHiddenSystemItems;

        public List<string> HiddenDrivePaths { get; set; } = new();

        public string LeftPanePath { get; set; } = string.Empty;

        public string RightPanePath { get; set; } = string.Empty;

        public WindowPlacementConfig? WindowPlacement { get; set; }

        public AccountSettingsConfig Account { get; set; } = new();

        public List<ToolbarCommandConfig> ToolbarCommands { get; set; } = new();

        public List<BackupAutomationConfig> BackupAutomations { get; set; } = new();

        public List<AutoExtractProfileConfig> AutoExtractProfiles { get; set; } = new();

        public List<PathGroupConfig> Groups { get; set; } = new();
    }

    public sealed class AccountSettingsConfig
    {
        public string ApiBaseUrl { get; set; } = "https://api.nuone.cl";

        public string Email { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        public string UserDisplayName { get; set; } = string.Empty;

        public string ServiceAccountsSummary { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = string.Empty;

        public string ServiceAccountsJson { get; set; } = string.Empty;

        public string LastLoginText { get; set; } = "尚未登入";

        public string LastStatusText { get; set; } = "尚未登入";
    }

    public sealed class ToolbarCommandConfig
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public string IconPath { get; set; } = string.Empty;

        public string IconGlyph { get; set; } = string.Empty;
    }

    public sealed class WindowPlacementConfig
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }

    public sealed class BackupAutomationConfig
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string SourcePath { get; set; } = string.Empty;

        public string DestinationPath { get; set; } = string.Empty;

        public BackupAutomationMode Mode { get; set; } = BackupAutomationMode.Copy;

        public int IntervalMinutes { get; set; } = 60;

        public bool IsEnabled { get; set; } = true;

        public string LastRunText { get; set; } = "尚未執行";

        public string LastResultText { get; set; } = "等待排程";
    }

    public sealed class AutoExtractProfileConfig
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string WatchPath { get; set; } = string.Empty;

        public string ExtractorPath { get; set; } = string.Empty;

        public List<string> Passwords { get; set; } = new();

        public string PasswordFilePath { get; set; } = string.Empty;

        public string PasswordListText { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        public string LastRunText { get; set; } = "尚未執行";

        public string LastResultText { get; set; } = "監看待命";
    }
}
