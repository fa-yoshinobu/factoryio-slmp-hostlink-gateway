using GatewayApp.Models;
using GatewayApp.Services;
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
        WarningText.Text = isRunning ? Loc.Text("ReadOnlyRunning") : WarningText.Text;
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
            MessageBox.Show(this, Loc.Text("PortRange"), Loc.Text("ModbusSettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Settings.UnitId is < 1 or > 247)
        {
            MessageBox.Show(this, Loc.Text("UnitIdRange"), Loc.Text("ModbusSettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Settings.RealScale < 1)
        {
            MessageBox.Show(this, Loc.Text("ScaleRange"), Loc.Text("ModbusSettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadMaxAddress(MaxCoilTextBox.Text, Loc.Text("MaxAddressCoil"), out var maxCoilAddress)
            || !TryReadMaxAddress(MaxDiscreteInputTextBox.Text, Loc.Text("MaxAddressDi"), out var maxDiscreteInputAddress)
            || !TryReadMaxAddress(MaxHoldingRegisterTextBox.Text, Loc.Text("MaxAddressHr"), out var maxHoldingRegisterAddress)
            || !TryReadMaxAddress(MaxInputRegisterTextBox.Text, Loc.Text("MaxAddressIr"), out var maxInputRegisterAddress))
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
            MessageBox.Show(this, Loc.Format("IntegerRequired", label), Loc.Text("ModbusSettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (parsed is < 0 or > 65535)
        {
            MessageBox.Show(this, Loc.Format("AddressRange", label), Loc.Text("ModbusSettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        value = parsed;
        return true;
    }

}
