using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace nuone_tools
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private const string InstanceRegistryMutexName = @"Local\nuone-tools-instance-registry";
        private const string InstancePipeNamePrefix = "nuone-tools-instance-pipe-";
        private const string DetachedLaunchToken = "--nuone-detached-launch";
        private static readonly JsonSerializerOptions LaunchRequestJsonOptions = new(JsonSerializerDefaults.Web);
        private Window? _window;
        private MainWindow? _mainWindow;
        private string? _instancePipeName;
        private CancellationTokenSource? _singleInstanceListenerCts;
        private Task? _singleInstanceListenerTask;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            AppLogging.Configure(MainWindow.ResolveConfiguredLogDirectoryPath());
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            WindowsNotificationService.Initialize();
            var commandLineArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
            var launchWorkingDirectory = Environment.CurrentDirectory;
            var launchRequest = new LaunchRequestPayload(args.Arguments, commandLineArgs, launchWorkingDirectory);
            var shouldReuseExistingWindow = commandLineArgs.Any(static argument => string.Equals(argument, "-w", StringComparison.OrdinalIgnoreCase));
            var targetInstanceIndex = ParseTargetInstanceIndex(commandLineArgs);
            AppLogging.Information(
                "App launched RawArguments={RawArguments} CommandLineArgs={CommandLineArgs} WorkingDirectory={WorkingDirectory} ReuseExistingWindow={ReuseExistingWindow} TargetInstanceIndex={TargetInstanceIndex}",
                args.Arguments,
                commandLineArgs,
                launchWorkingDirectory,
                shouldReuseExistingWindow,
                targetInstanceIndex);

            if (shouldReuseExistingWindow && ForwardLaunchRequestToRegisteredInstance(launchRequest, targetInstanceIndex))
            {
                AppLogging.Information(
                    "Secondary launch redirected RawArguments={RawArguments} WorkingDirectory={WorkingDirectory} TargetInstanceIndex={TargetInstanceIndex}",
                    args.Arguments,
                    launchWorkingDirectory,
                    targetInstanceIndex);
                Environment.Exit(0);
                return;
            }

            if (ShouldRelaunchDetached(commandLineArgs) &&
                TryLaunchDetachedInstance(commandLineArgs, launchWorkingDirectory))
            {
                AppLogging.Information(
                    "Primary launch detached to background instance CommandLineArgs={CommandLineArgs} WorkingDirectory={WorkingDirectory}",
                    commandLineArgs,
                    launchWorkingDirectory);
                Environment.Exit(0);
                return;
            }

            _window = new MainWindow(args.Arguments, commandLineArgs, launchWorkingDirectory);
            _mainWindow = (MainWindow)_window;
            RegisterCurrentInstance();
            _window.Closed += MainWindow_Closed;
            StartSingleInstanceListener();
            _window.Activate();
        }

        private static void CurrentDomain_ProcessExit(object? sender, EventArgs e)
        {
            UnregisterCurrentProcessInstance();
            WindowsNotificationService.Uninitialize();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_window is not null)
            {
                _window.Closed -= MainWindow_Closed;
            }

            StopSingleInstanceListener();
            _mainWindow = null;
            _window = null;
            UnregisterCurrentProcessInstance();
            AppLogging.Information("App main window closed. Exiting process.");
            Environment.Exit(0);
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            AppLogging.Error(e.Exception, "App.UnhandledException Message={Message}", e.Message);
            SafeAppendCrashLog($"App.UnhandledException message={e.Message} exception={e.Exception}");
            e.Handled = true;
        }

        private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            AppLogging.Error(e.ExceptionObject as Exception, "AppDomain.UnhandledException IsTerminating={IsTerminating} ExceptionObject={ExceptionObject}", e.IsTerminating, e.ExceptionObject);
            SafeAppendCrashLog($"AppDomain.UnhandledException isTerminating={e.IsTerminating} exception={e.ExceptionObject}");
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLogging.Error(e.Exception, "TaskScheduler.UnobservedTaskException Observed={Observed}", e.Observed);
            SafeAppendCrashLog($"TaskScheduler.UnobservedTaskException observed={e.Observed} exception={e.Exception}");
            e.SetObserved();
        }

        private static void SafeAppendCrashLog(string message)
        {
            try
            {
                MainWindow.AppendDebugLog("crash-debug.log", message);
            }
            catch
            {
            }
        }

        private void StartSingleInstanceListener()
        {
            if (_singleInstanceListenerCts is not null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_instancePipeName))
            {
                _instancePipeName = BuildInstancePipeName(Environment.ProcessId);
            }

            _singleInstanceListenerCts = new CancellationTokenSource();
            _singleInstanceListenerTask = Task.Run(() => ListenForSecondaryLaunchesAsync(_singleInstanceListenerCts.Token));
        }

        private void StopSingleInstanceListener()
        {
            var cts = _singleInstanceListenerCts;
            _singleInstanceListenerCts = null;
            if (cts is null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            cts.Dispose();
            _singleInstanceListenerTask = null;
        }

        private async Task ListenForSecondaryLaunchesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var pipeName = _instancePipeName;
                    if (string.IsNullOrWhiteSpace(pipeName))
                    {
                        break;
                    }

                    await using var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cancellationToken);
                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    var json = await reader.ReadToEndAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        continue;
                    }

                    var payload = JsonSerializer.Deserialize<LaunchRequestPayload>(json, LaunchRequestJsonOptions);
                    if (payload is null)
                    {
                        continue;
                    }

                    AppLogging.Information(
                        "Received secondary launch request RawArguments={RawArguments} CommandLineArgs={CommandLineArgs} WorkingDirectory={WorkingDirectory}",
                        payload.RawArguments,
                        payload.CommandLineArgs,
                        payload.WorkingDirectory);
                    _mainWindow?.HandleExternalLaunchRequest(payload.RawArguments, payload.CommandLineArgs, payload.WorkingDirectory);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogging.Error(ex, "Single-instance listener failed.");
                    try
                    {
                        await Task.Delay(500, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private static bool ForwardLaunchRequestToRegisteredInstance(LaunchRequestPayload payload, int? targetInstanceIndex)
        {
            var json = JsonSerializer.Serialize(payload, LaunchRequestJsonOptions);
            var registrations = LoadRegisteredInstances(cleanupStaleEntries: true);
            if (registrations.Count == 0)
            {
                return false;
            }

            var selectedRegistration = SelectTargetRegistration(registrations, targetInstanceIndex);
            if (selectedRegistration is null)
            {
                return false;
            }

            for (var attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", selectedRegistration.PipeName, PipeDirection.Out, PipeOptions.None);
                    client.Connect(250);
                    using var writer = new StreamWriter(client, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
                    writer.Write(json);
                    writer.Flush();
                    return true;
                }
                catch (Exception ex) when (attempt < 10)
                {
                    AppLogging.Warning(
                        "Forward launch request retry Attempt={Attempt} RawArguments={RawArguments} TargetProcessId={TargetProcessId} Error={Error}",
                        attempt,
                        payload.RawArguments,
                        selectedRegistration.ProcessId,
                        ex.Message);
                    Thread.Sleep(150);
                }
                catch (Exception ex)
                {
                    AppLogging.Error(
                        ex,
                        "Failed to forward launch request to registered instance TargetProcessId={TargetProcessId} PipeName={PipeName}",
                        selectedRegistration.ProcessId,
                        selectedRegistration.PipeName);
                    RemoveRegisteredInstance(selectedRegistration.ProcessId);
                    return false;
                }
            }

            return false;
        }

        private static RegisteredInstanceEntry? SelectTargetRegistration(IReadOnlyList<RegisteredInstanceEntry> registrations, int? targetInstanceIndex)
        {
            if (registrations.Count == 0)
            {
                return null;
            }

            if (targetInstanceIndex.HasValue)
            {
                var zeroBasedIndex = Math.Max(targetInstanceIndex.Value - 1, 0);
                if (zeroBasedIndex < registrations.Count)
                {
                    return registrations[zeroBasedIndex];
                }

                AppLogging.Warning(
                    "Requested target instance is out of range RequestedIndex={RequestedIndex} AvailableCount={AvailableCount}. Falling back to first instance.",
                    targetInstanceIndex.Value,
                    registrations.Count);
            }

            return registrations[0];
        }

        private static bool ShouldRelaunchDetached(string[] commandLineArgs)
        {
            if (commandLineArgs.Any(static arg => string.Equals(arg, DetachedLaunchToken, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return IsLikelyTerminalLaunch();
        }

        private static bool TryLaunchDetachedInstance(string[] commandLineArgs, string workingDirectory)
        {
            try
            {
                var executablePath = GetCurrentExecutablePath();
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    AppLogging.Warning("Detached launch skipped because executable path is unavailable. Path={ExecutablePath}", executablePath);
                    return false;
                }

                var arguments = commandLineArgs
                    .Concat(new[] { DetachedLaunchToken })
                    .Where(static arg => !string.IsNullOrWhiteSpace(arg))
                    .Select(QuoteArgument);

                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = string.Join(" ", arguments),
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? AppContext.BaseDirectory : workingDirectory,
                    UseShellExecute = true,
                });
                return true;
            }
            catch (Exception ex)
            {
                AppLogging.Error(ex, "Detached launch failed.");
                return false;
            }
        }

        private static bool IsLikelyTerminalLaunch()
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSYSTEM")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WT_SESSION")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TERM")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TERM_PROGRAM"));
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

            return System.IO.Path.Combine(AppContext.BaseDirectory, "nuone-tools.exe");
        }

        private void RegisterCurrentInstance()
        {
            _instancePipeName = BuildInstancePipeName(Environment.ProcessId);
            var registryEntries = LoadRegisteredInstances(cleanupStaleEntries: true)
                .Where(entry => entry.ProcessId != Environment.ProcessId)
                .ToList();
            registryEntries.Add(new RegisteredInstanceEntry(
                Environment.ProcessId,
                _instancePipeName,
                DateTimeOffset.UtcNow));
            SaveRegisteredInstances(registryEntries);
            AppLogging.Information(
                "Registered app instance ProcessId={ProcessId} PipeName={PipeName} RegisteredCount={RegisteredCount}",
                Environment.ProcessId,
                _instancePipeName,
                registryEntries.Count);
        }

        private static void UnregisterCurrentProcessInstance()
        {
            RemoveRegisteredInstance(Environment.ProcessId);
        }

        private static void RemoveRegisteredInstance(int processId)
        {
            var registryEntries = LoadRegisteredInstances(cleanupStaleEntries: false)
                .Where(entry => entry.ProcessId != processId)
                .ToList();
            SaveRegisteredInstances(registryEntries);
        }

        private static List<RegisteredInstanceEntry> LoadRegisteredInstances(bool cleanupStaleEntries)
        {
            return WithInstanceRegistryLock(() =>
            {
                var entries = ReadRegisteredInstancesUnsafe();
                if (cleanupStaleEntries)
                {
                    entries = entries
                        .Where(static entry => IsProcessAlive(entry.ProcessId))
                        .OrderBy(static entry => entry.RegisteredAtUtc)
                        .ThenBy(static entry => entry.ProcessId)
                        .ToList();
                    WriteRegisteredInstancesUnsafe(entries);
                }

                return entries;
            });
        }

        private static void SaveRegisteredInstances(IReadOnlyCollection<RegisteredInstanceEntry> entries)
        {
            WithInstanceRegistryLock(() =>
            {
                WriteRegisteredInstancesUnsafe(entries);
                return 0;
            });
        }

        private static T WithInstanceRegistryLock<T>(Func<T> action)
        {
            using var mutex = new Mutex(initiallyOwned: false, InstanceRegistryMutexName);
            mutex.WaitOne();
            try
            {
                return action();
            }
            finally
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch
                {
                }
            }
        }

        private static List<RegisteredInstanceEntry> ReadRegisteredInstancesUnsafe()
        {
            try
            {
                var registryPath = GetInstanceRegistryPath();
                if (!File.Exists(registryPath))
                {
                    return new List<RegisteredInstanceEntry>();
                }

                var json = File.ReadAllText(registryPath);
                return JsonSerializer.Deserialize<List<RegisteredInstanceEntry>>(json, LaunchRequestJsonOptions) ?? new List<RegisteredInstanceEntry>();
            }
            catch (Exception ex)
            {
                AppLogging.Warning("Failed to read instance registry Error={Error}", ex.Message);
                return new List<RegisteredInstanceEntry>();
            }
        }

        private static void WriteRegisteredInstancesUnsafe(IReadOnlyCollection<RegisteredInstanceEntry> entries)
        {
            try
            {
                var registryPath = GetInstanceRegistryPath();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(registryPath)!);
                var json = JsonSerializer.Serialize(entries, LaunchRequestJsonOptions);
                File.WriteAllText(registryPath, json);
            }
            catch (Exception ex)
            {
                AppLogging.Warning("Failed to write instance registry Error={Error}", ex.Message);
            }
        }

        private static string GetInstanceRegistryPath()
        {
            var configDirectoryPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "nuone-tools",
                "config");
            return System.IO.Path.Combine(configDirectoryPath, "instance-registry.json");
        }

        private static string BuildInstancePipeName(int processId)
        {
            return $"{InstancePipeNamePrefix}{processId}";
        }

        private static bool IsProcessAlive(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static int? ParseTargetInstanceIndex(IEnumerable<string> commandLineArgs)
        {
            var args = commandLineArgs.ToArray();
            for (var index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], "-it", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(args[index], "--instance-target", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (index + 1 < args.Length &&
                    int.TryParse(args[index + 1], out var parsedValue) &&
                    parsedValue > 0)
                {
                    return parsedValue;
                }
            }

            return null;
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return "\"\"";
            }

            return argument.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
                ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                : argument;
        }

        private sealed record LaunchRequestPayload(string? RawArguments, string[] CommandLineArgs, string? WorkingDirectory);
        private sealed record RegisteredInstanceEntry(int ProcessId, string PipeName, DateTimeOffset RegisteredAtUtc);
    }
}
