using GatewayApp;
using GatewayApp.Models;
using GatewayApp.Services;
using GatewayApp.ViewModels;
using GatewayApp.Views.Dialogs;
using System.Net;
using System.Net.Sockets;
using System.Windows;
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

            if (window.DataContext is not MainViewModel viewModel)
            {
                failures.Add("MainWindow DataContext is not MainViewModel.");
            }
            else
            {
                SmokeErrorLogBinding(window, viewModel, failures);
                SmokeRegisterForceInput(viewModel, failures);
                SmokeScaledRawFormatting(failures);
                SmokeLogWindow(viewModel, failures);
                SmokeModbusStartStop(failures);
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
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

            if (viewModel.ErrorLogs.Count == 0)
            {
                failures.Add("Error log entry was not added.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Error log binding smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SmokeRegisterForceInput(MainViewModel viewModel, List<string> failures)
    {
        try
        {
            viewModel.ApplyModbusSettings(new ModbusSettings
            {
                RealScale = 100,
                MaxHoldingRegisterAddress = 1,
                MaxInputRegisterAddress = 1,
            });

            var holding = viewModel.Mappings.Single(x =>
                x.ModbusType == ModbusType.HoldingRegister && x.ModbusAddress == 0);
            holding.RawValue = 321;

            var input = viewModel.Mappings.Single(x =>
                x.ModbusType == ModbusType.InputRegister && x.ModbusAddress == 0);
            input.RawValue = 125;

            viewModel.ToggleForce();

            if (holding.ForceValue != 321)
            {
                failures.Add($"Force enable did not inherit holding register value. Expected 321, got {holding.ForceValue}.");
            }

            if (input.ForceValue != 125)
            {
                failures.Add($"Force enable did not inherit input register value. Expected 125, got {input.ForceValue}.");
            }

            holding.DisplayType = DisplayType.Int16;
            viewModel.BeginRegisterEdit(holding);
            holding.ForceEditText = "1234";
            viewModel.CommitRegisterForceAsync(holding, clear: false).GetAwaiter().GetResult();

            if (holding.ForceValue != 1234)
            {
                failures.Add($"Holding register force value mismatch. Expected 1234, got {holding.ForceValue}.");
            }

            input.DisplayType = DisplayType.ScaledReal;
            viewModel.BeginRegisterEdit(input);
            input.ForceEditText = "1.25";
            viewModel.CommitRegisterForceAsync(input, clear: false).GetAwaiter().GetResult();

            if (input.ForceValue != 125)
            {
                failures.Add($"Input register force value mismatch. Expected 125, got {input.ForceValue}.");
            }

            viewModel.BeginRegisterEdit(input);
            input.ForceEditText = "not-a-number";
            viewModel.CommitRegisterForceAsync(input, clear: false).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(viewModel.LastError))
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

            modbus.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        catch (Exception ex)
        {
            failures.Add($"Modbus start/stop smoke failed: {ex.GetType().Name}: {ex.Message}");
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

            if (viewModel.ErrorLogs.Count == 0)
            {
                failures.Add("Log window smoke did not add a log entry.");
            }

            viewModel.ClearLogs();
            PumpDispatcher();

            if (viewModel.ErrorLogs.Count != 0)
            {
                failures.Add($"Clear logs failed. Count={viewModel.ErrorLogs.Count}.");
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

    private static void SmokeScaledRawFormatting(List<string> failures)
    {
        try
        {
            var entry = new MappingEntry(ModbusType.HoldingRegister, 0)
            {
                DisplayType = DisplayType.ScaledReal,
                RealScale = 100,
            };

            AssertEqual("1000 (10.00)", entry.FormatRawWithDisplay(1000), failures, "Scaled raw 1000");
            AssertEqual("1010 (10.10)", entry.FormatRawWithDisplay(1010), failures, "Scaled raw 1010");
        }
        catch (Exception ex)
        {
            failures.Add($"Scaled raw formatting smoke failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AssertEqual(string expected, string actual, List<string> failures, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
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
