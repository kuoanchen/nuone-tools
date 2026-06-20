using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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
        private Window? _window;

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
            _window = new MainWindow();
            _window.Activate();
        }

        private static void CurrentDomain_ProcessExit(object? sender, EventArgs e)
        {
            WindowsNotificationService.Uninitialize();
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
    }
}
