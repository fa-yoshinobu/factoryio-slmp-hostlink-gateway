using GatewayApp.Models;
using PlcComm.KvHostLink;
using PlcComm.Slmp;

namespace GatewayApp.Services;

public sealed class PlcClientService : IAsyncDisposable
{
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

        if (settings.Protocol.Equals("SLMP", StringComparison.OrdinalIgnoreCase))
        {
            var profile = ParseSlmpProfile(settings.SlmpProfile);
            var options = new SlmpConnectionOptions(settings.Host, profile)
            {
                Port = settings.Port,
                Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSec)),
            };
            _slmp = await SlmpClientFactory.OpenAndConnectAsync(options, cancellationToken).ConfigureAwait(false);
            TraceReported?.Invoke(
                $"PLC CONNECT SLMP profile={_slmp.PlcProfile} frame={_slmp.FrameType} mode={_slmp.CompatibilityMode} target={_slmp.TargetAddress}");
            return;
        }

        var hostLinkOptions = new KvHostLinkConnectionOptions(
            settings.Host,
            settings.HostLinkProfile,
            Port: settings.Port,
            Timeout: TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSec)));
        _hostLink = await KvHostLinkClientFactory.OpenAndConnectAsync(hostLinkOptions, cancellationToken).ConfigureAwait(false);
        TraceReported?.Invoke($"PLC CONNECT HostLink profile={_hostLink.PlcProfile} host={settings.Host}:{settings.Port}");
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

    public async Task WriteRawAsync(MappingEntry entry, int rawValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.PlcAddress))
        {
            throw new InvalidOperationException($"{entry.ModbusLabel} の PLC アドレスが未設定です。");
        }

        if (_slmp is not null)
        {
            if (entry.IsRegister)
            {
                await _slmp.WriteTypedAsync(entry.PlcAddress, "S", ConvertRawToSignedWord(rawValue), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await _slmp.WriteTypedAsync(entry.PlcAddress, GetPlcDType(entry), ConvertRawForWrite(entry, rawValue), cancellationToken)
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

        throw new InvalidOperationException("PLC に接続されていません。");
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

    private static object ConvertRawForWrite(MappingEntry entry, int rawValue)
    {
        return entry.IsBool ? rawValue != 0 : ConvertRawToSignedWord(rawValue);
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
            _ => Convert.ToInt32(value),
        };
    }

    private static SlmpPlcProfile ParseSlmpProfile(string text)
    {
        return text.Trim() switch
        {
            "iQ-R" => SlmpPlcProfiles.Parse("melsec:iq-r"),
            "iQ-F" => SlmpPlcProfiles.Parse("melsec:iq-f"),
            "Q Series" => SlmpPlcProfiles.Parse("melsec:qcpu"),
            "L Series" => SlmpPlcProfiles.Parse("melsec:lcpu"),
            _ => SlmpPlcProfiles.Parse(text),
        };
    }
}
