using GatewayApp.Models;
using PlcComm.KvHostLink;
using PlcComm.Slmp;
using System.Net.Sockets;

namespace GatewayApp.Services;

public sealed class PlcClientService : IAsyncDisposable
{
    private const int MaxBlockPoints = 64;
    private QueuedSlmpClient? _slmp;
    private QueuedKvHostLinkClient? _hostLink;

    public event Action<string>? TraceReported;

    public bool IsConnected => _slmp?.IsOpen == true || _hostLink?.IsOpen == true;

    public string ActiveProtocol => _slmp is not null
        ? "SLMP"
        : _hostLink is not null ? "HostLink" : "None";

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
                var profile = ParseSlmpProfile(normalized.SlmpProfile);
                var options = new SlmpConnectionOptions(normalized.Host, profile)
                {
                    Port = normalized.Port,
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, normalized.TimeoutSec)),
                    Transport = ParseSlmpTransport(normalized.Transport),
                };
                _slmp = await SlmpClientFactory.OpenAndConnectAsync(options, cancellationToken).ConfigureAwait(false);
                TraceReported?.Invoke(
                    $"PLC CONNECT SLMP endpoint={normalized.Host}:{normalized.Port} transport={options.Transport} profile={_slmp.PlcProfile} frame={_slmp.FrameType} mode={_slmp.CompatibilityMode} target={_slmp.TargetAddress}");
                return;
            }

            var hostLinkOptions = new KvHostLinkConnectionOptions(
                normalized.Host,
                normalized.HostLinkProfile,
                Port: normalized.Port,
                Timeout: TimeSpan.FromSeconds(Math.Max(1, normalized.TimeoutSec)),
                Transport: ParseHostLinkTransport(normalized.Transport));
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

        if (_slmp is not null)
        {
            if (entry.IsRegister)
            {
                var registerValue = await _slmp.ReadTypedAsync(entry.PlcAddress, "S", cancellationToken).ConfigureAwait(false);
                return ConvertPlcValueToRaw(registerValue);
            }

            var value = await _slmp.ReadTypedAsync(entry.PlcAddress, GetPlcDType(entry), cancellationToken).ConfigureAwait(false);
            return ConvertPlcValueToRaw(value);
        }

        if (_hostLink is not null)
        {
            if (entry.IsRegister)
            {
                var registerValue = await _hostLink.ReadTypedAsync(entry.PlcAddress, "S", cancellationToken).ConfigureAwait(false);
                return ConvertPlcValueToRaw(registerValue);
            }

            var value = await _hostLink.ReadTypedAsync(entry.PlcAddress, GetHostLinkDType(entry), cancellationToken).ConfigureAwait(false);
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
            if (entry.IsRegister)
            {
                await _slmp.WriteTypedAsync(entry.PlcAddress, "S", ConvertRawToSignedWord(rawValue), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await _slmp.WriteTypedAsync(entry.PlcAddress, GetPlcDType(entry), rawValue != 0, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_hostLink is not null)
        {
            if (entry.IsBool)
            {
                await _hostLink.WriteTypedAsync(entry.PlcAddress, string.Empty, rawValue != 0 ? 1 : 0, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _hostLink.WriteTypedAsync(entry.PlcAddress, "S", ConvertRawToSignedWord(rawValue), cancellationToken)
                    .ConfigureAwait(false);
            }

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

    private static string GetPlcDType(MappingEntry entry)
    {
        return entry.IsBool ? "BIT" : "S";
    }

    private static string GetHostLinkDType(MappingEntry entry)
    {
        return entry.IsBool ? string.Empty : "S";
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
    {
        var points = new List<PlcAccessPoint>();
        var singles = new List<MappingEntry>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.PlcAddress))
            {
                singles.Add(entry);
                continue;
            }

            if (TryCreateAccessPoint(entry, out var point))
            {
                points.Add(point);
            }
            else
            {
                singles.Add(entry);
            }
        }

        var blocks = new List<PlcAccessBlock>();
        PlcAccessBlock? current = null;

        foreach (var point in points
            .OrderBy(x => x.IsRegister)
            .ThenBy(x => x.DeviceKey, StringComparer.Ordinal)
            .ThenBy(x => x.Number))
        {
            if (current is null || !current.CanAdd(point))
            {
                current = new PlcAccessBlock(point);
                blocks.Add(current);
                continue;
            }

            current.Add(point);
        }

        return (blocks, singles);
    }

    private bool TryCreateAccessPoint(MappingEntry entry, out PlcAccessPoint point)
    {
        point = default!;
        try
        {
            if (_slmp is not null)
            {
                var address = SlmpAddress.Parse(entry.PlcAddress, _slmp.PlcProfile);
                point = new PlcAccessPoint(
                    entry,
                    entry.IsRegister,
                    address.Code.ToString(),
                    address.Number,
                    entry.PlcAddress,
                    address);
                return true;
            }

            if (_hostLink is not null)
            {
                var address = KvHostLinkAddress.Parse(entry.PlcAddress);
                point = new PlcAccessPoint(
                    entry,
                    entry.IsRegister,
                    $"{address.DeviceType}:{address.Suffix}",
                    checked((uint)address.Number),
                    entry.PlcAddress,
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

    private sealed record PlcAccessPoint(
        MappingEntry Entry,
        bool IsRegister,
        string DeviceKey,
        uint Number,
        string AddressText,
        SlmpDeviceAddress? SlmpAddress);

    private sealed class PlcAccessBlock
    {
        public PlcAccessBlock(PlcAccessPoint firstPoint)
        {
            IsRegister = firstPoint.IsRegister;
            DeviceKey = firstPoint.DeviceKey;
            StartNumber = firstPoint.Number;
            EndNumber = firstPoint.Number;
            StartAddressText = firstPoint.AddressText;
            StartSlmpAddress = firstPoint.SlmpAddress;
            Points.Add(firstPoint);
        }

        public bool IsRegister { get; }

        public string DeviceKey { get; }

        public uint StartNumber { get; }

        public uint EndNumber { get; private set; }

        public string StartAddressText { get; }

        public SlmpDeviceAddress? StartSlmpAddress { get; }

        public List<PlcAccessPoint> Points { get; } = [];

        public int Count => checked((int)(EndNumber - StartNumber + 1));

        public bool CanAdd(PlcAccessPoint point)
        {
            if (point.IsRegister != IsRegister || point.DeviceKey != DeviceKey)
            {
                return false;
            }

            var isSameOrNext = point.Number <= EndNumber
                || (EndNumber < uint.MaxValue && point.Number == EndNumber + 1);
            return isSameOrNext && Count < MaxBlockPoints;
        }

        public void Add(PlcAccessPoint point)
        {
            Points.Add(point);
            if (point.Number > EndNumber)
            {
                EndNumber = point.Number;
            }
        }
    }
}
