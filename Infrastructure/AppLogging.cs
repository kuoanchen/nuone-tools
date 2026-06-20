using System;
using System.IO;
using System.Linq;
using Serilog;

namespace nuone_tools
{
    internal static class AppLogging
    {
        private static readonly object SyncLock = new();
        private static string _configuredDirectoryPath = string.Empty;
        private static readonly string[] LegacyLogFilePatterns =
        {
            "*-debug.log",
            "nuone-tools-error-*.log",
        };

        internal static string ConfiguredDirectoryPath
        {
            get
            {
                lock (SyncLock)
                {
                    return _configuredDirectoryPath;
                }
            }
        }

        internal static string CurrentLogFilePath
        {
            get
            {
                lock (SyncLock)
                {
                    var directoryPath = string.IsNullOrWhiteSpace(_configuredDirectoryPath)
                        ? MainWindow.DefaultLogDirectoryPath
                        : _configuredDirectoryPath;
                    return Path.Combine(directoryPath, $"nuone-tools-{DateTime.Now.ToString("yyyyMMdd")}.log");
                }
            }
        }

        internal static void Configure(string? logDirectoryPath)
        {
            var normalizedDirectoryPath = MainWindow.NormalizeLogDirectoryPath(logDirectoryPath);

            lock (SyncLock)
            {
                if (!string.IsNullOrWhiteSpace(_configuredDirectoryPath) &&
                    string.Equals(_configuredDirectoryPath, normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Directory.CreateDirectory(normalizedDirectoryPath);
                DeleteLegacyLogFiles(normalizedDirectoryPath);

                var outputTemplate =
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj} " +
                    "{Properties:j}{NewLine}{Exception}";

                var logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .Enrich.WithProperty("App", "nuone-tools")
                    .WriteTo.File(
                        Path.Combine(normalizedDirectoryPath, "nuone-tools-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        shared: true,
                        outputTemplate: outputTemplate)
                    .CreateLogger();

                var previousLogger = Log.Logger;
                Log.Logger = logger;
                _configuredDirectoryPath = normalizedDirectoryPath;

                try
                {
                    (previousLogger as IDisposable)?.Dispose();
                }
                catch
                {
                }

                Log.Information("Serilog configured. Directory={LogDirectory}", normalizedDirectoryPath);
            }
        }

        internal static void Debug(string messageTemplate, params object?[] propertyValues)
        {
            Log.Debug(messageTemplate, propertyValues);
        }

        internal static void Information(string messageTemplate, params object?[] propertyValues)
        {
            Log.Information(messageTemplate, propertyValues);
        }

        internal static void Warning(string messageTemplate, params object?[] propertyValues)
        {
            Log.Warning(messageTemplate, propertyValues);
        }

        internal static void Error(Exception? exception, string messageTemplate, params object?[] propertyValues)
        {
            if (exception is null)
            {
                Log.Error(messageTemplate, propertyValues);
                return;
            }

            Log.Error(exception, messageTemplate, propertyValues);
        }

        internal static void Flush()
        {
            try
            {
                Log.CloseAndFlush();
            }
            catch
            {
            }
        }

        private static void DeleteLegacyLogFiles(string logDirectoryPath)
        {
            try
            {
                foreach (var pattern in LegacyLogFilePatterns)
                {
                    foreach (var filePath in Directory.EnumerateFiles(logDirectoryPath, pattern, SearchOption.TopDirectoryOnly).ToArray())
                    {
                        try
                        {
                            File.Delete(filePath);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
