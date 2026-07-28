using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AutoReportWizard.Infrastructure;

namespace AutoReportWizard;

/// <summary>
/// Application entry point.
/// Initializes telemetry, handles global exceptions,
/// and performs graceful shutdown.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            TelemetryService.Initialize();

            // Capture non-UI thread exceptions.
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Capture unobserved task exceptions.
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Application startup failed: {ex}");

            MessageBox.Show(
                $"The application failed to initialize.\n\n{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    private void Application_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"UI Exception: {e.Exception}");

        MessageBox.Show(
            $"An unexpected error occurred.\n\n{e.Exception.Message}",
            "Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Debug.WriteLine($"Unhandled Exception: {ex}");
        }
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        Debug.WriteLine($"Task Exception: {e.Exception}");

        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            TelemetryService.Shutdown();
        }
        finally
        {
            base.OnExit(e);
        }
    }
}