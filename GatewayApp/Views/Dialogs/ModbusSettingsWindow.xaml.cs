using GatewayApp.Models;
using System.Globalization;
using System.Windows;

namespace GatewayApp.Views.Dialogs;

public partial class ModbusSettingsWindow : Window
{
    public ModbusSettingsWindow(ModbusSettings settings, bool isRunning)
    {
        InitializeComponent();
        Settings = settings;
        DataContext = Settings;
        MaxCoilTextBox.Text = FormatMaxAddress(Settings.MaxCoilAddress);
        MaxDiscreteInputTextBox.Text = FormatMaxAddress(Settings.MaxDiscreteInputAddress);
        MaxHoldingRegisterTextBox.Text = FormatMaxAddress(Settings.MaxHoldingRegisterAddress);
        MaxInputRegisterTextBox.Text = FormatMaxAddress(Settings.MaxInputRegisterAddress);
        SaveButton.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = isRunning ? "稼働中は読み取り専用です" : WarningText.Text;
        if (isRunning)
        {
            DialogReadOnlyHelper.SetReadOnly(FormGrid, disableSelectors: false);
        }
    }

    public ModbusSettings Settings { get; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Settings.Port is < 1 or > 65535)
        {
            MessageBox.Show(this, "ポートは 1〜65535 で入力してください。", "Modbus通信設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Settings.UnitId is < 1 or > 247)
        {
            MessageBox.Show(this, "ユニット ID は 1〜247 で入力してください。", "Modbus通信設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Settings.RealScale < 1)
        {
            MessageBox.Show(this, "Scale は 1 以上で入力してください。", "Modbus通信設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadMaxAddress(MaxCoilTextBox.Text, "Coil 最大アドレス", out var maxCoilAddress)
            || !TryReadMaxAddress(MaxDiscreteInputTextBox.Text, "DI 最大アドレス", out var maxDiscreteInputAddress)
            || !TryReadMaxAddress(MaxHoldingRegisterTextBox.Text, "HR 最大アドレス", out var maxHoldingRegisterAddress)
            || !TryReadMaxAddress(MaxInputRegisterTextBox.Text, "IR 最大アドレス", out var maxInputRegisterAddress))
        {
            return;
        }

        Settings.MaxCoilAddress = maxCoilAddress;
        Settings.MaxDiscreteInputAddress = maxDiscreteInputAddress;
        Settings.MaxHoldingRegisterAddress = maxHoldingRegisterAddress;
        Settings.MaxInputRegisterAddress = maxInputRegisterAddress;
        DialogResult = true;
    }

    private static string FormatMaxAddress(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private bool TryReadMaxAddress(string text, string label, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            MessageBox.Show(this, $"{label} は整数で入力してください。", "Modbus通信設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (parsed is < 0 or > 65535)
        {
            MessageBox.Show(this, $"{label} は 0〜65535 で入力してください。", "Modbus通信設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        value = parsed;
        return true;
    }

}
