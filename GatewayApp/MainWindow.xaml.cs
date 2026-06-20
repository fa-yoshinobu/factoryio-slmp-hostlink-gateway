using GatewayApp.Models;
using GatewayApp.ViewModels;
using GatewayApp.Views.Dialogs;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace GatewayApp;

public partial class MainWindow : Window
{
    private LogWindow? _logWindow;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
    }

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Factory I/O タグ CSV インポート",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var preview = ViewModel.PreviewCsvImport(dialog.FileName);
            var previewWindow = new CsvImportPreviewWindow(preview)
            {
                Owner = this,
            };

            if (previewWindow.ShowDialog() == true)
            {
                ViewModel.ApplyCsvImport(preview);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CSV インポート", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.CurrentSettingsPath))
        {
            SaveSettingsAs_Click(sender, e);
            return;
        }

        try
        {
            ViewModel.SaveSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "上書き保存", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsRunning)
        {
            MessageBox.Show(this, "稼働中は設定を読み込めません。停止してから読み込んでください。", "設定読み込み", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Gateway settings (*.json)|*.json|All files (*.*)|*.*",
            Title = "設定読み込み",
            InitialDirectory = Directory.Exists(ViewModel.SettingsDialogDirectory) ? ViewModel.SettingsDialogDirectory : Environment.CurrentDirectory,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ViewModel.LoadSettingsFromFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "設定読み込み", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveSettingsAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Gateway settings (*.json)|*.json|All files (*.*)|*.*",
            Title = "名前を付けて設定保存",
            AddExtension = true,
            DefaultExt = ".json",
            FileName = string.IsNullOrWhiteSpace(ViewModel.CurrentSettingsPath) ? "settings.json" : Path.GetFileName(ViewModel.CurrentSettingsPath),
            InitialDirectory = Directory.Exists(ViewModel.SettingsDialogDirectory) ? ViewModel.SettingsDialogDirectory : Environment.CurrentDirectory,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ViewModel.SaveSettingsAs(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "設定保存", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PlcSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PlcSettingsWindow(ViewModel.Plc.Clone(), ViewModel.IsRunning)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.ApplyPlcSettings(dialog.Settings);
        }
    }

    private void ModbusSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ModbusSettingsWindow(ViewModel.Modbus.Clone(), ViewModel.IsRunning)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.ApplyModbusSettings(dialog.Settings);
        }
    }

    private void BulkAssign_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BulkAssignWindow
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.ApplyBulkAssign(dialog.TargetModbusType, dialog.PlcPrefix, dialog.StartNumber, dialog.Increment);
        }
    }

    private void ShowLog_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow is { IsVisible: true })
        {
            _logWindow.Activate();
            return;
        }

        _logWindow = new LogWindow(ViewModel)
        {
            Owner = this,
        };
        _logWindow.Closed += (_, _) => _logWindow = null;
        _logWindow.Show();
    }

    private async void Led_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { Tag: MappingEntry entry })
            {
                await ViewModel.CycleBoolForceCommand.ExecuteAsync(entry);
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportException(ex);
        }
    }

    private void RegisterValue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MappingEntry entry })
        {
            ViewModel.BeginRegisterEdit(entry);
        }
    }

    private async void RegisterForceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: MappingEntry entry })
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ViewModel.CommitRegisterForceAsync(entry, clear: false);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                await ViewModel.CommitRegisterForceAsync(entry, clear: true);
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportException(ex);
        }
    }

    private async void RegisterForceTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { Tag: MappingEntry entry, IsVisible: true } && entry.IsForceEditing)
            {
                await ViewModel.CommitRegisterForceAsync(entry, clear: false);
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportException(ex);
        }
    }

    private void RegisterForceTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.IsVisible)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void MappingGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column.SortMemberPath is not ("ModbusType" or "ModbusAddress"))
        {
            return;
        }

        e.Handled = true;

        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var column in MappingGrid.Columns)
        {
            column.SortDirection = null;
        }

        e.Column.SortDirection = direction;

        var view = CollectionViewSource.GetDefaultView(MappingGrid.ItemsSource);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            if (e.Column.SortMemberPath == "ModbusType")
            {
                view.SortDescriptions.Add(new SortDescription(nameof(MappingEntry.ModbusType), direction));
                view.SortDescriptions.Add(new SortDescription(nameof(MappingEntry.ModbusAddress), ListSortDirection.Ascending));
            }
            else
            {
                view.SortDescriptions.Add(new SortDescription(nameof(MappingEntry.ModbusAddress), direction));
                view.SortDescriptions.Add(new SortDescription(nameof(MappingEntry.ModbusType), ListSortDirection.Ascending));
            }
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _logWindow?.Close();
        await ViewModel.DisposeAsync();
    }
}
