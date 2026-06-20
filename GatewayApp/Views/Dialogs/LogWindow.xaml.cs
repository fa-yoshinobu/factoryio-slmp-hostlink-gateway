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
        _viewModel.ErrorLogs.CollectionChanged += ErrorLogs_CollectionChanged;
        Closed += (_, _) => _viewModel.ErrorLogs.CollectionChanged -= ErrorLogs_CollectionChanged;
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
        var text = string.Join(Environment.NewLine, _viewModel.ErrorLogs.Select(x => x.FullText));
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
            catch (Exception ex) when (attempt < 4)
            {
                await Task.Delay(80);
                if (attempt == 3)
                {
                    _viewModel.ReportException(ex);
                }
            }
            catch (Exception ex)
            {
                _viewModel.ReportException(ex);
                return;
            }
        }
    }

    private void ErrorLogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel.ErrorLogs.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                LogList.ScrollIntoView(_viewModel.ErrorLogs[^1]);
            }
            catch
            {
            }
        });
    }
}
