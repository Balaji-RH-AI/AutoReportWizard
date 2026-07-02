using System.Windows;
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

            this.DispatcherUnhandledException += (s, args) =>
            {
                System.IO.File.WriteAllText("crash.log", args.Exception.ToString());
            };

            // Initialize OpenTelemetry tracing (console exporter)
            // All generation spans will be emitted here.
            TelemetryService.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TelemetryService.Shutdown();
            base.OnExit(e);
        }
    }
}
