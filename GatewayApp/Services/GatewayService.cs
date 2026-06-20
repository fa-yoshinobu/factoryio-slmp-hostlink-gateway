using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class GatewayService : IAsyncDisposable
{
    private readonly ModbusSlaveService _modbus = new();
    private readonly PlcClientService _plc = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _runCts;
    private bool _disposed;

    public GatewayService()
    {
        _plc.TraceReported += message => TraceReported?.Invoke(message);
    }

    public event Action<string>? WarningReported;

    public event Action<string>? TraceReported;

    public async Task StartAsync(PlcSettings plcSettings, ModbusSettings modbusSettings)
    {
        await _operationGate.WaitAsync();
        try
        {
            ThrowIfDisposed();
            await StopUnlockedAsync();

            _runCts = new CancellationTokenSource();
            await _modbus.StartAsync(modbusSettings);
            await _plc.ConnectAsync(plcSettings, _runCts.Token);
        }
        catch
        {
            await StopUnlockedAsync();
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync()
    {
        _runCts?.Cancel();

        await _operationGate.WaitAsync();
        try
        {
            await StopUnlockedAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public int ReadModbusRaw(MappingEntry entry) => _modbus.ReadRaw(entry);

    public void WriteModbusRaw(MappingEntry entry, int rawValue) => _modbus.WriteRaw(entry, rawValue);

    public async Task PollPlcAsync(IEnumerable<MappingEntry> mappings, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var runToken = GetRunToken(cancellationToken);

            if (!_plc.IsConnected)
            {
                throw new InvalidOperationException("PLC に接続されていません。");
            }

            foreach (var entry in mappings.Where(x => !string.IsNullOrWhiteSpace(x.PlcAddress)))
            {
                runToken.ThrowIfCancellationRequested();

                if (entry.Direction == DataDirection.FromPlc)
                {
                    var raw = await _plc.ReadRawAsync(entry, runToken);
                    if (raw.HasValue)
                    {
                        entry.RawValue = raw.Value;
                        _modbus.WriteRaw(entry, entry.EffectiveRawValue);
                    }
                }
                else
                {
                    var raw = _modbus.ReadRaw(entry);
                    if (!entry.IsForceApplied)
                    {
                        entry.RawValue = raw;
                    }

                    await _plc.WriteRawAsync(entry, entry.EffectiveRawValue, runToken);
                    entry.LastWritten = DateTime.Now;
                }
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ForceWriteAsync(MappingEntry entry, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var runToken = GetRunToken(cancellationToken);

            _modbus.WriteRaw(entry, entry.EffectiveRawValue);
            TraceReported?.Invoke(
                $"FORCE {entry.ModbusLabel} PLC={entry.PlcAddress} 方向={entry.Direction} raw={FormatRaw(entry, entry.EffectiveRawValue)}");
            TraceReported?.Invoke(
                $"MODBUS WRITE {entry.ModbusLabel} <= {FormatRaw(entry, entry.EffectiveRawValue)}");

            if (entry.Direction == DataDirection.FromPlc)
            {
                entry.LastWritten = DateTime.Now;
                return;
            }

            if (_plc.IsConnected)
            {
                var plcValue = await WriteAndCheckAsync(entry, runToken);
                if (plcValue.HasValue && !RawMatches(entry, plcValue.Value, entry.EffectiveRawValue))
                {
                    WarningReported?.Invoke(
                        $"{entry.ModbusLabel} -> {entry.PlcAddress} のPLC値不一致。送信={FormatRaw(entry, entry.EffectiveRawValue)} / PLC={FormatRaw(entry, plcValue.Value)}");
                }
            }
            else
            {
                throw new InvalidOperationException("PLC に接続されていません。");
            }

            entry.LastWritten = DateTime.Now;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static int NormalizeRaw(MappingEntry entry, int rawValue)
    {
        return entry.IsBool ? rawValue == 0 ? 0 : 1 : unchecked((ushort)rawValue);
    }

    private async Task<int?> WriteAndCheckAsync(MappingEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TraceReported?.Invoke(
            $"PLC WRITE {_plc.ActiveProtocol} {(entry.IsRegister ? "S16" : "BIT")} {entry.PlcAddress} <= {FormatRaw(entry, entry.EffectiveRawValue)}");
        await _plc.WriteRawAsync(entry, entry.EffectiveRawValue, cancellationToken);
        TraceReported?.Invoke("PLC WRITE API OK");

        var plcValue = await _plc.ReadRawAsync(entry, cancellationToken);
        TraceReported?.Invoke(
            $"PLC CHECK {_plc.ActiveProtocol} {(entry.IsRegister ? "S16" : "BIT")} {entry.PlcAddress} => {(plcValue.HasValue ? FormatRaw(entry, plcValue.Value) : "null")}");
        return plcValue;
    }

    private static bool RawMatches(MappingEntry entry, int left, int right)
    {
        return NormalizeRaw(entry, left) == NormalizeRaw(entry, right);
    }

    private static string FormatRaw(MappingEntry entry, int rawValue)
    {
        return entry.FormatRawWithDisplay(NormalizeRaw(entry, rawValue));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync();
        _disposed = true;
        _modbus.Dispose();
        _operationGate.Dispose();
    }

    private async Task StopUnlockedAsync()
    {
        _runCts?.Cancel();
        _modbus.Stop();
        await _plc.DisconnectAsync();
        _runCts?.Dispose();
        _runCts = null;
    }

    private CancellationToken GetRunToken(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runCts = _runCts ?? throw new InvalidOperationException("通信が起動していません。");
        return runCts.Token;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
