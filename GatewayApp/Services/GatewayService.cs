using GatewayApp.Models;
using System.Diagnostics;

namespace GatewayApp.Services;

internal interface IGatewayService : IAsyncDisposable
{
    event Action<string>? WarningReported;

    event Action<string>? TraceReported;

    int ModbusClientCount { get; }

    double? ModbusRequestCycleMilliseconds { get; }

    double? PlcPollCycleMilliseconds { get; }

    Task StartAsync(PlcSettings plcSettings, ModbusSettings modbusSettings);

    Task StopAsync();

    int ReadModbusRaw(MappingEntry entry);

    void WriteModbusRaw(MappingEntry entry, int rawValue);

    Task DisconnectPlcAfterFaultAsync();

    Task ReconnectPlcAsync(PlcSettings settings, CancellationToken cancellationToken = default);

    Task<PlcOperationMode> ReadPlcOperationModeAsync(CancellationToken cancellationToken = default);

    Task PollPlcAsync(IEnumerable<MappingEntry> mappings, CancellationToken cancellationToken = default);

    Task ForceWriteAsync(MappingEntry entry, CancellationToken cancellationToken = default);
}

public sealed class GatewayService : IGatewayService
{
    private readonly ModbusSlaveService _modbus = new();
    private readonly PlcClientService _plc = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Lock _plcCycleLock = new();
    private readonly Dictionary<MappingEntry, ToPlcWriteSnapshot> _lastToPlcWrites = [];
    private CancellationTokenSource? _runCts;
    private long? _lastPlcPollStartTicks;
    private double? _plcPollCycleMilliseconds;
    private bool _disposed;

    public GatewayService()
    {
        _plc.TraceReported += message => TraceReported?.Invoke(message);
        _modbus.UnexpectedExceptionReported += exception =>
            WarningReported?.Invoke($"{exception.GetType().Name}: {exception.Message}");
    }

    public event Action<string>? WarningReported;

    public event Action<string>? TraceReported;

    public int ModbusClientCount => _modbus.ConnectedClientCount;

    public double? ModbusRequestCycleMilliseconds => _modbus.RequestCycleMilliseconds;

    public double? PlcPollCycleMilliseconds
    {
        get
        {
            lock (_plcCycleLock)
            {
                return _plcPollCycleMilliseconds;
            }
        }
    }

