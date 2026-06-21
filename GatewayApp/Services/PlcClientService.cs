using GatewayApp.Models;
using PlcComm.KvHostLink;
using PlcComm.Slmp;
using System.Net.Sockets;

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
                    $"PLC CONNECT SLMP transport={options.Transport} profile={_slmp.PlcProfile} frame={_slmp.FrameType} mode={_slmp.CompatibilityMode} target={_slmp.TargetAddress}");
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
                $"PLC CONNECT HostLink transport={hostLinkOptions.Transport} profile={_hostLink.PlcProfile} host={normalized.Host}:{normalized.Port}");
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
