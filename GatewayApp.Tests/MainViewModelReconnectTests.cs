using GatewayApp.Models;
using GatewayApp.Services;
using GatewayApp.ViewModels;
using System.IO;
using System.Windows.Threading;

namespace GatewayApp.Tests;

public sealed class MainViewModelReconnectTests
{
    [Fact]
    public async Task Poll_failure_with_auto_reconnect_keeps_gateway_running_and_recovers()
    {
        var now = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var service = new FakeGatewayService
        {
            PollException = new IOException("lost"),
        };
        await using var viewModel = CreateViewModel(service, () => now);
        viewModel.IsRunning = true;
        viewModel.Plc.AutoReconnect = true;

        await viewModel.PollOnceAsync();

        Assert.True(viewModel.IsRunning);
        Assert.Equal(1, service.DisconnectCount);
        Assert.Equal(0, service.StopCount);
        Assert.Equal("ModbusTcpListening", viewModel.ModbusStatus);
        Assert.Equal("PlcReconnecting", viewModel.PlcStatus);
        Assert.Contains(viewModel.Logs, x => x.Message == "PlcReconnectLost");
        Assert.Contains(viewModel.Logs, x => x.Message == "PlcReconnectStarted");

        service.PollException = null;
        await viewModel.PollOnceAsync();
        Assert.Equal(0, service.ReconnectCount);

        now = now.AddSeconds(1);
        await viewModel.PollOnceAsync();

        Assert.True(viewModel.IsRunning);
        Assert.Equal(1, service.ReconnectCount);
        Assert.Equal(0, service.StopCount);
        Assert.Equal("PlcConnected", viewModel.PlcStatus);
        Assert.Contains(viewModel.Logs, x => x.Message == "PlcReconnectRecovered");
    }

    [Fact]
    public async Task Poll_failure_with_auto_reconnect_off_stops_gateway()
    {
        var service = new FakeGatewayService
        {
            PollException = new IOException("lost"),
        };
        await using var viewModel = CreateViewModel(service);
        viewModel.IsRunning = true;
        viewModel.Plc.AutoReconnect = false;

        await viewModel.PollOnceAsync();

        Assert.False(viewModel.IsRunning);
        Assert.Equal(0, service.DisconnectCount);
        Assert.Equal(1, service.StopCount);
    }

    [Fact]
    public async Task Reconnect_failures_back_off_without_retry_log_spam()
    {
        var now = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var service = new FakeGatewayService
        {
            PollException = new IOException("lost"),
        };
        service.ReconnectFailures.Enqueue(new IOException("still down"));
        service.ReconnectFailures.Enqueue(new IOException("still down again"));
        await using var viewModel = CreateViewModel(service, () => now);
        viewModel.IsRunning = true;

        await viewModel.PollOnceAsync();
        service.PollException = null;

        now = now.AddSeconds(1);
        await viewModel.PollOnceAsync();
        now = now.AddSeconds(2);
        await viewModel.PollOnceAsync();

        Assert.True(viewModel.IsRunning);
        Assert.Equal(2, service.ReconnectCount);
        Assert.Equal(1, viewModel.Logs.Count(x => x.Message == "PlcReconnectLost"));
        Assert.Equal(1, viewModel.Logs.Count(x => x.Message == "PlcReconnectStarted"));
        Assert.DoesNotContain(viewModel.Logs, x => x.Message == "PlcReconnectRecovered");
    }

    [Fact]
    public async Task Force_during_reconnect_logs_skip_without_stopping()
    {
        var now = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var service = new FakeGatewayService
        {
            PollException = new IOException("lost"),
        };
        await using var viewModel = CreateViewModel(service, () => now);
        var coil = new MappingEntry(ModbusType.Coil, 0)
        {
            PlcAddress = "M0",
            RawValue = 0,
        };
        viewModel.Mappings.Add(coil);
        viewModel.IsRunning = true;
        viewModel.ToggleForceX();

        await viewModel.PollOnceAsync();
        await viewModel.CycleBoolForceAsync(coil);

        Assert.True(viewModel.IsRunning);
        Assert.Equal(1, service.ForceWriteCount);
        Assert.Equal(0, service.StopCount);
        Assert.Contains(viewModel.Logs, x => x.Message == "ForcePlcWriteSkippedReconnecting");
    }

    [Fact]
    public async Task Stop_during_reconnect_cancels_future_reconnect_attempts()
    {
        var now = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var service = new FakeGatewayService
        {
            PollException = new IOException("lost"),
        };
        await using var viewModel = CreateViewModel(service, () => now);
        viewModel.IsRunning = true;

        await viewModel.PollOnceAsync();
        await viewModel.ToggleRunningCommand.ExecuteAsync(null);

        now = now.AddSeconds(60);
        await viewModel.PollOnceAsync();

        Assert.False(viewModel.IsRunning);
        Assert.Equal(1, service.StopCount);
        Assert.Equal(0, service.ReconnectCount);
        Assert.Equal("PlcDisconnected", viewModel.PlcStatus);
    }

    private static MainViewModel CreateViewModel(
        FakeGatewayService service,
        Func<DateTime>? utcNow = null)
    {
        return new MainViewModel(
            service,
            Dispatcher.CurrentDispatcher,
            utcNow,
            startPollTimer: false);
    }

    private sealed class FakeGatewayService : IGatewayService
    {
        public event Action<string>? WarningReported
        {
            add { }
            remove { }
        }

        public event Action<string>? TraceReported
        {
            add { }
            remove { }
        }

        public Queue<Exception> ReconnectFailures { get; } = new();

        public Exception? PollException { get; set; }

        public int ModbusClientCount { get; set; }

        public double? ModbusRequestCycleMilliseconds { get; set; }

        public double? PlcPollCycleMilliseconds { get; set; }

        public int StopCount { get; private set; }

        public int DisconnectCount { get; private set; }

        public int ReconnectCount { get; private set; }

        public int ForceWriteCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public Task StartAsync(PlcSettings plcSettings, ModbusSettings modbusSettings)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public int ReadModbusRaw(MappingEntry entry)
        {
            return entry.EffectiveRawValue;
        }

        public void WriteModbusRaw(MappingEntry entry, int rawValue)
        {
        }

        public Task DisconnectPlcAfterFaultAsync()
        {
            DisconnectCount++;
            return Task.CompletedTask;
        }

        public Task ReconnectPlcAsync(PlcSettings settings, CancellationToken cancellationToken = default)
        {
            ReconnectCount++;
            if (ReconnectFailures.TryDequeue(out var exception))
            {
                throw exception;
            }

            return Task.CompletedTask;
        }

        public Task<PlcOperationMode> ReadPlcOperationModeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PlcOperationMode.Run);
        }

        public Task PollPlcAsync(IEnumerable<MappingEntry> mappings, CancellationToken cancellationToken = default)
        {
            if (PollException is not null)
            {
                throw PollException;
            }

            return Task.CompletedTask;
        }

        public Task ForceWriteAsync(MappingEntry entry, CancellationToken cancellationToken = default)
        {
            ForceWriteCount++;
            throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
        }
    }
}
