using GatewayApp.Models;
using GatewayApp.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GatewayApp.Views.Dialogs;

public partial class CsvImportPreviewWindow : Window
{
    private readonly IReadOnlyList<CsvImportPreviewItem> _items;

    public CsvImportPreviewWindow(IReadOnlyList<CsvImportPreviewItem> items)
    {
        InitializeComponent();
        _items = items;
        PreviewGrid.ItemsSource = _items;
        ApplyLocalizedText();
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalizedText();
        PreviewGrid.Items.Refresh();
    }

    private void ApplyLocalizedText()
    {
        SummaryText.Text = Loc.Format(
            "CsvPreviewSummary",
            _items.Count(x => x.Action == CsvImportAction.Add),
            _items.Count(x => x.Action == CsvImportAction.Update),
            _items.Count(x => x.Action == CsvImportAction.Skip));

        if (PreviewGrid.Columns.Count < 6)
        {
            return;
        }

        PreviewGrid.Columns[0].Header = Loc.Text("ColumnAction");
        PreviewGrid.Columns[1].Header = "Modbus";
        PreviewGrid.Columns[2].Header = Loc.Text("ColumnName");
        PreviewGrid.Columns[3].Header = Loc.Text("ColumnDisplayType");
        PreviewGrid.Columns[4].Header = Loc.Text("ColumnExistingPlc");
        PreviewGrid.Columns[5].Header = Loc.Text("ColumnReason");
    }

    private void PreviewGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not CsvImportPreviewItem item)
        {
            return;
        }

        e.Row.Background = item.Action switch
        {
            CsvImportAction.Add => new SolidColorBrush(Color.FromRgb(0xe7, 0xf6, 0xe7)),
            CsvImportAction.Update => new SolidColorBrush(Color.FromRgb(0xff, 0xf6, 0xd7)),
            _ => new SolidColorBrush(Color.FromRgb(0xee, 0xee, 0xee)),
        };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

