using GatewayApp.Models;
using GatewayApp.Services;
using System.Windows;

namespace GatewayApp.Views.Dialogs;

public partial class BulkAssignWindow : Window
{
    private static readonly string[] SlmpBoolDevices = ["X", "Y", "M", "L", "B"];
    private static readonly string[] SlmpRegisterDevices = ["D", "W", "R", "ZR"];
    private static readonly string[] HostLinkBoolDevices = ["R", "B", "MR", "LR", "X", "Y", "M", "L"];
    private static readonly string[] HostLinkRegisterDevices = ["DM", "EM", "FM", "ZF", "W", "D", "E", "F"];

    private readonly PlcSettings _plc;

    public BulkAssignWindow()
        : this(new PlcSettings())
    {
    }

    public BulkAssignWindow(PlcSettings plc)
    {
        InitializeComponent();
        _plc = plc.Clone();
        TypeCombo.ItemsSource = Enum.GetValues<ModbusType>();
        TypeCombo.SelectedItem = ModbusType.Coil;
        TypeCombo.SelectionChanged += (_, _) => UpdateDeviceOptions();
        UpdateDeviceOptions();
    }

    public ModbusType TargetModbusType { get; private set; } = ModbusType.Coil;

    public string PlcPrefix { get; private set; } = "M";

    public string StartNumberText { get; private set; } = "0";

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (TypeCombo.SelectedItem is ModbusType type)
        {
            TargetModbusType = type;
        }

        PlcPrefix = DeviceCombo.SelectedItem as string ?? string.Empty;
        if (string.IsNullOrWhiteSpace(PlcPrefix))
        {
            MessageBox.Show(this, "PLC デバイスを選択してください。", "一括割当", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartNumberText = StartTextBox.Text.Trim();
        if (!PlcAddressSequence.TryFormat(_plc.Protocol, _plc.SlmpProfile, PlcPrefix, StartNumberText, 0, out _, out var startError))
        {
            MessageBox.Show(this, startError, "一括割当", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void UpdateDeviceOptions()
    {
        var modbusType = TypeCombo.SelectedItem is ModbusType selected
            ? selected
            : ModbusType.Coil;
        var options = DeviceOptions(_plc.Protocol, modbusType);
        DeviceCombo.ItemsSource = options;
        DeviceCombo.SelectedIndex = options.Length > 0 ? 0 : -1;
    }

    public static string[] DeviceOptions(string plcProtocol, ModbusType modbusType)
    {
        var isHostLink = plcProtocol.Equals("HostLink", StringComparison.OrdinalIgnoreCase);
        var isRegister = modbusType is ModbusType.HoldingRegister or ModbusType.InputRegister;
        return (isHostLink, isRegister) switch
        {
            (false, false) => SlmpBoolDevices,
            (false, true) => SlmpRegisterDevices,
            (true, false) => HostLinkBoolDevices,
            (true, true) => HostLinkRegisterDevices,
        };
    }
}
