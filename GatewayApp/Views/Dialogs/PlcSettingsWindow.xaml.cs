using GatewayApp.Models;
using PlcComm.KvHostLink;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GatewayApp.Views.Dialogs;

public sealed record PlcProfileOption(string Value, string Label);

public partial class PlcSettingsWindow : Window
{
    private static readonly PlcProfileOption[] SlmpProfiles =
    [
        new("melsec:iq-r", "iQ-R"),
        new("melsec:iq-f", "iQ-F"),
        new("melsec:iq-l", "iQ-L"),
        new("melsec:mx-r", "MX-R"),
        new("melsec:mx-f", "MX-F"),
        new("melsec:qnudv", "QnUDV"),
        new("melsec:qnu", "QnU"),
        new("melsec:qcpu", "QCPU"),
        new("melsec:lcpu", "LCPU"),
    ];

    private static readonly PlcProfileOption[] HostLinkProfiles =
        KvHostLinkDeviceRanges.AvailablePlcProfiles()
            .Select(profile => new PlcProfileOption(profile, FormatHostLinkProfile(profile)))
            .ToArray();

    public PlcSettingsWindow(PlcSettings settings, bool isRunning)
    {
        InitializeComponent();
        Settings = settings.Normalize();
        DataContext = Settings;

        SlmpRadio.IsChecked = Settings.Protocol.Equals("SLMP", StringComparison.OrdinalIgnoreCase);
        HostLinkRadio.IsChecked = !SlmpRadio.IsChecked;
        SaveButton.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = isRunning ? "稼働中は読み取り専用です" : WarningText.Text;
        UpdateProfileSelector();
        if (isRunning)
        {
            SetReadOnly(FormGrid);
        }
    }

    public PlcSettings Settings { get; }

    private void ProtocolRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Settings.Protocol = SlmpRadio.IsChecked == true ? "SLMP" : "HostLink";
        UpdateProfileSelector();
    }

    private void UpdateProfileSelector()
    {
        ProfileLabel.Text = "機種";

        if (SlmpRadio.IsChecked == true)
        {
            ProfileCombo.ItemsSource = WithCurrentOption(SlmpProfiles, Settings.SlmpProfile, FormatSlmpProfile);
            ProfileCombo.SelectedValue = Settings.SlmpProfile;
            return;
        }

        ProfileCombo.ItemsSource = WithCurrentOption(HostLinkProfiles, Settings.HostLinkProfile, FormatHostLinkProfile);
        ProfileCombo.SelectedValue = Settings.HostLinkProfile;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.Protocol = SlmpRadio.IsChecked == true ? "SLMP" : "HostLink";
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

    private static string FormatSlmpProfile(string profileName)
    {
        return profileName switch
        {
            "melsec:iq-r" => "iQ-R",
            "melsec:iq-f" => "iQ-F",
            "melsec:iq-l" => "iQ-L",
            "melsec:mx-r" => "MX-R",
            "melsec:mx-f" => "MX-F",
            "melsec:qnudv" => "QnUDV",
            "melsec:qnu" => "QnU",
            "melsec:qcpu" => "QCPU",
            "melsec:lcpu" => "LCPU",
            _ => string.IsNullOrWhiteSpace(profileName) ? "MELSEC" : profileName,
        };
    }

    private static string FormatHostLinkProfile(string profileName)
    {
        return profileName switch
        {
            "keyence:kv-nano" => "KV-Nano",
            "keyence:kv-nano-xym" => "KV-Nano / XYM",
            "keyence:kv-3000" => "KV-3000",
            "keyence:kv-3000-xym" => "KV-3000 / XYM",
            "keyence:kv-5000" => "KV-5000",
            "keyence:kv-5000-xym" => "KV-5000 / XYM",
            "keyence:kv-7000" => "KV-7000",
            "keyence:kv-7000-xym" => "KV-7000 / XYM",
            "keyence:kv-8000" => "KV-8000",
            "keyence:kv-8000-xym" => "KV-8000 / XYM",
            "keyence:kv-x500" => "KV-X500",
            "keyence:kv-x500-xym" => "KV-X500 / XYM",
            _ => string.IsNullOrWhiteSpace(profileName) ? "KEYENCE KV" : profileName,
        };
    }

    private static void SetReadOnly(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            switch (child)
            {
                case TextBox textBox:
                    textBox.IsReadOnly = true;
                    break;
                case ComboBox comboBox:
                    comboBox.IsEnabled = false;
                    break;
                case RadioButton radioButton:
                    radioButton.IsEnabled = false;
                    break;
            }

            SetReadOnly(child);
        }
    }
}
