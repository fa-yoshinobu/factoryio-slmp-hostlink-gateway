using System.Text.Json.Serialization;

namespace GatewayApp.Models;

public sealed class AppSettings
{
    [JsonRequired]
    public PlcSettings Plc { get; set; } = new();

    [JsonRequired]
    public ModbusSettings Modbus { get; set; } = new();

    [JsonRequired]
    public int RealScale { get; set; } = 100;

    [JsonRequired]
    public List<MappingEntrySettings> Mappings { get; set; } = [];
}

public sealed class MappingEntrySettings
{
    [JsonRequired]
    public ModbusType ModbusType { get; set; }

    [JsonRequired]
    public int ModbusAddress { get; set; }

    [JsonRequired]
    public string PlcAddress { get; set; } = string.Empty;

    [JsonRequired]
    public DisplayType DisplayType { get; set; }

    [JsonRequired]
    public string Comment { get; set; } = string.Empty;
}

public sealed class PlcSettings
{
    public const string DefaultSlmpProfile = "melsec:iq-r";
    public const string DefaultHostLinkProfile = "keyence:kv-8000";
    public const string DefaultTransport = "TCP";
    public const int DefaultSlmpPort = 1025;
    public const int DefaultHostLinkPort = 8501;
    public const string SimulatorHost = "127.0.0.1";
    public const int GxSimulator3Port = 5511;
    public const int KvStudioSimulatorPort = 8501;
    private static readonly (string Value, string Label)[] SlmpProfileOptionsInternal =
    [
        ("melsec:iq-r", "iQ-R"),
        ("melsec:iq-f", "iQ-F"),
        ("melsec:iq-l", "iQ-L"),
        ("melsec:mx-r", "MX-R"),
        ("melsec:mx-f", "MX-F"),
        ("melsec:qnudv", "QnUDV"),
        ("melsec:qnu", "QnU"),
        ("melsec:qcpu", "QCPU"),
        ("melsec:lcpu", "LCPU"),
    ];
    private static readonly (string Value, string Label)[] HostLinkProfileOptionsInternal =
    [
        ("keyence:kv-nano", "KV-Nano"),
        ("keyence:kv-nano-xym", "KV-Nano / XYM"),
        ("keyence:kv-3000", "KV-3000"),
        ("keyence:kv-3000-xym", "KV-3000 / XYM"),
        ("keyence:kv-5000", "KV-5000"),
        ("keyence:kv-5000-xym", "KV-5000 / XYM"),
        ("keyence:kv-7000", "KV-7000"),
        ("keyence:kv-7000-xym", "KV-7000 / XYM"),
        ("keyence:kv-8000", "KV-8000"),
        ("keyence:kv-8000-xym", "KV-8000 / XYM"),
        ("keyence:kv-x500", "KV-X500"),
        ("keyence:kv-x500-xym", "KV-X500 / XYM"),
    ];
    public static IReadOnlyList<(string Value, string Label)> SlmpProfileOptions => SlmpProfileOptionsInternal;

    public static IReadOnlyList<(string Value, string Label)> HostLinkProfileOptions => HostLinkProfileOptionsInternal;

    [JsonRequired]
    public string Protocol { get; set; } = "SLMP";

    [JsonRequired]
    public string Host { get; set; } = "192.168.250.100";

    [JsonRequired]
    public int Port { get; set; } = DefaultSlmpPort;

    [JsonRequired]
    public string Transport { get; set; } = DefaultTransport;

    [JsonRequired]
    public int TimeoutSec { get; set; } = 3;

    [JsonRequired]
    public int PollingMs { get; set; } = 50;

    [JsonRequired]
    public string SlmpProfile { get; set; } = DefaultSlmpProfile;

    [JsonRequired]
    public string HostLinkProfile { get; set; } = DefaultHostLinkProfile;

    [JsonRequired]
    public bool UseSimulator { get; set; }

    public bool AutoReconnect { get; set; } = true;

    public PlcSettings Clone()
    {
        var clone = new PlcSettings
        {
            Protocol = string.IsNullOrWhiteSpace(Protocol) ? "SLMP" : Protocol,
            Host = string.IsNullOrWhiteSpace(Host) ? "192.168.250.100" : Host,
            Port = Port,
            Transport = NormalizeTransport(Transport),
            TimeoutSec = TimeoutSec,
            PollingMs = PollingMs,
            SlmpProfile = NormalizeSlmpProfile(SlmpProfile),
            HostLinkProfile = NormalizeHostLinkProfile(HostLinkProfile),
            UseSimulator = UseSimulator,
            AutoReconnect = AutoReconnect,
        }.Normalize();

        return clone;
    }

