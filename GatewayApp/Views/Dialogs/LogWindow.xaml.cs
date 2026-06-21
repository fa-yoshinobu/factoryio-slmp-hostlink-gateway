using GatewayApp.ViewModels;
using System.Collections.Specialized;
using System.Windows;

namespace GatewayApp.Views.Dialogs;

public partial class LogWindow : Window
{
    private readonly MainViewModel _viewModel;

    public LogWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.Logs.CollectionChanged += Logs_CollectionChanged;
        Closed += (_, _) => _viewModel.Logs.CollectionChanged -= Logs_CollectionChanged;
    }

    private async void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string text } && !string.IsNullOrWhiteSpace(text))
        {
            await CopyToClipboardAsync(text);
        }
    }

    private async void CopyAllLogs_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, _viewModel.Logs.Select(x => x.FullText));
        if (!string.IsNullOrWhiteSpace(text))
        {
            await CopyToClipboardAsync(text);
        }
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearLogs();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task CopyToClipboardAsync(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                await Task.Delay(80);
            }
            catch (Exception ex)
            {
                _viewModel.ReportException(ex);
                return;
            }
        }
    }

    private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel.Logs.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                LogList.ScrollIntoView(_viewModel.Logs[^1]);
            }
            catch
            {
            }
        });
    }
}
