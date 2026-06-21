using GatewayApp.Models;
using GatewayApp.Services;
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
    private bool _cleanupStarted;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        UpdateLanguageMenuChecks();
        ApplyLocalizedMappingHeaders();
    }

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = Loc.Text("MenuImportCsv"),
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
            MessageBox.Show(this, ex.Message, Loc.Text("CsvImportCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, ex.Message, Loc.Text("SettingsSaveOverwriteCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsRunning)
        {
            MessageBox.Show(this, Loc.Text("SettingsLoadRunning"), Loc.Text("SettingsLoadCaption"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Gateway settings (*.json)|*.json|All files (*.*)|*.*",
            Title = Loc.Text("SettingsLoadCaption"),
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
            MessageBox.Show(this, ex.Message, Loc.Text("SettingsLoadCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveSettingsAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Gateway settings (*.json)|*.json|All files (*.*)|*.*",
            Title = Loc.Text("MenuSaveAs"),
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
            MessageBox.Show(this, ex.Message, Loc.Text("SettingsSaveCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            var removeCount = ViewModel.CountMappingsAboveModbusLimits(dialog.Settings);
            if (removeCount > 0)
            {
                var result = MessageBox.Show(
                    this,
                    Loc.Format("ConfirmDeleteOutOfRangeMappings", removeCount),
                    Loc.Text("ModbusSettingsTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            ViewModel.ApplyModbusSettings(dialog.Settings);
        }
    }

    private void BulkAssign_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BulkAssignWindow(ViewModel.Plc)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.ApplyBulkAssign(dialog.TargetModbusType, dialog.PlcPrefix, dialog.StartNumberText, increment: 1);
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

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow
        {
            Owner = this,
        }.ShowDialog();
    }

    private void LanguageEnglish_Click(object sender, RoutedEventArgs e)
    {
        Loc.SetLanguage(UiLanguage.English);
        UpdateLanguageMenuChecks();
        ApplyLocalizedMappingHeaders();
    }

    private void LanguageJapanese_Click(object sender, RoutedEventArgs e)
    {
        Loc.SetLanguage(UiLanguage.Japanese);
        UpdateLanguageMenuChecks();
        ApplyLocalizedMappingHeaders();
    }

    private void UpdateLanguageMenuChecks()
    {
        EnglishLanguageMenuItem.IsChecked = Loc.CurrentLanguage == UiLanguage.English;
        JapaneseLanguageMenuItem.IsChecked = Loc.CurrentLanguage == UiLanguage.Japanese;
    }

    private void ApplyLocalizedMappingHeaders()
    {
        if (MappingGrid.Columns.Count < 5)
        {
            return;
        }

        MappingGrid.Columns[0].Header = Loc.Text("ColumnModbusType");
        MappingGrid.Columns[1].Header = Loc.Text("ColumnModbusAddress");
        MappingGrid.Columns[2].Header = Loc.Text("ColumnPlcAddress");
        MappingGrid.Columns[3].Header = Loc.Text("ColumnDisplayType");
        MappingGrid.Columns[4].Header = Loc.Text("ColumnComment");
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

    private void RegisterForceTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { Tag: MappingEntry entry, IsVisible: true } && entry.IsForceEditing)
            {
                entry.IsForceEditing = false;
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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsDirty)
        {
            var result = MessageBox.Show(
                this,
                Loc.Text("ConfirmExitUnsaved"),
                Loc.Text("ExitCaption"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _logWindow?.Close();
        StartCleanupAfterClose();
    }

    private void StartCleanupAfterClose()
    {
        if (_cleanupStarted)
        {
            return;
        }

        _cleanupStarted = true;
        var disposeTask = ViewModel.DisposeAsync().AsTask();
        _ = disposeTask.ContinueWith(
            task =>
            {
                var exception = task.Exception?.GetBaseException();
                if (exception is not null && !CommunicationExceptionClassifier.IsExpectedLocalStop(exception))
                {
                    App.WriteExceptionLog(exception);
                }
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
