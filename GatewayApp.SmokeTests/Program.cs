using GatewayApp;
using GatewayApp.Converters;
using GatewayApp.Models;
using GatewayApp.Services;
using GatewayApp.ViewModels;
using GatewayApp.Views.Dialogs;
using NModbus;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GatewayApp.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var failures = new List<string>();
        Application? application = null;
        MainWindow? window = null;

        try
        {
            application = new Application();
            window = new MainWindow();
            window.Show();
            window.UpdateLayout();

            if (window.Icon is null)
            {
                failures.Add("MainWindow icon was not loaded.");
            }

            if (window.DataContext is not MainViewModel viewModel)
            {
                failures.Add("MainWindow DataContext is not MainViewModel.");
            }
            else
            {
                SmokeLanguageSwitch(window, viewModel, failures);
                SmokeAlwaysOnTopOption(window, failures);
                SmokeErrorLogBinding(window, viewModel, failures);
                SmokeExpectedStopExceptionSuppression(viewModel, failures);
                SmokeRegisterForceInput(viewModel, failures);
                SmokeScaledRawFormatting(failures);
                SmokePlcProfileOptions(failures);
                SmokeStrictSettingsJson(failures);
                SmokeAtomicSettingsSave(failures);
                SmokePlcSimulatorOption(failures);
                SmokePlcAutoReconnectOption(failures);
                SmokePlcConnectionFailureMessage(failures);
                SmokeBulkAssignDeviceOptions(failures);
                SmokePlcAddressSequence(failures);
                SmokeTypedPlcAddresses(failures);
                SmokeValueBrushConverter(failures);
                SmokeTodoFixes(viewModel, failures);
                SmokeLogWindow(viewModel, failures);
                SmokeLogFileDirectory(failures);
                SmokeAboutWindow(failures);
                SmokeModbusStartStop(failures);
                SmokeMainWindowClosesWhenDisconnected(failures);
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (window?.DataContext is MainViewModel viewModel)
            {
                viewModel.IsDirty = false;
            }

            window?.Close();
            application?.Shutdown();
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("Smoke tests passed.");
            return 0;
        }

        Console.Error.WriteLine("Smoke tests failed:");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    private static void SmokeErrorLogBinding(MainWindow window, MainViewModel viewModel, List<string> failures)
    {
        try
        {
            viewModel.ReportException(new InvalidOperationException("smoke error"));
            window.UpdateLayout();
            PumpDispatcher();

            if (viewModel.Logs.Count == 0)
            {
                failures.Add("Error log entry was not added.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Error log binding smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeExpectedStopExceptionSuppression(MainViewModel viewModel, List<string> failures)
    {
        try
        {
            viewModel.ClearLogs();
            viewModel.IsRunning = false;
            viewModel.ReportException(new SocketException((int)SocketError.OperationAborted));

            if (viewModel.Logs.Count != 0)
            {
                failures.Add("Expected local stop SocketException was added to the error log.");
            }

            viewModel.ReportException(new InvalidOperationException("real error"));
            if (viewModel.Logs.Count != 1)
            {
                failures.Add("Real exception was not added to the error log after stop exception suppression.");
            }

            viewModel.ClearLogs();
        }
        catch (Exception ex)
        {
            failures.Add($"Expected stop exception suppression smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeRegisterForceInput(MainViewModel viewModel, List<string> failures)
    {
        try
        {
            viewModel.ApplyModbusSettings(new ModbusSettings
            {
                RealScale = 100,
                MaxCoilAddress = 0,
                MaxHoldingRegisterAddress = 1,
                MaxInputRegisterAddress = 1,
            });

            var coil = viewModel.Mappings.Single(x =>
                x.ModbusType == ModbusType.Coil && x.ModbusAddress == 0);
            coil.RawValue = 0;

            var holding = viewModel.Mappings.Single(x =>
                x.ModbusType == ModbusType.HoldingRegister && x.ModbusAddress == 0);
            holding.RawValue = 321;

            var input = viewModel.Mappings.Single(x =>
                x.ModbusType == ModbusType.InputRegister && x.ModbusAddress == 0);
            input.RawValue = 125;

            viewModel.ToggleForceX();

            if (holding.ForceValue != 321)
            {
                failures.Add($"Force X enable did not inherit holding register value. Expected 321, got {holding.ForceValue}.");
            }

            if (input.ForceEnabled || input.ForceValue.HasValue)
            {
                failures.Add($"Force X affected input register. Enabled={input.ForceEnabled}, value={input.ForceValue}.");
            }

            if (coil.ForceValue != 0)
            {
                failures.Add($"Force X enable did not inherit coil value. Expected 0, got {coil.ForceValue}.");
            }

            viewModel.CycleBoolForceAsync(coil).GetAwaiter().GetResult();

            if (coil.ForceValue != 1)
            {
                failures.Add($"Coil force did not toggle from inherited current value. Expected 1, got {coil.ForceValue}.");
            }

            viewModel.CycleBoolForceAsync(coil).GetAwaiter().GetResult();

            if (coil.ForceValue != 0)
            {
                failures.Add($"Coil force did not toggle back to inherited current value. Expected 0, got {coil.ForceValue}.");
            }

            holding.DisplayType = DisplayType.Int16;
            viewModel.BeginRegisterEdit(holding);
            holding.ForceEditText = "1234";
            viewModel.CommitRegisterForceAsync(holding, clear: false).GetAwaiter().GetResult();

            if (holding.ForceValue != 1234)
            {
                failures.Add($"Holding register force value mismatch. Expected 1234, got {holding.ForceValue}.");
            }

            viewModel.BeginRegisterEdit(input);

            if (input.IsForceEditing)
            {
                failures.Add("Input register editing started while Force Y was disabled.");
                input.IsForceEditing = false;
            }

            viewModel.ToggleForceY();

            if (input.ForceValue != 125)
            {
                failures.Add($"Force Y enable did not inherit input register value. Expected 125, got {input.ForceValue}.");
            }

            input.DisplayType = DisplayType.ScaledReal;
            viewModel.BeginRegisterEdit(input);
            input.ForceEditText = "1.25";
            viewModel.CommitRegisterForceAsync(input, clear: false).GetAwaiter().GetResult();

            if (input.ForceValue != 125)
            {
                failures.Add($"Input register force value mismatch. Expected 125, got {input.ForceValue}.");
            }

            input.RealScale = 10;
            viewModel.BeginRegisterEdit(input);
            input.ForceEditText = "10.00";
            viewModel.CommitRegisterForceAsync(input, clear: false).GetAwaiter().GetResult();

            if (input.ForceValue != 100)
            {
                failures.Add($"Scale 10 input register force value mismatch. Expected 100, got {input.ForceValue}.");
            }

            input.RealScale = 100;
            viewModel.BeginRegisterEdit(input);
            input.ForceEditText = "10.00";
            viewModel.CommitRegisterForceAsync(input, clear: false).GetAwaiter().GetResult();

            if (input.ForceValue != 1000)
            {
                failures.Add($"Scale 100 input register force value mismatch. Expected 1000, got {input.ForceValue}.");
            }

            viewModel.BeginRegisterEdit(input);
            input.ForceEditText = "not-a-number";
            viewModel.CommitRegisterForceAsync(input, clear: false).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(viewModel.StatusMessage))
            {
                failures.Add("Invalid register input did not report an error.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Register force smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeModbusStartStop(List<string> failures)
    {
        try
        {
            var port = GetFreeTcpPort();
            using var modbus = new ModbusSlaveService();
            modbus.StartAsync(new ModbusSettings
            {
                ListenIp = "127.0.0.1",
                Port = port,
                UnitId = 1,
            }).GetAwaiter().GetResult();

            if (modbus.RequestCycleMilliseconds.HasValue)
            {
                failures.Add("Modbus request cycle was set before any external request.");
            }

            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            var master = new ModbusFactory().CreateMaster(client);
            master.ReadCoils(1, 0, 1);
            Thread.Sleep(20);
            master.ReadCoils(1, 0, 1);

            var externalCycle = modbus.RequestCycleMilliseconds;
            if (!externalCycle.HasValue || externalCycle.Value <= 0)
            {
                failures.Add("External Modbus requests did not update request cycle.");
            }

            var entry = new MappingEntry(ModbusType.Coil, 0);
            modbus.ReadRaw(entry);
            modbus.WriteRaw(entry, 1);
            if (modbus.RequestCycleMilliseconds != externalCycle)
            {
                failures.Add("Internal Modbus access changed request cycle.");
            }

            modbus.Stop();
            if (modbus.RequestCycleMilliseconds.HasValue)
            {
                failures.Add("Modbus request cycle was not cleared after stop.");
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        catch (Exception ex)
        {
            failures.Add($"Modbus start/stop smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeMainWindowClosesWhenDisconnected(List<string> failures)
    {
        MainWindow? closeWindow = null;
        try
        {
            closeWindow = new MainWindow();
            closeWindow.Show();
            closeWindow.UpdateLayout();

            if (closeWindow.DataContext is MainViewModel viewModel)
            {
                viewModel.IsDirty = false;
            }

            closeWindow.Close();
            PumpDispatcher();

            if (closeWindow.IsVisible)
            {
                failures.Add("MainWindow remained visible after Close() while disconnected.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"MainWindow close smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            closeWindow?.Close();
        }
    }

    private static void SmokeLogWindow(MainViewModel viewModel, List<string> failures)
    {
        LogWindow? logWindow = null;
        try
        {
            logWindow = new LogWindow(viewModel);
            logWindow.Show();
            logWindow.UpdateLayout();
            viewModel.ReportLog("log window smoke");
            PumpDispatcher();

            if (viewModel.Logs.Count == 0)
            {
                failures.Add("Log window smoke did not add a log entry.");
            }

            viewModel.ClearLogs();
            PumpDispatcher();

            if (viewModel.Logs.Count != 0)
            {
                failures.Add($"Clear logs failed. Count={viewModel.Logs.Count}.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Log window smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            logWindow?.Close();
        }
    }

    private static void SmokeLanguageSwitch(MainWindow window, MainViewModel viewModel, List<string> failures)
    {
        try
        {
            if (!viewModel.WindowTitle.Contains("Unsaved settings", StringComparison.Ordinal))
            {
                failures.Add($"Default language was not English. Title={viewModel.WindowTitle}");
            }

            if (window.FindName("JapaneseLanguageMenuItem") is not MenuItem japaneseItem
                || window.FindName("EnglishLanguageMenuItem") is not MenuItem englishItem)
            {
                failures.Add("Language menu items were not found.");
                return;
            }

            japaneseItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpDispatcher();
            if (!viewModel.WindowTitle.Contains("未保存設定", StringComparison.Ordinal) || !japaneseItem.IsChecked)
            {
                failures.Add($"Japanese language switch did not update UI state. Title={viewModel.WindowTitle}");
            }

            if (window.FindName("MappingGrid") is DataGrid mappingGrid
                && mappingGrid.Columns[0].Header?.ToString() != "Modbus種別")
            {
                failures.Add($"Mapping grid header did not switch to Japanese. Header={mappingGrid.Columns[0].Header}");
            }

            englishItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpDispatcher();
            if (!viewModel.WindowTitle.Contains("Unsaved settings", StringComparison.Ordinal) || !englishItem.IsChecked)
            {
                failures.Add($"English language switch did not restore UI state. Title={viewModel.WindowTitle}");
            }

            if (window.FindName("MappingGrid") is DataGrid restoredGrid
                && restoredGrid.Columns[0].Header?.ToString() != "Modbus Type")
            {
                failures.Add($"Mapping grid header did not switch back to English. Header={restoredGrid.Columns[0].Header}");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Language switch smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeAlwaysOnTopOption(MainWindow window, List<string> failures)
    {
        try
        {
            if (window.FindName("AlwaysOnTopMenuItem") is not MenuItem alwaysOnTopItem)
            {
                failures.Add("Always on Top menu item was not found.");
                return;
            }

            if (window.Topmost || alwaysOnTopItem.IsChecked)
            {
                failures.Add("Always on Top was enabled by default.");
            }

            alwaysOnTopItem.IsChecked = true;
            alwaysOnTopItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpDispatcher();
            if (!window.Topmost || !alwaysOnTopItem.IsChecked)
            {
                failures.Add("Always on Top menu item did not enable window topmost.");
            }

            alwaysOnTopItem.IsChecked = false;
            alwaysOnTopItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpDispatcher();
            if (window.Topmost || alwaysOnTopItem.IsChecked)
            {
                failures.Add("Always on Top menu item did not disable window topmost.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Always on Top smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeLogFileDirectory(List<string> failures)
    {
        try
        {
            var marker = $"smoke-log-path-{Guid.NewGuid():N}";
            var service = new LogFileService();
            service.WriteGatewayLog(new LogEntry(marker));
            service.WriteExceptionLog(new InvalidOperationException(marker));

            var gatewayLogPath = Path.Combine(AppContext.BaseDirectory, "gateway.log");
            var errorLogPath = Path.Combine(AppContext.BaseDirectory, "error.log");

            if (!File.Exists(gatewayLogPath) || !File.ReadAllText(gatewayLogPath).Contains(marker, StringComparison.Ordinal))
            {
                failures.Add($"gateway.log was not written under the executable directory: {gatewayLogPath}");
            }

            if (!File.Exists(errorLogPath) || !File.ReadAllText(errorLogPath).Contains(marker, StringComparison.Ordinal))
            {
                failures.Add($"error.log was not written under the executable directory: {errorLogPath}");
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"gateway-log-smoke-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(tempDir);
                var tempService = new LogFileService(tempDir, maxLogBytes: 64);
                var tempGatewayLogPath = Path.Combine(tempDir, "gateway.log");
                var tempErrorLogPath = Path.Combine(tempDir, "error.log");
                var tempGatewayRotatedPath = $"{tempGatewayLogPath}.1";
                var tempErrorRotatedPath = $"{tempErrorLogPath}.1";

                File.WriteAllText(tempGatewayLogPath, new string('x', 60));
                tempService.WriteGatewayLog(new LogEntry("after-size-limit"));
                var currentLogText = File.ReadAllText(tempGatewayLogPath);
                var rotatedLogText = File.Exists(tempGatewayRotatedPath) ? File.ReadAllText(tempGatewayRotatedPath) : string.Empty;
                if (rotatedLogText.Length == 0
                    || !currentLogText.Contains("after-size-limit", StringComparison.Ordinal)
                    || !rotatedLogText.StartsWith('x'))
                {
                    failures.Add("gateway.log did not rotate when it exceeded the configured size limit.");
                }

                File.WriteAllText(tempGatewayLogPath, "old gateway");
                File.WriteAllText(tempErrorLogPath, "old error");
                File.WriteAllText(tempGatewayRotatedPath, "old gateway archive");
                File.WriteAllText(tempErrorRotatedPath, "old error archive");
                tempService.ClearAllLogs();
                if (File.ReadAllText(tempGatewayLogPath).Length != 0
                    || File.ReadAllText(tempErrorLogPath).Length != 0
                    || File.Exists(tempGatewayRotatedPath)
                    || File.Exists(tempErrorRotatedPath))
                {
                    failures.Add("Startup log clear did not empty current logs and delete rotated logs.");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Log file directory smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeAboutWindow(List<string> failures)
    {
        AboutWindow? aboutWindow = null;
        try
        {
            aboutWindow = new AboutWindow();
            aboutWindow.Show();
            aboutWindow.UpdateLayout();
            PumpDispatcher();

            if (aboutWindow.FindName("VersionTextBlock") is not TextBlock versionText ||
                string.IsNullOrWhiteSpace(versionText.Text) ||
                !versionText.Text.StartsWith("Version:", StringComparison.Ordinal))
            {
                failures.Add("About window version text was not populated.");
            }

            if (aboutWindow.FindName("LibrariesListView") is not ListView librariesList ||
                librariesList.Items.Count < 5)
            {
                failures.Add("About window library list was not populated.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"About window smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            aboutWindow?.Close();
        }
    }

    private static void SmokeScaledRawFormatting(List<string> failures)
    {
        try
        {
            var entry = new MappingEntry(ModbusType.HoldingRegister, 0)
            {
                DisplayType = DisplayType.ScaledReal,
                RealScale = 100,
            };

            entry.RawValue = 1000;
            AssertEqual("10.00", entry.DisplayValue, failures, "Scale 100 display value");
            AssertEqual("1000 (10.00)", entry.FormatRawWithDisplay(1000), failures, "Scaled raw 1000");
            AssertEqual("1010 (10.10)", entry.FormatRawWithDisplay(1010), failures, "Scaled raw 1010");
            AssertEqual("-1000 (-10.00)", entry.FormatRawWithDisplay(unchecked((ushort)(short)-1000)), failures, "Scaled raw -1000");
            AssertEqual("Integer: 1000", entry.RegisterIntegerToolTip, failures, "Scaled integer tooltip 1000");

            entry.RealScale = 10;
            AssertEqual("100.00", entry.DisplayValue, failures, "Scale 10 display value");
            AssertEqual("1000 (100.00)", entry.FormatRawWithDisplay(1000), failures, "Scale 10 raw remains 1000");
            AssertEqual("Integer: 1000", entry.RegisterIntegerToolTip, failures, "Scale 10 integer tooltip remains 1000");

            entry.RealScale = 100;
            entry.ForceEnabled = true;
            entry.ForceValue = unchecked((ushort)(short)-1000);
            AssertEqual("Integer: -1000", entry.RegisterIntegerToolTip, failures, "Forced scaled integer tooltip -1000");

            entry.DisplayType = DisplayType.Int16;
            AssertEqual("-1", entry.FormatRawWithDisplay(unchecked((ushort)(short)-1)), failures, "Int16 raw -1");
        }
        catch (Exception ex)
        {
            failures.Add($"Scaled raw formatting smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokePlcProfileOptions(List<string> failures)
    {
        PlcSettingsWindow? settingsWindow = null;
        try
        {
            var settings = new PlcSettings
            {
                Protocol = "HostLink",
                Port = PlcSettings.DefaultHostLinkPort,
                Transport = "udp",
                SlmpProfile = "melsec:iq-l",
                HostLinkProfile = "keyence:kv-8000-xym",
            }.Normalize();

            AssertEqual("melsec:iq-l", settings.SlmpProfile, failures, "SLMP canonical profile remains literal");
            AssertEqual("iQ-L", PlcSettings.NormalizeSlmpProfile("iQ-L"), failures, "SLMP profile legacy label remains literal");
            AssertEqual("keyence:kv-8000-xym", settings.HostLinkProfile, failures, "HostLink profile canonical value");
            AssertEqual("UDP", settings.Transport, failures, "PLC transport normalization");

            settingsWindow = new PlcSettingsWindow(settings, isRunning: false);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("ProfileCombo") is not ComboBox profileCombo)
            {
                failures.Add("PLC settings profile combo was not found.");
                return;
            }

            var selectedOption = profileCombo.SelectedItem as PlcProfileOption;
            if (selectedOption?.Value != "keyence:kv-8000-xym" || selectedOption.Label != "KV-8000 / KV-8000A / XYM")
            {
                failures.Add(
                    $"HostLink profile combo did not use human label with canonical value. Value={selectedOption?.Value}, Label={selectedOption?.Label}.");
            }

            if (settingsWindow.FindName("TransportCombo") is not ComboBox transportCombo
                || transportCombo.SelectedValue as string != "UDP")
            {
                failures.Add("PLC settings transport combo did not select UDP.");
            }

            settingsWindow.Close();
            settingsWindow = new PlcSettingsWindow(new PlcSettings
            {
                Protocol = "SLMP",
                SlmpProfile = "melsec:qcpu:qj71e71-100",
            }.Normalize(), isRunning: false);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("ProfileCombo") is not ComboBox slmpProfileCombo)
            {
                failures.Add("SLMP profile combo was not found.");
                return;
            }

            selectedOption = slmpProfileCombo.SelectedItem as PlcProfileOption;
            if (selectedOption?.Value != "melsec:qcpu:qj71e71-100" || selectedOption.Label != "QCPU / QJ71E71-100")
            {
                failures.Add(
                    $"SLMP Ethernet unit profile combo did not use human label with canonical value. Value={selectedOption?.Value}, Label={selectedOption?.Label}.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"PLC profile options smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            settingsWindow?.Close();
        }
    }

    private static void SmokeStrictSettingsJson(List<string> failures)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gateway-settings-smoke-{Guid.NewGuid():N}.json");
        try
        {
            AssertSettingsJsonRejected(
                path,
                """
                {
                  "plc": {
                    "protocol": "SLMP",
                    "host": "127.0.0.1",
                    "port": 5511,
                    "transport": "TCP",
                    "timeoutSec": 3,
                    "pollingMs": 100,
                    "slmpProfile": "melsec:iq-r",
                    "hostLinkProfile": "keyence:kv-8000",
                    "useSimulator": true,
                    "legacyField": true
                  },
                  "modbus": {
                    "listenIp": "127.0.0.1",
                    "port": 502,
                    "unitId": 1,
                    "realScale": 100,
                    "maxCoilAddress": null,
                    "maxDiscreteInputAddress": null,
                    "maxHoldingRegisterAddress": null,
                    "maxInputRegisterAddress": null
                  },
                  "realScale": 100,
                  "mappings": []
                }
                """,
                failures,
                "Settings JSON accepted an unknown legacy field.");

            AssertSettingsJsonRejected(
                path,
                """
                {
                  "plc": {
                    "protocol": "SLMP",
                    "host": "127.0.0.1",
                    "port": 5511,
                    "transport": "TCP",
                    "timeoutSec": 3,
                    "pollingMs": 100,
                    "slmpProfile": "melsec:iq-r",
                    "hostLinkProfile": "keyence:kv-8000"
                  },
                  "modbus": {
                    "listenIp": "127.0.0.1",
                    "port": 502,
                    "unitId": 1,
                    "realScale": 100,
                    "maxCoilAddress": null,
                    "maxDiscreteInputAddress": null,
                    "maxHoldingRegisterAddress": null,
                    "maxInputRegisterAddress": null
                  },
                  "realScale": 100,
                  "mappings": []
                }
                """,
                failures,
                "Settings JSON accepted a missing useSimulator field.");
        }
        catch (Exception ex)
        {
            failures.Add($"Strict settings JSON smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void AssertSettingsJsonRejected(string path, string json, List<string> failures, string failureMessage)
    {
        File.WriteAllText(path, json);
        try
        {
            _ = new SettingsService().Load(path);
            failures.Add(failureMessage);
        }
        catch (JsonException)
        {
        }
    }

    private static void SmokeAtomicSettingsSave(List<string> failures)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"factoryio-gateway-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            Directory.CreateDirectory(directory);
            var service = new SettingsService();
            var settings = new AppSettings
            {
                RealScale = 123,
                Plc = new PlcSettings { Host = "127.0.0.1", UseSimulator = true },
                Modbus = new ModbusSettings { Port = 1502 },
                Mappings =
                [
                    new MappingEntrySettings
                    {
                        ModbusType = ModbusType.HoldingRegister,
                        ModbusAddress = 1,
                        PlcAddress = "D100",
                        DisplayType = DisplayType.Int16,
                        Comment = "saved",
                    },
                ],
            };

            service.Save(path, settings);
            var loaded = service.Load(path);
            AssertEqual("123", loaded.RealScale.ToString(CultureInfo.InvariantCulture), failures, "Atomic settings save roundtrip scale");
            AssertEqual("saved", loaded.Mappings.Single().Comment, failures, "Atomic settings save roundtrip mapping");
            AssertNoSettingsTempFiles(directory, failures, "successful save");

            const string oldContent = "existing settings";
            File.WriteAllText(path, oldContent);
            File.SetAttributes(path, FileAttributes.ReadOnly);

            try
            {
                service.Save(path, settings);
                failures.Add("Atomic settings save unexpectedly overwrote a read-only file.");
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            AssertEqual(oldContent, File.ReadAllText(path), failures, "Atomic settings save preserved read-only file");
            AssertNoSettingsTempFiles(directory, failures, "failed save");
        }
        catch (Exception ex)
        {
            failures.Add($"Atomic settings save smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void AssertNoSettingsTempFiles(string directory, List<string> failures, string phase)
    {
        var tempFiles = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "settings.json.*.tmp")
            : [];
        if (tempFiles.Length > 0)
        {
            failures.Add($"Atomic settings save left temp files after {phase}: {string.Join(", ", tempFiles.Select(Path.GetFileName))}");
        }
    }

    private static void SmokePlcSimulatorOption(List<string> failures)
    {
        PlcSettingsWindow? settingsWindow = null;
        try
        {
            var slmpSettings = new PlcSettings
            {
                Protocol = "SLMP",
                Host = "192.168.0.10",
                Port = PlcSettings.DefaultSlmpPort,
                Transport = "UDP",
                SlmpProfile = "melsec:iq-r",
            }.Normalize();

            settingsWindow = new PlcSettingsWindow(slmpSettings, isRunning: false);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("SimulatorCheckBox") is not CheckBox simulatorCheck)
            {
                failures.Add("PLC simulator checkbox was not found.");
                return;
            }

            if (simulatorCheck.Visibility != Visibility.Visible)
            {
                failures.Add("SLMP simulator checkbox was not visible for iQ-R.");
            }

            AssertEqual("GX Simulator 3", simulatorCheck.Content?.ToString() ?? string.Empty, failures, "SLMP simulator label");
            simulatorCheck.IsChecked = true;
            PumpDispatcher();
            AssertSimulatorSettingsPreserved(
                settingsWindow,
                slmpSettings,
                "192.168.0.10",
                PlcSettings.DefaultSlmpPort,
                "UDP",
                failures,
                "SLMP simulator settings");

            settingsWindow.Close();
            settingsWindow = new PlcSettingsWindow(new PlcSettings
            {
                Protocol = "SLMP",
                SlmpProfile = "melsec:iq-f",
            }.Normalize(), isRunning: false);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("SimulatorCheckBox") is CheckBox unsupportedSlmpCheck
                && unsupportedSlmpCheck.Visibility != Visibility.Collapsed)
            {
                failures.Add("SLMP simulator checkbox was visible for unsupported iQ-F.");
            }

            settingsWindow.Close();
            var hostLinkSettings = new PlcSettings
            {
                Protocol = "HostLink",
                Host = "192.168.0.20",
                Port = 12345,
                Transport = "UDP",
                HostLinkProfile = "keyence:kv-x500-xym",
            }.Normalize();
            settingsWindow = new PlcSettingsWindow(hostLinkSettings, isRunning: false);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("SimulatorCheckBox") is not CheckBox hostLinkSimulatorCheck)
            {
                failures.Add("HostLink simulator checkbox was not found.");
                return;
            }

            if (hostLinkSimulatorCheck.Visibility != Visibility.Visible)
            {
                failures.Add("HostLink simulator checkbox was not visible for KV-X500 / XYM.");
            }

            AssertEqual("KV STUDIO(Simulator)", hostLinkSimulatorCheck.Content?.ToString() ?? string.Empty, failures, "HostLink simulator label");
            hostLinkSimulatorCheck.IsChecked = true;
            PumpDispatcher();
            AssertSimulatorSettingsPreserved(
                settingsWindow,
                hostLinkSettings,
                "192.168.0.20",
                12345,
                "UDP",
                failures,
                "HostLink simulator settings");

            settingsWindow.Close();
            settingsWindow = new PlcSettingsWindow(new PlcSettings
            {
                Protocol = "HostLink",
                HostLinkProfile = "keyence:kv-8000-xym",
            }.Normalize(), isRunning: false);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("SimulatorCheckBox") is not CheckBox kv8000XymSimulatorCheck)
            {
                failures.Add("HostLink KV-8000 / XYM simulator checkbox was not found.");
                return;
            }

            if (kv8000XymSimulatorCheck.Visibility != Visibility.Visible)
            {
                failures.Add("HostLink simulator checkbox was not visible for KV-8000 / XYM.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"PLC simulator option smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            settingsWindow?.Close();
        }
    }

    private static void AssertSimulatorSettingsPreserved(
        PlcSettingsWindow settingsWindow,
        PlcSettings settings,
        string expectedHost,
        int expectedPort,
        string expectedTransport,
        List<string> failures,
        string label)
    {
        AssertEqual(expectedHost, settings.Host, failures, $"{label} host");
        AssertEqual(expectedTransport, settings.Transport, failures, $"{label} transport");
        if (settings.Port != expectedPort)
        {
            failures.Add($"{label} port: expected '{expectedPort}', got '{settings.Port}'.");
        }

        if (!settings.UseSimulator)
        {
            failures.Add($"{label}: UseSimulator was not enabled.");
        }

        var clone = settings.Clone();
        AssertEqual(expectedHost, clone.Host, failures, $"{label} cloned host");
        AssertEqual(expectedTransport, clone.Transport, failures, $"{label} cloned transport");
        if (clone.Port != expectedPort)
        {
            failures.Add($"{label} cloned port: expected '{expectedPort}', got '{clone.Port}'.");
        }

        if (settingsWindow.FindName("HostTextBox") is not TextBox hostTextBox
            || hostTextBox.IsEnabled
            || hostTextBox.Text != expectedHost)
        {
            failures.Add($"{label}: host editor was not preserved and disabled.");
        }

        if (settingsWindow.FindName("TransportCombo") is not ComboBox transportCombo
            || transportCombo.IsEnabled
            || transportCombo.SelectedValue as string != expectedTransport)
        {
            failures.Add($"{label}: transport editor was not preserved and disabled.");
        }

        if (settingsWindow.FindName("PortTextBox") is not TextBox portTextBox
            || portTextBox.IsEnabled
            || portTextBox.Text != expectedPort.ToString(CultureInfo.InvariantCulture))
        {
            failures.Add($"{label}: port editor was not preserved and disabled.");
        }
    }

    private static void SmokePlcAutoReconnectOption(List<string> failures)
    {
        PlcSettingsWindow? settingsWindow = null;
        try
        {
            var editableSettings = new PlcSettings
            {
                AutoReconnect = true,
            }.Normalize();

            settingsWindow = new PlcSettingsWindow(editableSettings, isRunning: false);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("AutoReconnectCheckBox") is not CheckBox editableCheck)
            {
                failures.Add("PLC auto reconnect checkbox was not found.");
                return;
            }

            if (editableCheck.IsChecked != true)
            {
                failures.Add("PLC auto reconnect checkbox was not enabled by default.");
            }

            editableCheck.IsChecked = false;
            PumpDispatcher();
            if (editableSettings.AutoReconnect)
            {
                failures.Add("PLC auto reconnect checkbox did not update settings.");
            }

            settingsWindow.Close();

            var readOnlySettings = new PlcSettings
            {
                AutoReconnect = true,
            }.Normalize();

            settingsWindow = new PlcSettingsWindow(readOnlySettings, isRunning: true);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("AutoReconnectCheckBox") is not CheckBox readOnlyCheck)
            {
                failures.Add("Read-only PLC auto reconnect checkbox was not found.");
                return;
            }

            if (readOnlyCheck.IsEnabled)
            {
                failures.Add("PLC auto reconnect checkbox was editable while running.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"PLC auto reconnect option smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            settingsWindow?.Close();
        }
    }

    private static void SmokePlcConnectionFailureMessage(List<string> failures)
    {
        PlcClientService? client = null;
        PlcSettingsWindow? settingsWindow = null;
        try
        {
            client = new PlcClientService();
            var settings = new PlcSettings
            {
                Protocol = "HostLink",
                Host = "127.0.0.1",
                Port = PlcSettings.DefaultHostLinkPort,
                Transport = "BAD",
                HostLinkProfile = "keyence:kv-8000",
            };

            try
            {
                client.ConnectAsync(settings).GetAwaiter().GetResult();
                failures.Add("Invalid PLC transport did not fail.");
            }
            catch (InvalidOperationException ex)
            {
                if (!ex.Message.Contains("PLC connection failed (HostLink BAD 127.0.0.1:8501", StringComparison.Ordinal)
                    || !ex.Message.Contains("Invalid PLC transport: BAD", StringComparison.Ordinal))
                {
                    failures.Add($"PLC connection failure message lacked connection context: {ex.Message}");
                }
            }

            var portSettings = new PlcSettings();
            settingsWindow = new PlcSettingsWindow(portSettings, isRunning: false);
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            PumpDispatcher();

            if (settingsWindow.FindName("HostLinkRadio") is RadioButton hostLinkRadio)
            {
                hostLinkRadio.IsChecked = true;
                PumpDispatcher();
            }

            if (portSettings.Port != PlcSettings.DefaultHostLinkPort)
            {
                failures.Add($"HostLink protocol switch did not apply default port. Expected {PlcSettings.DefaultHostLinkPort}, got {portSettings.Port}.");
            }

            if (settingsWindow.FindName("SlmpRadio") is RadioButton slmpRadio)
            {
                slmpRadio.IsChecked = true;
                PumpDispatcher();
            }

            if (portSettings.Port != PlcSettings.DefaultSlmpPort)
            {
                failures.Add($"SLMP protocol switch did not apply default port. Expected {PlcSettings.DefaultSlmpPort}, got {portSettings.Port}.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"PLC connection failure message smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            settingsWindow?.Close();
            if (client is not null)
            {
                client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static void SmokeValueBrushConverter(List<string> failures)
    {
        try
        {
            var converter = new EntryValueBrushConverter();
            AssertBrush(Color.FromRgb(0x4e, 0xe0, 0x72), converter.Convert(false, typeof(Brush), string.Empty, CultureInfo.InvariantCulture), failures, "Normal value brush");
            AssertBrush(Color.FromRgb(0xff, 0x8c, 0x00), converter.Convert(true, typeof(Brush), string.Empty, CultureInfo.InvariantCulture), failures, "Force value brush");
        }
        catch (Exception ex)
        {
            failures.Add($"Value brush smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeTodoFixes(MainViewModel viewModel, List<string> failures)
    {
        try
        {
            var ledConverter = new LedStateConverter();
            AssertBrush(Color.FromRgb(0x5a, 0x3a, 0x00), ledConverter.Convert(LedState.ForceOff, typeof(Brush), string.Empty, CultureInfo.InvariantCulture), failures, "ForceOff LED brush");

            if (viewModel.ModbusCycleText != "Modbus Cycle: --"
                || viewModel.PlcCycleText != "PLC Cycle: --"
                || viewModel.PlcModeStatus != "PLC Mode: --")
            {
                failures.Add($"Initial status text mismatch. Modbus='{viewModel.ModbusCycleText}', PLC='{viewModel.PlcCycleText}', PLC mode='{viewModel.PlcModeStatus}'.");
            }

            if (typeof(MappingEntrySettings).GetProperty("Direction") is not null)
            {
                failures.Add("MappingEntrySettings still exposes Direction.");
            }

            viewModel.IsDirty = false;
            var mapping = new MappingEntry(ModbusType.Coil, 0);
            viewModel.Mappings.Add(mapping);
            if (!viewModel.IsDirty || !viewModel.WindowTitle.EndsWith(" *", StringComparison.Ordinal))
            {
                failures.Add("Dirty state was not reflected in the window title.");
            }

            var settings = new ModbusSettings { MaxCoilAddress = 0 };
            var coil1 = new MappingEntry(ModbusType.Coil, 1);
            viewModel.Mappings.Add(coil1);
            if (viewModel.CountMappingsAboveModbusLimits(settings) == 0)
            {
                failures.Add("Mapping deletion warning count did not detect out-of-range entries.");
            }

            var csvPath = Path.Combine(Path.GetTempPath(), $"gateway-smoke-{Guid.NewGuid():N}.csv");
            try
            {
                File.WriteAllText(csvPath, "Name,Data Type,Address\r\nA,,Holding Reg 0\r\n");
                var preview = CsvImportService.Preview(csvPath, [], 100);
                if (preview.Count != 1 || preview[0].Action != CsvImportAction.Skip || preview[0].Reason != "DataType is not set.")
                {
                    failures.Add("Empty register DataType was not skipped with an explicit reason.");
                }

                var firstExisting = new MappingEntry(ModbusType.HoldingRegister, 0)
                {
                    Comment = "Old",
                    DisplayType = DisplayType.Int16,
                };
                var duplicateExisting = new MappingEntry(ModbusType.HoldingRegister, 0)
                {
                    Comment = "Duplicate",
                    DisplayType = DisplayType.Int16,
                };
                File.WriteAllText(csvPath, "Name,Data Type,Address\r\nA,INT,Holding Reg 0\r\n");
                preview = CsvImportService.Preview(csvPath, [firstExisting, duplicateExisting], 100);
                if (preview.Count != 1
                    || preview[0].Action != CsvImportAction.Update
                    || !ReferenceEquals(preview[0].Existing, firstExisting))
                {
                    failures.Add("CSV preview did not tolerate duplicate existing Modbus addresses using the first entry.");
                }
            }
            finally
            {
                File.Delete(csvPath);
            }
        }
        catch (Exception ex)
        {
            failures.Add($"TODO fixes smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeBulkAssignDeviceOptions(List<string> failures)
    {
        BulkAssignWindow? bulkAssignWindow = null;
        try
        {
            AssertSequence(["X", "Y", "M", "L", "B"], BulkAssignWindow.DeviceOptions("SLMP", ModbusType.Coil), failures, "SLMP bool bulk devices");
            AssertSequence(["X", "Y", "M", "L", "B"], BulkAssignWindow.DeviceOptions("SLMP", ModbusType.DiscreteInput), failures, "SLMP input bulk devices");
            AssertSequence(["D", "W", "R", "ZR"], BulkAssignWindow.DeviceOptions("SLMP", ModbusType.HoldingRegister), failures, "SLMP register bulk devices");
            AssertSequence(["D", "W", "R", "ZR"], BulkAssignWindow.DeviceOptions("SLMP", ModbusType.InputRegister), failures, "SLMP input register bulk devices");

            AssertSequence(["R", "B", "MR", "LR", "X", "Y", "M", "L"], BulkAssignWindow.DeviceOptions("HostLink", ModbusType.Coil), failures, "HostLink bool bulk devices");
            AssertSequence(["R", "B", "MR", "LR", "X", "Y", "M", "L"], BulkAssignWindow.DeviceOptions("HostLink", ModbusType.DiscreteInput), failures, "HostLink input bulk devices");
            AssertSequence(["DM", "EM", "FM", "ZF", "W", "D", "E", "F"], BulkAssignWindow.DeviceOptions("HostLink", ModbusType.HoldingRegister), failures, "HostLink register bulk devices");
            AssertSequence(["DM", "EM", "FM", "ZF", "W", "D", "E", "F"], BulkAssignWindow.DeviceOptions("HostLink", ModbusType.InputRegister), failures, "HostLink input register bulk devices");

            bulkAssignWindow = new BulkAssignWindow();
            bulkAssignWindow.Show();
            bulkAssignWindow.UpdateLayout();
            PumpDispatcher();

            if (bulkAssignWindow.FindName("IncrementTextBox") is not null)
            {
                failures.Add("Bulk assign increment input is still present.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Bulk assign device options smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            bulkAssignWindow?.Close();
        }
    }

    private static void SmokePlcAddressSequence(List<string> failures)
    {
        try
        {
            AssertAddress("BF", "SLMP", "melsec:iq-r", "B", "E", 1, failures, "SLMP B hex increment");
            AssertAddress("B10", "SLMP", "melsec:iq-r", "B", "F", 1, failures, "SLMP B hex carry");
            AssertAddress("W10", "SLMP", "melsec:iq-r", "W", "F", 1, failures, "SLMP W hex carry");
            AssertAddress("X10", "SLMP", "melsec:iq-r", "X", "F", 1, failures, "SLMP X hex carry");
            AssertAddress("X10", "SLMP", "melsec:iq-f", "X", "7", 1, failures, "SLMP iQ-F X octal carry");
            AssertAddress("M10", "SLMP", "melsec:iq-r", "M", "9", 1, failures, "SLMP M decimal increment");

            AssertAddress("R100", "HostLink", "melsec:iq-r", "R", "015", 1, failures, "HostLink R bit-bank carry");
            AssertAddress("MR100", "HostLink", "melsec:iq-r", "MR", "015", 1, failures, "HostLink MR bit-bank carry");
            AssertAddress("X10", "HostLink", "melsec:iq-r", "X", "0F", 1, failures, "HostLink X bit carry");
            AssertAddress("B10", "HostLink", "melsec:iq-r", "B", "F", 1, failures, "HostLink B hex carry");
            AssertAddress("DM10", "HostLink", "melsec:iq-r", "DM", "9", 1, failures, "HostLink DM decimal increment");

            if (PlcAddressSequence.TryFormat("HostLink", "melsec:iq-r", "X", "2684354560", 0, out var address, out var error))
            {
                failures.Add($"HostLink X overflow address unexpectedly formatted as {address}.");
            }
            else if (error != Loc.Text("PlcAddressOutOfRange"))
            {
                failures.Add($"HostLink X overflow address returned wrong error. Expected '{Loc.Text("PlcAddressOutOfRange")}', got '{error}'.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"PLC address sequence smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeTypedPlcAddresses(List<string> failures)
    {
        try
        {
            var bitEntry = new MappingEntry(ModbusType.Coil, 0) { PlcAddress = "M0" };
            var bitAddress = PlcTypedAddressParser.ParseRequired(bitEntry.PlcAddress, bitEntry);
            AssertEqual("M0", bitAddress.BaseAddress, failures, "Typed bit base address");
            AssertEqual("BIT", bitAddress.DataType, failures, "Typed bit data type");

            var registerEntry = new MappingEntry(ModbusType.HoldingRegister, 0) { PlcAddress = "D0" };
            var registerAddress = PlcTypedAddressParser.ParseRequired(registerEntry.PlcAddress, registerEntry);
            AssertEqual("D0", registerAddress.BaseAddress, failures, "Typed register base address");
            AssertEqual("S", registerAddress.DataType, failures, "Typed register data type");

            AssertEqual("M1", PlcTypedAddressParser.NormalizeAddress("m1"), failures, "Bulk bool address");
            AssertEqual("D1", PlcTypedAddressParser.NormalizeAddress("d1"), failures, "Bulk register address");

            if (TryParseTypedAddress(new MappingEntry(ModbusType.Coil, 2) { PlcAddress = "M2:BIT" }))
            {
                failures.Add("Bool PLC address with suffix was accepted.");
            }

            if (TryParseTypedAddress(new MappingEntry(ModbusType.HoldingRegister, 3) { PlcAddress = "D3:U" }))
            {
                failures.Add("Register PLC address with suffix was accepted.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Typed PLC address smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool TryParseTypedAddress(MappingEntry entry)
    {
        try
        {
            _ = PlcTypedAddressParser.ParseRequired(entry.PlcAddress, entry);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void AssertAddress(
        string expected,
        string protocol,
        string slmpProfile,
        string prefix,
        string start,
        int offset,
        List<string> failures,
        string label)
    {
        if (!PlcAddressSequence.TryFormat(protocol, slmpProfile, prefix, start, offset, out var actual, out var error))
        {
            failures.Add($"{label}: format failed: {error}");
            return;
        }

        AssertEqual(expected, actual, failures, label);
    }

    private static void AssertSequence(string[] expected, string[] actual, List<string> failures, string label)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            failures.Add($"{label}: expected '{string.Join(", ", expected)}', got '{string.Join(", ", actual)}'.");
        }
    }

    private static void AssertEqual(string expected, string actual, List<string> failures, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            failures.Add($"{label}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void AssertBrush(Color expected, object actual, List<string> failures, string label)
    {
        if (actual is not SolidColorBrush brush || brush.Color != expected)
        {
            failures.Add($"{label}: expected '{expected}', got '{actual}'.");
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void PumpDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
