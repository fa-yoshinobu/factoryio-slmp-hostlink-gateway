using GatewayApp.Models;
using PlcComm.KvHostLink;
using PlcComm.Slmp;
using System.Net.Sockets;

namespace GatewayApp.Services;

public enum PlcOperationMode
{
    Unknown,
    Run,
    Stop,
    Pause,
    Program,
}

public sealed class PlcClientService : IAsyncDisposable
{
    private static readonly SlmpTargetAddress SlmpOwnStationTarget = new(0x00, 0xFF, SlmpModuleIo.OwnStation, 0x00);

    private QueuedSlmpClient? _slmp;
    private QueuedKvHostLinkClient? _hostLink;

    public event Action<string>? TraceReported;

    public bool IsConnected => _slmp?.IsOpen == true || _hostLink?.IsOpen == true;

    public string ActiveProtocol => _slmp is not null
        ? "SLMP"
        : _hostLink is not null ? "HostLink" : "None";

    public async Task<PlcOperationMode> ReadOperationModeAsync(CancellationToken cancellationToken = default)
    {
        if (_slmp is not null)
        {
            var state = await _slmp.ReadCpuOperationStateAsync(cancellationToken).ConfigureAwait(false);
            return state.Status switch
            {
                SlmpCpuOperationStatus.Run => PlcOperationMode.Run,
                SlmpCpuOperationStatus.Stop => PlcOperationMode.Stop,
                SlmpCpuOperationStatus.Pause => PlcOperationMode.Pause,
                _ => PlcOperationMode.Unknown,
            };
        }

        if (_hostLink is not null)
        {
            var mode = await _hostLink.ConfirmOperatingModeAsync(cancellationToken).ConfigureAwait(false);
            return mode switch
            {
                KvPlcMode.Run => PlcOperationMode.Run,
                KvPlcMode.Program => PlcOperationMode.Program,
                _ => PlcOperationMode.Unknown,
            };
        }

        throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
    }

