using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using L2Companion.Core;

namespace L2Companion;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
        CrashDiagnostics.WriteMarker("startup", "App started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CrashDiagnostics.WriteMarker("shutdown", $"ExitCode={e.ApplicationExitCode}");

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashDiagnostics.WriteUnhandled("dispatcher", e.Exception);
        // Let app terminate as usual; we need the crash signal for debugging.
    }

    private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        CrashDiagnostics.WriteUnhandled("appdomain", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashDiagnostics.WriteUnhandled("taskscheduler", e.Exception);
    }
}
