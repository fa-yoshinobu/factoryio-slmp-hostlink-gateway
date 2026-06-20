using GatewayApp.Models;
using System.Windows;

namespace GatewayApp.Views.Dialogs;

public partial class PlcSettingsWindow : Window
{
    private static readonly string[] SlmpProfiles =
    [
        "melsec:iq-r",
        "melsec:iq-f",
        "melsec:qcpu",
        "melsec:lcpu",
        "melsec:qnu",
        "melsec:qnudv",
    ];

    public PlcSettingsWindow(PlcSettings settings, bool isRunning)
    {
        InitializeComponent();
        Settings = settings.Normalize();
        ProfileCombo.ItemsSource = SlmpProfiles;
        DataContext = Settings;
        ProfileCombo.SelectedItem = Settings.SlmpProfile;

        SlmpRadio.IsChecked = Settings.Protocol.Equals("SLMP", StringComparison.OrdinalIgnoreCase);
        HostLinkRadio.IsChecked = !SlmpRadio.IsChecked;
        FormGrid.IsEnabled = !isRunning;
        SaveButton.IsEnabled = !isRunning;
        WarningText.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        UpdateProfileVisibility();
    }

    public PlcSettings Settings { get; }

    private void ProtocolRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Settings.Protocol = SlmpRadio.IsChecked == true ? "SLMP" : "HostLink";
        UpdateProfileVisibility();
    }

    private void UpdateProfileVisibility()
    {
        var visible = SlmpRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ProfileLabel.Visibility = visible;
        ProfileCombo.Visibility = visible;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.Protocol = SlmpRadio.IsChecked == true ? "SLMP" : "HostLink";
        Settings.SlmpProfile = ProfileCombo.SelectedItem as string ?? PlcSettings.DefaultSlmpProfile;
        Settings.Normalize();
        DialogResult = true;
    }
}
