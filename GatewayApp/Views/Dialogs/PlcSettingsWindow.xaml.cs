using GatewayApp.Models;
using System.Globalization;
using System.Windows;

namespace GatewayApp.Views.Dialogs;

public sealed record PlcProfileOption(string Value, string Label);

public sealed record TransportOption(string Value, string Label);

public partial class PlcSettingsWindow : Window
{
    private static readonly TransportOption[] TransportOptions =
    [
        new("TCP", "TCP"),
        new("UDP", "UDP"),
    ];

    private static readonly PlcProfileOption[] SlmpProfiles =
        PlcSettings.SlmpProfileOptions
            .Select(option => new PlcProfileOption(option.Value, option.Label))
            .ToArray();

    private static readonly PlcProfileOption[] HostLinkProfiles =
        PlcSettings.HostLinkProfileOptions
            .Select(option => new PlcProfileOption(option.Value, option.Label))
            .ToArray();

    private string _currentProtocol;

    public PlcSettingsWindow(PlcSettings settings, bool isRunning)
    {
        InitializeComponent();
        Settings = settings.Normalize();
        DataContext = Settings;
        _currentProtocol = Settings.Protocol;

        TransportCombo.ItemsSource = TransportOptions;
        TransportCombo.SelectedValue = Settings.Transport;
        SlmpRadio.IsChecked = Settings.Protocol.Equals("SLMP", StringComparison.OrdinalIgnoreCase);
        HostLinkRadio.IsChecked = !SlmpRadio.IsChecked;
        SaveButton.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = isRunning ? "稼働中は読み取り専用です" : WarningText.Text;
        UpdateProfileSelector();
        if (isRunning)
        {
            DialogReadOnlyHelper.SetReadOnly(FormGrid, disableSelectors: true);
        }
    }

    public PlcSettings Settings { get; }

    private void ProtocolRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var protocol = SlmpRadio.IsChecked == true ? "SLMP" : "HostLink";
        ApplyProtocolDefaultPort(_currentProtocol, protocol);
        Settings.Protocol = protocol;
        _currentProtocol = protocol;
        UpdateProfileSelector();
    }

    private void UpdateProfileSelector()
    {
        ProfileLabel.Text = "機種";

        if (SlmpRadio.IsChecked == true)
        {
            ProfileCombo.ItemsSource = WithCurrentOption(SlmpProfiles, Settings.SlmpProfile, PlcSettings.FormatSlmpProfile);
            ProfileCombo.SelectedValue = Settings.SlmpProfile;
            return;
        }

        ProfileCombo.ItemsSource = WithCurrentOption(HostLinkProfiles, Settings.HostLinkProfile, PlcSettings.FormatHostLinkProfile);
        ProfileCombo.SelectedValue = Settings.HostLinkProfile;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.Protocol = SlmpRadio.IsChecked == true ? "SLMP" : "HostLink";
        Settings.Transport = TransportCombo.SelectedValue as string ?? Settings.Transport;
        if (Settings.Protocol.Equals("SLMP", StringComparison.OrdinalIgnoreCase))
        {
            Settings.SlmpProfile = ProfileCombo.SelectedValue as string ?? PlcSettings.DefaultSlmpProfile;
        }
        else
        {
            Settings.HostLinkProfile = ProfileCombo.SelectedValue as string ?? PlcSettings.DefaultHostLinkProfile;
        }

        Settings.Normalize();
        DialogResult = true;
    }

    private void ApplyProtocolDefaultPort(string previousProtocol, string nextProtocol)
    {
        if (previousProtocol.Equals(nextProtocol, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousDefault = PlcSettings.DefaultPortForProtocol(previousProtocol);
        var nextDefault = PlcSettings.DefaultPortForProtocol(nextProtocol);
        if (Settings.Port != previousDefault)
        {
            return;
        }

        Settings.Port = nextDefault;
        PortTextBox.Text = nextDefault.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<PlcProfileOption> WithCurrentOption(
        IReadOnlyList<PlcProfileOption> options,
        string currentValue,
        Func<string, string> labelFormatter)
    {
        if (options.Any(option => string.Equals(option.Value, currentValue, StringComparison.OrdinalIgnoreCase)))
        {
            return options;
        }

        return options.Append(new PlcProfileOption(currentValue, labelFormatter(currentValue))).ToArray();
    }

}