    public async Task ConnectAsync(PlcSettings settings, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        var normalized = settings.Clone();
        if (normalized.UseSimulator)
        {
            normalized.ApplySimulatorEndpoint();
        }

        try
        {
            if (normalized.Protocol.Equals("SLMP", StringComparison.OrdinalIgnoreCase))
            {
                var options = CreateSlmpConnectionOptions(normalized);
                _slmp = await SlmpClientFactory.OpenAndConnectAsync(options, cancellationToken).ConfigureAwait(false);
                TraceReported?.Invoke(
                    $"PLC CONNECT SLMP endpoint={normalized.Host}:{normalized.Port} transport={options.Transport} profile={_slmp.PlcProfile} frame={_slmp.FrameType} mode={_slmp.CompatibilityMode} target={_slmp.TargetAddress}");
                return;
            }

            var hostLinkOptions = new KvHostLinkConnectionOptions(
                normalized.Host,
                normalized.Port,
                ParseHostLinkTransport(normalized.Transport),
                normalized.HostLinkProfile,
                TimeSpan.FromSeconds(Math.Max(1, normalized.TimeoutSec)));
            _hostLink = await KvHostLinkClientFactory.OpenAndConnectAsync(hostLinkOptions, cancellationToken).ConfigureAwait(false);
            TraceReported?.Invoke(
                $"PLC CONNECT HostLink endpoint={normalized.Host}:{normalized.Port} transport={hostLinkOptions.Transport} profile={_hostLink.PlcProfile}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(BuildConnectionFailureMessage(normalized, ex), ex);
        }
    }

    internal static SlmpConnectionOptions CreateSlmpConnectionOptions(PlcSettings settings)
    {
        var profile = ParseSlmpProfile(settings.SlmpProfile);
        return new SlmpConnectionOptions(
            settings.Host,
            profile,
            settings.Port,
            ParseSlmpTransport(settings.Transport),
            SlmpOwnStationTarget)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSec)),
        };
    }

    public async Task DisconnectAsync()
    {
        if (_slmp is not null)
        {
            await _slmp.DisposeAsync().ConfigureAwait(false);
            _slmp = null;
        }

        if (_hostLink is not null)
        {
            await _hostLink.DisposeAsync().ConfigureAwait(false);
            _hostLink = null;
        }
    }

    public async Task<int?> ReadRawAsync(MappingEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.PlcAddress))
        {
            return null;
        }

        var address = PlcTypedAddressParser.ParseRequired(entry.PlcAddress, entry);
        if (_slmp is not null)
        {
            var value = await _slmp.ReadTypedAsync(address.BaseAddress, address.DataType, cancellationToken).ConfigureAwait(false);
            return ConvertPlcValueToRaw(value);
        }

        if (_hostLink is not null)
        {
            var value = address.DataType == "BIT"
                ? await ReadHostLinkBitAsync(address.BaseAddress, cancellationToken).ConfigureAwait(false)
                : await _hostLink.ReadTypedAsync(address.BaseAddress, address.DataType, cancellationToken).ConfigureAwait(false);
            return ConvertPlcValueToRaw(value);
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<MappingEntry, int>> ReadRawBlocksAsync(
        IEnumerable<MappingEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<MappingEntry, int>();
        var (blocks, singles) = BuildBlocks(entries);

        foreach (var single in singles)
        {
            var raw = await ReadRawAsync(single, cancellationToken).ConfigureAwait(false);
            if (raw.HasValue)
            {
                result[single] = raw.Value;
            }
        }

        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_slmp is not null)
            {
                await ReadSlmpBlockAsync(block, result, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (_hostLink is not null)
            {
                await ReadHostLinkBlockAsync(block, result, cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }

    public async Task WriteRawAsync(MappingEntry entry, int rawValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.PlcAddress))
        {
            throw new InvalidOperationException(Loc.Format("PlcAddressMissing", entry.ModbusLabel));
        }

        if (_slmp is not null)
        {
            var address = PlcTypedAddressParser.ParseRequired(entry.PlcAddress, entry);
            await _slmp.WriteTypedAsync(address.BaseAddress, address.DataType, ConvertRawForSlmp(address, rawValue), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_hostLink is not null)
        {
            var address = PlcTypedAddressParser.ParseRequired(entry.PlcAddress, entry);
            await WriteHostLinkTypedAsync(address, rawValue, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
    }

    public async Task WriteRawBlocksAsync(
        IReadOnlyList<(MappingEntry Entry, int RawValue)> writes,
        CancellationToken cancellationToken = default)
    {
        var rawByEntry = writes.ToDictionary(x => x.Entry, x => x.RawValue);
        var (blocks, singles) = BuildBlocks(writes.Select(x => x.Entry));

        foreach (var single in singles)
        {
            await WriteRawAsync(single, rawByEntry[single], cancellationToken).ConfigureAwait(false);
        }

        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_slmp is not null)
            {
                await WriteSlmpBlockAsync(block, rawByEntry, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (_hostLink is not null)
            {
                await WriteHostLinkBlockAsync(block, rawByEntry, cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private static object ConvertRawForSlmp(PlcTypedAddress address, int rawValue)
    {
        return address.DataType switch
        {
            "BIT" => rawValue != 0,
            "U" => unchecked((ushort)rawValue),
            _ => ConvertRawToSignedWord(rawValue),
        };
    }

    private Task WriteHostLinkTypedAsync(PlcTypedAddress address, int rawValue, CancellationToken cancellationToken)
    {
        return address.DataType switch
        {
            "BIT" => _hostLink!.WriteAsync(address.BaseAddress, rawValue != 0 ? 1 : 0, dataFormat: string.Empty, cancellationToken),
            "U" => _hostLink!.WriteTypedAsync(address.BaseAddress, "U", unchecked((ushort)rawValue), cancellationToken),
            _ => _hostLink!.WriteTypedAsync(address.BaseAddress, "S", ConvertRawToSignedWord(rawValue), cancellationToken),
        };
    }

    private async Task<object> ReadHostLinkBitAsync(string baseAddress, CancellationToken cancellationToken)
    {
        var tokens = await _hostLink!.ReadAsync(baseAddress, dataFormat: string.Empty, cancellationToken).ConfigureAwait(false);
        return tokens.FirstOrDefault() ?? "0";
    }

    private static short ConvertRawToSignedWord(int rawValue)
    {
        return unchecked((short)(ushort)rawValue);
    }

    private static int ConvertPlcValueToRaw(object value)
    {
        return value switch
        {
            bool b => b ? 1 : 0,
            short s => unchecked((ushort)s),
            ushort u => u,
            int i => unchecked((ushort)i),
            uint u => unchecked((ushort)u),
            _ => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private async Task ReadSlmpBlockAsync(
        PlcAccessBlock block,
        Dictionary<MappingEntry, int> result,
        CancellationToken cancellationToken)
    {
        var start = block.StartSlmpAddress
            ?? throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
        if (block.IsRegister)
        {
            var words = await _slmp!.ReadWordsRawAsync(start, checked((ushort)block.Count), cancellationToken)
                .ConfigureAwait(false);
            foreach (var point in block.Points)
            {
                result[point.Entry] = words[checked((int)(point.Number - block.StartNumber))];
            }

            return;
        }

        var bits = await _slmp!.ReadBitsAsync(start, checked((ushort)block.Count), cancellationToken)
            .ConfigureAwait(false);
        foreach (var point in block.Points)
        {
            result[point.Entry] = bits[checked((int)(point.Number - block.StartNumber))] ? 1 : 0;
        }
    }

    private async Task ReadHostLinkBlockAsync(
        PlcAccessBlock block,
        Dictionary<MappingEntry, int> result,
        CancellationToken cancellationToken)
    {
        if (block.IsRegister)
        {
            var words = await _hostLink!.ReadWordsAsync(block.StartAddressText, block.Count, cancellationToken)
                .ConfigureAwait(false);
            foreach (var point in block.Points)
            {
                result[point.Entry] = words[checked((int)(point.Number - block.StartNumber))];
            }

            return;
        }

        var values = await _hostLink!.ReadConsecutiveAsync(block.StartAddressText, block.Count, string.Empty, cancellationToken)
            .ConfigureAwait(false);
        foreach (var point in block.Points)
        {
            result[point.Entry] = ConvertPlcValueToRaw(values[checked((int)(point.Number - block.StartNumber))]);
        }
    }

    private async Task WriteSlmpBlockAsync(
        PlcAccessBlock block,
        Dictionary<MappingEntry, int> rawByEntry,
        CancellationToken cancellationToken)
    {
        var start = block.StartSlmpAddress
            ?? throw new InvalidOperationException(Loc.Text("PlcNotConnected"));
        if (block.IsRegister)
        {
            var words = new ushort[block.Count];
            foreach (var point in block.Points)
            {
                words[checked((int)(point.Number - block.StartNumber))] = unchecked((ushort)rawByEntry[point.Entry]);
            }

            await _slmp!.WriteWordsAsync(start, words, cancellationToken).ConfigureAwait(false);
            return;
        }

        var bits = new bool[block.Count];
        foreach (var point in block.Points)
        {
            bits[checked((int)(point.Number - block.StartNumber))] = rawByEntry[point.Entry] != 0;
        }

        await _slmp!.WriteBitsAsync(start, bits, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteHostLinkBlockAsync(
        PlcAccessBlock block,
        Dictionary<MappingEntry, int> rawByEntry,
        CancellationToken cancellationToken)
    {
        if (block.IsRegister)
        {
            var words = new ushort[block.Count];
            foreach (var point in block.Points)
            {
                words[checked((int)(point.Number - block.StartNumber))] = unchecked((ushort)rawByEntry[point.Entry]);
            }

            await _hostLink!.WriteWordsSingleRequestAsync(block.StartAddressText, words, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var bits = new int[block.Count];
        foreach (var point in block.Points)
        {
            bits[checked((int)(point.Number - block.StartNumber))] = rawByEntry[point.Entry] != 0 ? 1 : 0;
        }

        await _hostLink!.WriteConsecutiveAsync(block.StartAddressText, bits, string.Empty, cancellationToken)
            .ConfigureAwait(false);
    }

    private (List<PlcAccessBlock> Blocks, List<MappingEntry> Singles) BuildBlocks(IEnumerable<MappingEntry> entries)
        => PlcAccessBlockPlanner.BuildBlocks(entries, TryCreateAccessPoint);

    private bool TryCreateAccessPoint(MappingEntry entry, out PlcAccessPoint point)
    {
        point = default!;
        try
        {
            if (_slmp is not null)
            {
                var typedAddress = PlcTypedAddressParser.ParseRequired(entry.PlcAddress, entry);
                var address = SlmpAddress.Parse(typedAddress.BaseAddress, _slmp.PlcProfile);
                point = new PlcAccessPoint(
                    entry,
                    entry.IsRegister,
                    address.Code.ToString(),
                    address.Number,
                    typedAddress.BaseAddress,
                    address);
                return true;
            }

            if (_hostLink is not null)
            {
                var typedAddress = PlcTypedAddressParser.ParseRequired(entry.PlcAddress, entry);
                var address = KvHostLinkAddress.Parse(typedAddress.BaseAddress);
                point = new PlcAccessPoint(
                    entry,
                    entry.IsRegister,
                    $"{address.DeviceType}:{address.Suffix}",
                    checked((uint)address.Number),
                    typedAddress.BaseAddress,
                    null);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static SlmpPlcProfile ParseSlmpProfile(string text)
    {
        return SlmpPlcProfiles.Parse(text.Trim());
    }

    private static SlmpTransportMode ParseSlmpTransport(string text)
    {
        return text.Trim().ToUpperInvariant() switch
        {
            "TCP" => SlmpTransportMode.Tcp,
            "UDP" => SlmpTransportMode.Udp,
            _ => throw new InvalidOperationException(Loc.Format("InvalidPlcTransport", text)),
        };
    }

    private static HostLinkTransportMode ParseHostLinkTransport(string text)
    {
        return text.Trim().ToUpperInvariant() switch
        {
            "TCP" => HostLinkTransportMode.Tcp,
            "UDP" => HostLinkTransportMode.Udp,
            _ => throw new InvalidOperationException(Loc.Format("InvalidPlcTransport", text)),
        };
    }

    private static string BuildConnectionFailureMessage(PlcSettings settings, Exception exception)
    {
        var profile = settings.Protocol.Equals("SLMP", StringComparison.OrdinalIgnoreCase)
            ? settings.SlmpProfile
            : settings.HostLinkProfile;
        return Loc.Format(
            "PlcConnectionFailed",
            settings.Protocol,
            settings.Transport,
            settings.Host,
            settings.Port,
            profile,
            settings.TimeoutSec,
            DescribeConnectionFailure(exception));
    }

    private static string DescribeConnectionFailure(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return Loc.Text("ResponseTimeout");
        }

        var socketException = FindSocketException(exception);
        if (socketException is not null)
        {
            return $"SocketError={socketException.SocketErrorCode}, NativeError={socketException.NativeErrorCode}: {socketException.Message}";
        }

        return exception.Message;
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }
        }

        return null;
    }

}
