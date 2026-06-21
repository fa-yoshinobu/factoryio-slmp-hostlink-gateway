using GatewayApp.Models;
using NModbus;
using NModbus.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace GatewayApp.Services;

public sealed class ModbusSlaveService : IDisposable
{
    private CancellationTokenSource? _listenCts;
    private IModbusSlaveNetwork? _network;
    private Task? _listenTask;

    public SlaveDataStore DataStore { get; } = new();

    public event Action<Exception>? UnexpectedExceptionReported;

    public int ConnectedClientCount => _network is IModbusTcpSlaveNetwork tcpNetwork
        ? tcpNetwork.Masters.Count
        : 0;

    public Task StartAsync(ModbusSettings settings)
    {
        Stop();

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
    }

    public int ReadRaw(MappingEntry entry)
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

    public void WriteRaw(MappingEntry entry, int rawValue)
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
}
