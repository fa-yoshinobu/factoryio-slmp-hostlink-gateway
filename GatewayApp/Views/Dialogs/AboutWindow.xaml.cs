using CommunityToolkit.Mvvm.ComponentModel;
using CsvHelper;
using NModbus;
using PlcComm.KvHostLink;
using PlcComm.Slmp;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace GatewayApp.Views.Dialogs;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var appVersion = GetAssemblyVersionText(Assembly.GetExecutingAssembly());
        VersionTextBlock.Text = $"Version: {appVersion}";

        LibrariesListView.ItemsSource = new[]
        {
            new LibraryInfo("CommunityToolkit.Mvvm", GetAssemblyVersionText(typeof(ObservableObject).Assembly), "ObservableObject and RelayCommand support"),
            new LibraryInfo("CsvHelper", GetAssemblyVersionText(typeof(CsvReader).Assembly), "Factory I/O tag CSV parsing"),
            new LibraryInfo("NModbus", GetAssemblyVersionText(typeof(ModbusFactory).Assembly), "Modbus TCP slave/server"),
            new LibraryInfo("PlcComm.Slmp", GetAssemblyVersionText(typeof(SlmpConnectionOptions).Assembly), "MELSEC SLMP PLC read/write"),
            new LibraryInfo("PlcComm.KvHostLink", GetAssemblyVersionText(typeof(KvHostLinkConnectionOptions).Assembly), "KEYENCE Host Link PLC read/write"),
            new LibraryInfo(".NET Runtime", Environment.Version.ToString(), "WPF application runtime"),
        };
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (InvalidOperationException)
        {
            // Ignore browser launch failures.
        }
        catch (Win32Exception)
        {
            // Ignore browser launch failures.
        }

        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string GetAssemblyVersionText(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plusIndex = info.IndexOf('+', StringComparison.Ordinal);
            return plusIndex >= 0 ? info[..plusIndex] : info;
        }

        var version = assembly.GetName().Version;
        return version?.ToString() ?? "Unknown";
    }

    private sealed record LibraryInfo(string Name, string Version, string Notes);
}
