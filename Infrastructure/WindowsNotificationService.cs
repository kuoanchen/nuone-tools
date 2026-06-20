using System;
using System.IO;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace nuone_tools
{
    internal static class WindowsNotificationService
    {
        private static readonly object SyncLock = new();
        private static bool _isInitialized;

        internal static void Initialize()
        {
            lock (SyncLock)
            {
                if (_isInitialized)
                {
                    return;
                }

                try
                {
                    var isSupported = AppNotificationManager.IsSupported();
                    AppLogging.Information("Windows notification support check IsSupported={IsSupported}", isSupported);

                    AppNotificationManager.Default.Register("Nuone Tools", BuildNotificationIconUri());
                    _isInitialized = true;
                    AppLogging.Information("Windows notification service initialized");
                }
                catch (Exception ex)
                {
                    AppLogging.Error(
                        ex,
                        "Windows notification service initialization failed Type={ExceptionType} HResult=0x{HResult:X8}",
                        ex.GetType().FullName ?? ex.GetType().Name,
                        ex.HResult);
                }
            }
        }

        internal static void Uninitialize()
        {
            lock (SyncLock)
            {
                if (!_isInitialized)
                {
                    return;
                }

                try
                {
                    AppNotificationManager.Default.Unregister();
                    AppLogging.Information("Windows notification service unregistered");
                }
                catch (Exception ex)
                {
                    AppLogging.Error(ex, "Windows notification service unregister failed");
                }
                finally
                {
                    _isInitialized = false;
                }
            }
        }

        private static Uri BuildNotificationIconUri()
        {
            foreach (var baseDirectory in EnumerateCandidateBaseDirectories())
            {
                var squareLogoPath = Path.Combine(baseDirectory, "Assets", "Square44x44Logo.scale-200.png");
                if (File.Exists(squareLogoPath))
                {
                    AppLogging.Information("Windows notification icon path resolved Path={IconPath}", squareLogoPath);
                    return new Uri(squareLogoPath, UriKind.Absolute);
                }

                var appIconPath = Path.Combine(baseDirectory, "Assets", "AppIcon.ico");
                if (File.Exists(appIconPath))
                {
                    AppLogging.Information("Windows notification icon path resolved Path={IconPath}", appIconPath);
                    return new Uri(appIconPath, UriKind.Absolute);
                }
            }

            throw new FileNotFoundException(
                $"No usable notification icon was found for Windows notifications. BaseDirectory={AppContext.BaseDirectory}");
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateCandidateBaseDirectories()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = AppContext.BaseDirectory;

            for (var depth = 0; depth < 4 && !string.IsNullOrWhiteSpace(current); depth++)
            {
                var normalized = Path.GetFullPath(current);
                if (seen.Add(normalized))
                {
                    yield return normalized;
                }

                var parent = Directory.GetParent(normalized);
                if (parent is null)
                {
                    yield break;
                }

                current = parent.FullName;
            }
        }

        internal static void Show(NotificationHistoryRecord record)
        {
            if (!_isInitialized)
            {
                AppLogging.Warning(
                    "Windows notification show skipped because service is not initialized Category={Category} Summary={Summary}",
                    record.Category,
                    record.Summary);
                return;
            }

            try
            {
                var builder = new AppNotificationBuilder()
                    .AddText(BuildTitle(record))
                    .AddText(record.Summary);

                var detail = BuildDetail(record);
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    builder.AddText(detail);
                }

                AppNotificationManager.Default.Show(builder.BuildNotification());
                AppLogging.Information(
                    "Windows notification show submitted Category={Category} Summary={Summary}",
                    record.Category,
                    record.Summary);
            }
            catch (Exception ex)
            {
                AppLogging.Error(
                    ex,
                    "Windows notification show failed Category={Category} Summary={Summary} Type={ExceptionType} HResult=0x{HResult:X8}",
                    record.Category,
                    record.Summary,
                    ex.GetType().FullName ?? ex.GetType().Name,
                    ex.HResult);
            }
        }

        private static string BuildTitle(NotificationHistoryRecord record)
        {
            var scope = record.Scope == NotificationHistoryScope.Sync ? "同步" : "本機";
            var category = string.IsNullOrWhiteSpace(record.Category) ? "通知" : record.Category.Trim();
            return $"Nuone Tools · {scope} · {category}";
        }

        private static string BuildDetail(NotificationHistoryRecord record)
        {
            var detail = string.IsNullOrWhiteSpace(record.Details)
                ? string.Empty
                : record.Details.Trim();

            if (string.Equals(detail, record.Summary?.Trim(), StringComparison.Ordinal))
            {
                detail = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(record.DeviceName))
            {
                detail = string.IsNullOrWhiteSpace(detail)
                    ? $"裝置：{record.DeviceName}"
                    : $"裝置：{record.DeviceName}\n{detail}";
            }

            if (detail.Length <= 240)
            {
                return detail;
            }

            return $"{detail[..240]}...";
        }
    }
}
