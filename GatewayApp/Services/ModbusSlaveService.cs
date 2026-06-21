using GatewayApp.Models;
using NModbus;
using NModbus.Data;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace GatewayApp.Services;

public sealed class ModbusSlaveService : IDisposable
{
    private CancellationTokenSource? _listenCts;
    private IModbusSlaveNetwork? _network;
    private Task? _listenTask;
    private readonly AsyncLocal<int> _internalAccessDepth = new();
    private readonly Lock _activityLock = new();
    private readonly Dictionary<ModbusActivityKey, long> _lastRequestTicksByKey = [];
    private double? _requestCycleMilliseconds;

    public SlaveDataStore DataStore { get; } = new();

    public ModbusSlaveService()
    {
        DataStore.CoilDiscretes.BeforeRead += (_, e) =>
            RecordExternalActivity("R", ModbusType.Coil, e.StartAddress, e.NumberOfPoints);
        DataStore.CoilDiscretes.BeforeWrite += (_, e) =>
            RecordExternalActivity("W", ModbusType.Coil, e.StartAddress, e.NumberOfPoints);
        DataStore.CoilInputs.BeforeRead += (_, e) =>
            RecordExternalActivity("R", ModbusType.DiscreteInput, e.StartAddress, e.NumberOfPoints);
        DataStore.CoilInputs.BeforeWrite += (_, e) =>
            RecordExternalActivity("W", ModbusType.DiscreteInput, e.StartAddress, e.NumberOfPoints);
        DataStore.HoldingRegisters.BeforeRead += (_, e) =>
            RecordExternalActivity("R", ModbusType.HoldingRegister, e.StartAddress, e.NumberOfPoints);
        DataStore.HoldingRegisters.BeforeWrite += (_, e) =>
            RecordExternalActivity("W", ModbusType.HoldingRegister, e.StartAddress, e.NumberOfPoints);
        DataStore.InputRegisters.BeforeRead += (_, e) =>
            RecordExternalActivity("R", ModbusType.InputRegister, e.StartAddress, e.NumberOfPoints);
        DataStore.InputRegisters.BeforeWrite += (_, e) =>
            RecordExternalActivity("W", ModbusType.InputRegister, e.StartAddress, e.NumberOfPoints);
    }

    public event Action<Exception>? UnexpectedExceptionReported;

    public int ConnectedClientCount => _network is IModbusTcpSlaveNetwork tcpNetwork
        ? tcpNetwork.Masters.Count
        : 0;

    public double? RequestCycleMilliseconds
    {
        get
        {
            lock (_activityLock)
            {
                return _requestCycleMilliseconds;
            }
        }
    }

    public Task StartAsync(ModbusSettings settings)
    {
        Stop();
        ResetActivityStats();

        var address = IPAddress.Parse(settings.ListenIp);
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(address, settings.Port);
            listener.Start();

            var factory = new ModbusFactory();
            _network = factory.CreateSlaveNetwork(listener);
            _network.AddSlave(factory.CreateSlave(settings.UnitId, DataStore));

            _listenCts = new CancellationTokenSource();
            _listenTask = _network.ListenAsync(_listenCts.Token);
            if (_listenTask.IsFaulted)
            {
                var exception = _listenTask.Exception?.GetBaseException()
                    ?? new InvalidOperationException(Loc.Text("ModbusTcpStartFailed"));
                ObserveIfFaulted(_listenTask);
                return Task.FromException(exception);
            }

            _ = ObserveListenTaskAsync(_listenTask);
            return Task.CompletedTask;
        }
        catch
        {
            listener?.Stop();
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        var listenTask = _listenTask;

        if (_listenCts is not null)
        {
            _listenCts.Cancel();
            _listenCts.Dispose();
            _listenCts = null;
        }

        _network?.Dispose();
        _network = null;
        ObserveIfFaulted(listenTask);
        _listenTask = null;
        ResetActivityStats();
    }

    public int ReadRaw(MappingEntry entry)
    {
        BeginInternalAccess();
        try
        {
            var address = checked((ushort)entry.ModbusAddress);
            return entry.ModbusType switch
            {
                ModbusType.Coil => DataStore.CoilDiscretes.ReadPoints(address, 1)[0] ? 1 : 0,
                ModbusType.DiscreteInput => DataStore.CoilInputs.ReadPoints(address, 1)[0] ? 1 : 0,
                ModbusType.HoldingRegister => DataStore.HoldingRegisters.ReadPoints(address, 1)[0],
                ModbusType.InputRegister => DataStore.InputRegisters.ReadPoints(address, 1)[0],
                _ => 0,
            };
        }
        finally
        {
            EndInternalAccess();
        }
    }

    public void WriteRaw(MappingEntry entry, int rawValue)
    {
        BeginInternalAccess();
        try
        {
            var address = checked((ushort)entry.ModbusAddress);
            var word = unchecked((ushort)rawValue);

            switch (entry.ModbusType)
            {
                case ModbusType.Coil:
                    DataStore.CoilDiscretes.WritePoints(address, [rawValue != 0]);
                    break;
                case ModbusType.DiscreteInput:
                    DataStore.CoilInputs.WritePoints(address, [rawValue != 0]);
                    break;
                case ModbusType.HoldingRegister:
                    DataStore.HoldingRegisters.WritePoints(address, [word]);
                    break;
                case ModbusType.InputRegister:
                    DataStore.InputRegisters.WritePoints(address, [word]);
                    break;
            }
        }
        finally
        {
            EndInternalAccess();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task ObserveListenTaskAsync(Task listenTask)
    {
        try
        {
            await listenTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception ex)
        {
            UnexpectedExceptionReported?.Invoke(ex);
        }
    }

    private static void ObserveIfFaulted(Task? task)
    {
        if (task?.IsFaulted == true)
        {
            _ = task.Exception;
        }
    }

    private void RecordExternalActivity(string operation, ModbusType modbusType, ushort startAddress, ushort numberOfPoints)
    {
        if (_internalAccessDepth.Value > 0)
        {
            return;
        }

        var nowTicks = Stopwatch.GetTimestamp();
        var key = new ModbusActivityKey(operation, modbusType, startAddress, numberOfPoints);

        lock (_activityLock)
        {
            if (_lastRequestTicksByKey.TryGetValue(key, out var previousTicks))
            {
                _requestCycleMilliseconds = (nowTicks - previousTicks) * 1000.0 / Stopwatch.Frequency;
            }

            _lastRequestTicksByKey[key] = nowTicks;
        }
    }

    private void ResetActivityStats()
    {
        lock (_activityLock)
        {
            _lastRequestTicksByKey.Clear();
            _requestCycleMilliseconds = null;
        }
    }

    private void BeginInternalAccess()
    {
        _internalAccessDepth.Value++;
    }

    private void EndInternalAccess()
    {
        _internalAccessDepth.Value = Math.Max(0, _internalAccessDepth.Value - 1);
    }

    private readonly record struct ModbusActivityKey(
        string Operation,
        ModbusType ModbusType,
        ushort StartAddress,
        ushort NumberOfPoints);
}
