using GatewayApp.ViewModels;
using GatewayApp.Services;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GatewayApp;

public partial class App : Application
{
    private const long MaxErrorLogBytes = 1_000_000;

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
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FactoryIOGateway");
            Directory.CreateDirectory(directory);

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {exception}\r\n";
            var path = Path.Combine(directory, "error.log");
            RotateErrorLogIfNeeded(path);
            File.AppendAllText(path, line);
        }
        catch
        {
        }
    }

    private static void RotateErrorLogIfNeeded(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length <= MaxErrorLogBytes)
        {
            return;
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} error.log rotated because it exceeded {MaxErrorLogBytes} bytes.");
    }
}
