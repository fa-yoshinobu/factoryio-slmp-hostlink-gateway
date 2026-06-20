using GatewayApp.Models;
using System.Globalization;
using System.Windows;

namespace GatewayApp.Views.Dialogs;

public partial class BulkAssignWindow : Window
{
    public BulkAssignWindow()
    {
        InitializeComponent();
        TypeCombo.ItemsSource = Enum.GetValues<ModbusType>();
        TypeCombo.SelectedItem = ModbusType.Coil;
    }

    public ModbusType TargetModbusType { get; private set; } = ModbusType.Coil;

    public string PlcPrefix { get; private set; } = "M";

    public int StartNumber { get; private set; }

    public int Increment { get; private set; } = 1;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (TypeCombo.SelectedItem is ModbusType type)
        {
            TargetModbusType = type;
        }

        PlcPrefix = PrefixTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(PlcPrefix))
        {
            MessageBox.Show(this, "PLC デバイス接頭辞を入力してください。", "一括割当", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(StartTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start))
        {
            MessageBox.Show(this, "開始番号を整数で入力してください。", "一括割当", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(IncrementTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var increment) || increment == 0)
        {
            MessageBox.Show(this, "増分は 0 以外の整数で入力してください。", "一括割当", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartNumber = start;
        Increment = increment;
        DialogResult = true;
    }
}

