using GatewayApp.ViewModels;
using GatewayApp.Services;
using System.Windows;
using System.Windows.Threading;

namespace GatewayApp;

public partial class App : Application
{
    private static readonly LogFileService LogFileService = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (CommunicationExceptionClassifier.IsExpectedLocalStop(e.Exception))
        {
            e.Handled = true;
            return;
        }

        ReportException(e.Exception);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            if (CommunicationExceptionClassifier.IsExpectedLocalStop(exception))
            {
                return;
            }

            WriteExceptionLog(exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (CommunicationExceptionClassifier.IsExpectedLocalStop(e.Exception))
        {
            e.SetObserved();
            return;
        }

        ReportException(e.Exception);
        e.SetObserved();
    }

    private static void ReportException(Exception exception)
    {
        WriteExceptionLog(exception);

        var dispatcher = Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ReportExceptionOnUi(exception);
            return;
        }

        dispatcher.BeginInvoke(() => ReportExceptionOnUi(exception));
    }

    private static void ReportExceptionOnUi(Exception exception)
    {
        try
        {
            if (Current?.MainWindow?.DataContext is MainViewModel viewModel)
            {
                viewModel.ReportException(exception);
            }
        }
        catch
        {
        }
    }

    private static void WriteExceptionLog(Exception exception)
    {
        try
        {
            LogFileService.WriteExceptionLog(exception);
        }
        catch
        {
        }
    }
}