    public async Task StartAsync(PlcSettings plcSettings, ModbusSettings modbusSettings)
    {
        await _operationGate.WaitAsync();
        try
        {
            ThrowIfDisposed();
            await StopUnlockedAsync();
            ResetPlcCycleStats();
            ClearToPlcWriteSnapshots();

            _runCts = new CancellationTokenSource();
            await _modbus.StartAsync(modbusSettings);
            await _plc.ConnectAsync(plcSettings, _runCts.Token);
        }
        catch
        {
            await StopAfterFailedStartAsync();
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync()
    {
        CancelRun();

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

    public async Task DisconnectPlcAfterFaultAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            if (_runCts is null)
            {
                return;
            }

            await DisconnectPlcUnlockedAsync().ConfigureAwait(false);
            ClearToPlcWriteSnapshots();
            ResetPlcCycleStats();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ReconnectPlcAsync(PlcSettings settings, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var runToken = GetRunToken(cancellationToken);
            if (_plc.IsConnected)
            {
                return;
            }

            await _plc.ConnectAsync(settings, runToken).ConfigureAwait(false);
            ResetPlcCycleStats();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlcOperationMode> ReadPlcOperationModeAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var runToken = GetRunToken(cancellationToken);

            if (!_plc.IsConnected)
            {
                throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
            }

            return await _plc.ReadOperationModeAsync(runToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PollPlcAsync(IEnumerable<MappingEntry> mappings, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var runToken = GetRunToken(cancellationToken);

            if (!_plc.IsConnected)
            {
                throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
            }

            var activeMappings = mappings.Where(x => !string.IsNullOrWhiteSpace(x.PlcAddress)).ToList();
            if (activeMappings.Count == 0)
            {
                return;
            }

            RecordPlcPollStart();

            var fromPlcEntries = activeMappings
                .Where(x => x.Direction == DataDirection.FromPlc)
                .ToList();
            var fromPlcValues = await _plc.ReadRawBlocksAsync(fromPlcEntries, runToken).ConfigureAwait(false);
            foreach (var entry in fromPlcEntries)
            {
                runToken.ThrowIfCancellationRequested();
                if (fromPlcValues.TryGetValue(entry, out var raw))
                {
                    entry.RawValue = raw;
                    _modbus.WriteRaw(entry, entry.EffectiveRawValue);
                }
            }

            var toPlcEntries = activeMappings
                .Where(x => x.Direction == DataDirection.ToPlc)
                .ToList();
            PruneToPlcWriteSnapshots(toPlcEntries);
            var writes = new List<(MappingEntry Entry, int RawValue)>(toPlcEntries.Count);
            foreach (var entry in toPlcEntries)
            {
                runToken.ThrowIfCancellationRequested();
                var raw = _modbus.ReadRaw(entry);
                if (!entry.IsForceApplied)
                {
                    entry.RawValue = raw;
                }

                var effectiveRawValue = entry.EffectiveRawValue;
                if (ShouldWriteToPlc(entry, effectiveRawValue))
                {
                    writes.Add((entry, effectiveRawValue));
                }
            }

            if (writes.Count > 0)
            {
                await _plc.WriteRawBlocksAsync(writes, runToken).ConfigureAwait(false);
                foreach (var (entry, rawValue) in writes)
                {
                    RememberToPlcWrite(entry, rawValue);
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
                $"FORCE {entry.ModbusLabel} PLC={entry.PlcAddress} direction={entry.Direction} raw={FormatRaw(entry, entry.EffectiveRawValue)}");
            TraceReported?.Invoke(
                $"MODBUS WRITE {entry.ModbusLabel} <= {FormatRaw(entry, entry.EffectiveRawValue)}");

            if (entry.Direction == DataDirection.FromPlc)
            {
                return;
            }

            if (_plc.IsConnected)
            {
                var plcValue = await WriteAndCheckAsync(entry, runToken);
                if (plcValue.HasValue && !RawMatches(entry, plcValue.Value, entry.EffectiveRawValue))
                {
                    WarningReported?.Invoke(
                        Loc.Format("PlcMismatch", entry.ModbusLabel, entry.PlcAddress, FormatRaw(entry, entry.EffectiveRawValue), FormatRaw(entry, plcValue.Value)));
                }
                else
                {
                    RememberToPlcWrite(entry, entry.EffectiveRawValue);
                }
            }
            else
            {
                throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
            }

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
        await _plc.WriteRawAsync(entry, entry.EffectiveRawValue, cancellationToken);
        return await _plc.ReadRawAsync(entry, cancellationToken);
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
        CancelRun();
        _modbus.Stop();
        try
        {
            await DisconnectPlcUnlockedAsync();
        }
        catch (Exception ex) when (CommunicationExceptionClassifier.IsExpectedLocalStop(ex))
        {
        }

        _runCts?.Dispose();
        _runCts = null;
        ResetPlcCycleStats();
        ClearToPlcWriteSnapshots();
    }

    private async Task DisconnectPlcUnlockedAsync()
    {
        try
        {
            await _plc.DisconnectAsync();
        }
        catch (Exception ex) when (CommunicationExceptionClassifier.IsExpectedLocalStop(ex))
        {
        }
    }

    private async Task StopAfterFailedStartAsync()
    {
        try
        {
            await StopUnlockedAsync();
        }
        catch
        {
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void CancelRun()
    {
        var runCts = _runCts;
        if (runCts is null)
        {
            return;
        }

        try
        {
            runCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private CancellationToken GetRunToken(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runCts = _runCts ?? throw new InvalidOperationException(Loc.Text("CommunicationNotRunning"));
        return runCts.Token;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void RecordPlcPollStart()
    {
        var nowTicks = Stopwatch.GetTimestamp();
        lock (_plcCycleLock)
        {
            if (_lastPlcPollStartTicks.HasValue)
            {
                _plcPollCycleMilliseconds = (nowTicks - _lastPlcPollStartTicks.Value) * 1000.0 / Stopwatch.Frequency;
            }

            _lastPlcPollStartTicks = nowTicks;
        }
    }

    private void ResetPlcCycleStats()
    {
        lock (_plcCycleLock)
        {
            _lastPlcPollStartTicks = null;
            _plcPollCycleMilliseconds = null;
        }
    }

    private bool ShouldWriteToPlc(MappingEntry entry, int rawValue)
    {
        var normalizedRawValue = NormalizeRaw(entry, rawValue);
        return !_lastToPlcWrites.TryGetValue(entry, out var previous)
            || previous.PlcAddress != entry.PlcAddress
            || previous.RawValue != normalizedRawValue;
    }

    private void RememberToPlcWrite(MappingEntry entry, int rawValue)
    {
        _lastToPlcWrites[entry] = new ToPlcWriteSnapshot(
            entry.PlcAddress,
            NormalizeRaw(entry, rawValue));
    }

    private void PruneToPlcWriteSnapshots(IReadOnlyCollection<MappingEntry> activeToPlcEntries)
    {
        var activeEntries = activeToPlcEntries.ToHashSet();
        foreach (var entry in _lastToPlcWrites.Keys.ToList())
        {
            if (!activeEntries.Contains(entry))
            {
                _lastToPlcWrites.Remove(entry);
            }
        }
    }

    private void ClearToPlcWriteSnapshots()
    {
        _lastToPlcWrites.Clear();
    }

    private readonly record struct ToPlcWriteSnapshot(string PlcAddress, int RawValue);
}
