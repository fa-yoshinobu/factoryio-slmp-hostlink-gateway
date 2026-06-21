using GatewayApp.Models;
using GatewayApp.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

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

    private bool _updatingSimulatorUi;

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
        WarningText.Text = isRunning ? Loc.Text("ReadOnlyRunning") : WarningText.Text;
        UpdateProfileSelector();
        UpdateSimulatorOption();
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
        if (!Settings.UseSimulator)
        {
            ApplyProtocolDefaultPort(_currentProtocol, protocol);
        }

        Settings.Protocol = protocol;
        _currentProtocol = protocol;
        UpdateProfileSelector();
    }

    private void UpdateProfileSelector()
    {
        _updatingSimulatorUi = true;
        try
        {
            if (SlmpRadio.IsChecked == true)
            {
                ProfileCombo.ItemsSource = WithCurrentOption(SlmpProfiles, Settings.SlmpProfile, PlcSettings.FormatSlmpProfile);
                ProfileCombo.SelectedValue = Settings.SlmpProfile;
                return;
            }

            ProfileCombo.ItemsSource = WithCurrentOption(HostLinkProfiles, Settings.HostLinkProfile, PlcSettings.FormatHostLinkProfile);
            ProfileCombo.SelectedValue = Settings.HostLinkProfile;
        }
        finally
        {
            _updatingSimulatorUi = false;
            UpdateSimulatorOption();
        }
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSimulatorUi)
        {
            return;
        }

        UpdateSelectedProfileSetting();
        UpdateSimulatorOption();
    }

    private void SimulatorCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingSimulatorUi)
        {
            return;
        }

        UpdateSelectedProfileSetting();
        Settings.UseSimulator = SimulatorCheckBox.Visibility == Visibility.Visible
            && SimulatorCheckBox.IsChecked == true;
        UpdateEndpointInputState();
    }

    private void UpdateSimulatorOption()
    {
        if (SimulatorCheckBox is null)
        {
            return;
        }

        var protocol = SelectedProtocol();
        UpdateSelectedProfileSetting();
        var supportsSimulator = PlcSettings.SupportsSimulator(protocol, Settings.SlmpProfile, Settings.HostLinkProfile);

        _updatingSimulatorUi = true;
        try
        {
            SimulatorCheckBox.Content = protocol.Equals("HostLink", StringComparison.OrdinalIgnoreCase)
                ? "KV STUDIO(Simulator)"
                : "GX Simulator 3";
            SimulatorCheckBox.Visibility = supportsSimulator ? Visibility.Visible : Visibility.Collapsed;
            if (!supportsSimulator)
            {
                Settings.UseSimulator = false;
                SimulatorCheckBox.IsChecked = false;
                return;
            }

            SimulatorCheckBox.IsChecked = Settings.UseSimulator;
        }
        finally
        {
            _updatingSimulatorUi = false;
        }

        UpdateEndpointInputState();
    }

    private void UpdateEndpointInputState()
    {
        var canEditEndpoint = SimulatorCheckBox.Visibility != Visibility.Visible
            || SimulatorCheckBox.IsChecked != true;
        HostTextBox.IsEnabled = canEditEndpoint;
        TransportCombo.IsEnabled = canEditEndpoint;
        PortTextBox.IsEnabled = canEditEndpoint;
    }

    private void UpdateSelectedProfileSetting()
    {
        if (SlmpRadio.IsChecked == true)
        {
            Settings.SlmpProfile = ProfileCombo.SelectedValue as string ?? Settings.SlmpProfile;
            return;
        }

        Settings.HostLinkProfile = ProfileCombo.SelectedValue as string ?? Settings.HostLinkProfile;
    }

    private string SelectedProtocol()
    {
        return SlmpRadio.IsChecked == true ? "SLMP" : "HostLink";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.Protocol = SelectedProtocol();
        Settings.Transport = TransportCombo.SelectedValue as string ?? Settings.Transport;
        if (Settings.Protocol.Equals("SLMP", StringComparison.OrdinalIgnoreCase))
        {
            Settings.SlmpProfile = ProfileCombo.SelectedValue as string ?? PlcSettings.DefaultSlmpProfile;
        }
        else
        {
            Settings.HostLinkProfile = ProfileCombo.SelectedValue as string ?? PlcSettings.DefaultHostLinkProfile;
        }

        Settings.UseSimulator = SimulatorCheckBox.Visibility == Visibility.Visible
            && SimulatorCheckBox.IsChecked == true;

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
