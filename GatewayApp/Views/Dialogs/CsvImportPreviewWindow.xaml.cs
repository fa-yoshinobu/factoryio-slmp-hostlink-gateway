using GatewayApp.Models;
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
        SummaryText.Text = $"追加 {_items.Count(x => x.Action == CsvImportAction.Add)} / 更新 {_items.Count(x => x.Action == CsvImportAction.Update)} / スキップ {_items.Count(x => x.Action == CsvImportAction.Skip)}";
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

