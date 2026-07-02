using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AutoReportWizard.Infrastructure;

namespace AutoReportWizard
{
    /// <summary>
    /// Application entry point.
    /// Initializes OpenTelemetry tracing on startup and shuts it down cleanly on exit.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize OpenTelemetry tracing (console exporter)
            // All generation spans will be emitted here.
            TelemetryService.Initialize();
        }

        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"Unhandled exception: {e.Exception}");
            MessageBox.Show(
                $"An unexpected error occurred. The application will continue running.\n\n{e.Exception.Message}",
                "Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TelemetryService.Shutdown();
            base.OnExit(e);
        }
    }
}