    public PlcSettings Normalize()
    {
        Protocol = string.IsNullOrWhiteSpace(Protocol) ? "SLMP" : Protocol;
        Host = string.IsNullOrWhiteSpace(Host) ? "192.168.250.100" : Host;
        Transport = NormalizeTransport(Transport);
        TimeoutSec = Math.Max(1, TimeoutSec);
        PollingMs = Math.Max(10, PollingMs);
        SlmpProfile = NormalizeSlmpProfile(SlmpProfile);
        HostLinkProfile = NormalizeHostLinkProfile(HostLinkProfile);
        UseSimulator = UseSimulator && SupportsSimulator(Protocol, SlmpProfile, HostLinkProfile);
        Port = Port is < 1 or > 65535 ? DefaultPortForProtocol(Protocol) : Port;

        return this;
    }

    public static string NormalizeSlmpProfile(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultSlmpProfile;
        }

        return text;
    }

    public static int DefaultPortForProtocol(string protocol)
    {
        return protocol.Equals("HostLink", StringComparison.OrdinalIgnoreCase)
            ? DefaultHostLinkPort
            : DefaultSlmpPort;
    }

    public static bool SupportsSimulator(string protocol, string? slmpProfile, string? hostLinkProfile)
    {
        return protocol.Equals("HostLink", StringComparison.OrdinalIgnoreCase)
            ? IsHostLinkSimulatorProfile(hostLinkProfile)
            : IsSlmpSimulatorProfile(slmpProfile);
    }

    public static bool IsSlmpSimulatorProfile(string? value)
    {
        var profile = NormalizeSlmpProfile(value);
        return profile is "melsec:iq-r" or "melsec:iq-l";
    }

    public static bool IsHostLinkSimulatorProfile(string? value)
    {
        var profile = NormalizeHostLinkProfile(value);
        return profile is "keyence:kv-x500"
            or "keyence:kv-x500-xym"
            or "keyence:kv-8000"
            or "keyence:kv-8000-xym";
    }

    public void ApplySimulatorEndpoint()
    {
        Host = SimulatorHost;
        Port = Protocol.Equals("HostLink", StringComparison.OrdinalIgnoreCase)
            ? KvStudioSimulatorPort
            : GxSimulator3Port;
        Transport = "TCP";
    }

    public static string NormalizeTransport(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            null or "" => DefaultTransport,
            "TCP" => "TCP",
            "UDP" => "UDP",
            var text => text,
        };
    }

    public static string NormalizeHostLinkProfile(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultHostLinkProfile;
        }

        return text;
    }

    public static string FormatSlmpProfile(string? value)
    {
        return FormatProfile(value, SlmpProfileOptionsInternal, "MELSEC");
    }

    public static string FormatHostLinkProfile(string? value)
    {
        return FormatProfile(value, HostLinkProfileOptionsInternal, "KEYENCE KV");
    }

    private static string FormatProfile(string? value, IReadOnlyList<(string Value, string Label)> options, string emptyLabel)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return emptyLabel;
        }

        foreach (var option in options)
        {
            if (string.Equals(option.Value, text, StringComparison.Ordinal))
            {
                return option.Label;
            }
        }

        return text;
    }
}

public sealed class ModbusSettings
{
    [JsonRequired]
    public string ListenIp { get; set; } = "127.0.0.1";

    [JsonRequired]
    public int Port { get; set; } = 502;

    [JsonRequired]
    public byte UnitId { get; set; } = 1;

    [JsonRequired]
    public int RealScale { get; set; } = 100;

    [JsonRequired]
    public int? MaxCoilAddress { get; set; }

    [JsonRequired]
    public int? MaxDiscreteInputAddress { get; set; }

    [JsonRequired]
    public int? MaxHoldingRegisterAddress { get; set; }

    [JsonRequired]
    public int? MaxInputRegisterAddress { get; set; }

    public ModbusSettings Clone()
    {
        return new ModbusSettings
        {
            ListenIp = ListenIp,
            Port = Port,
            UnitId = UnitId,
            RealScale = RealScale,
            MaxCoilAddress = MaxCoilAddress,
            MaxDiscreteInputAddress = MaxDiscreteInputAddress,
            MaxHoldingRegisterAddress = MaxHoldingRegisterAddress,
            MaxInputRegisterAddress = MaxInputRegisterAddress,
        };
    }
}
