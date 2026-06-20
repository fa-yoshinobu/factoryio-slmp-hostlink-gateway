namespace GatewayApp.Models;

public sealed class AppSettings
{
    public PlcSettings Plc { get; set; } = new();

    public ModbusSettings Modbus { get; set; } = new();

    public int RealScale { get; set; } = 100;

    public List<MappingEntrySettings> Mappings { get; set; } = [];
}

public sealed class MappingEntrySettings
{
    public ModbusType ModbusType { get; set; }

    public int ModbusAddress { get; set; }

    public string PlcAddress { get; set; } = string.Empty;

    public DisplayType DisplayType { get; set; }

    public string Comment { get; set; } = string.Empty;
}

public sealed class PlcSettings
{
    public const string DefaultSlmpProfile = "melsec:iq-r";
    public const string DefaultHostLinkProfile = "keyence:kv-8000";
    public const string DefaultTransport = "TCP";
    public const int DefaultSlmpPort = 1025;
    public const int DefaultHostLinkPort = 8501;
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
    private static readonly IReadOnlyDictionary<string, string> SlmpProfileAliases =
        SlmpProfileOptionsInternal
            .Select(option => (Alias: option.Label, option.Value))
            .Concat(new (string Alias, string Value)[]
            {
                ("Q Series", "melsec:qcpu"),
                ("L Series", "melsec:lcpu"),
            })
            .ToDictionary(option => option.Alias, option => option.Value, StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> HostLinkProfileAliases =
        HostLinkProfileOptionsInternal.ToDictionary(option => option.Label, option => option.Value, StringComparer.Ordinal);

    public static IReadOnlyList<(string Value, string Label)> SlmpProfileOptions => SlmpProfileOptionsInternal;

    public static IReadOnlyList<(string Value, string Label)> HostLinkProfileOptions => HostLinkProfileOptionsInternal;

    public string Protocol { get; set; } = "SLMP";

    public string Host { get; set; } = "192.168.250.100";

    public int Port { get; set; } = DefaultSlmpPort;

    public string Transport { get; set; } = DefaultTransport;

    public int TimeoutSec { get; set; } = 3;

    public int PollingMs { get; set; } = 100;

    public string SlmpProfile { get; set; } = DefaultSlmpProfile;

    public string HostLinkProfile { get; set; } = DefaultHostLinkProfile;

    public PlcSettings Clone()
    {
        return new PlcSettings
        {
            Protocol = string.IsNullOrWhiteSpace(Protocol) ? "SLMP" : Protocol,
            Host = string.IsNullOrWhiteSpace(Host) ? "192.168.250.100" : Host,
            Port = Port,
            Transport = NormalizeTransport(Transport),
            TimeoutSec = TimeoutSec,
            PollingMs = PollingMs,
            SlmpProfile = NormalizeSlmpProfile(SlmpProfile),
            HostLinkProfile = NormalizeHostLinkProfile(HostLinkProfile),
        }.Normalize();
    }

    public PlcSettings Normalize()
    {
        Protocol = string.IsNullOrWhiteSpace(Protocol) ? "SLMP" : Protocol;
        Host = string.IsNullOrWhiteSpace(Host) ? "192.168.250.100" : Host;
        Port = Port is < 1 or > 65535 ? DefaultPortForProtocol(Protocol) : Port;
        Transport = NormalizeTransport(Transport);
        TimeoutSec = Math.Max(1, TimeoutSec);
        PollingMs = Math.Max(10, PollingMs);
        SlmpProfile = NormalizeSlmpProfile(SlmpProfile);
        HostLinkProfile = NormalizeHostLinkProfile(HostLinkProfile);
        return this;
    }

    public static string NormalizeSlmpProfile(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultSlmpProfile;
        }

        return SlmpProfileAliases.TryGetValue(text, out var canonical) ? canonical : text;
    }

    public static int DefaultPortForProtocol(string protocol)
    {
        return protocol.Equals("HostLink", StringComparison.OrdinalIgnoreCase)
            ? DefaultHostLinkPort
            : DefaultSlmpPort;
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

        return HostLinkProfileAliases.TryGetValue(text, out var canonical) ? canonical : text;
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
    public string ListenIp { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 502;

    public byte UnitId { get; set; } = 1;

    public int RealScale { get; set; } = 100;

    public int? MaxCoilAddress { get; set; }

    public int? MaxDiscreteInputAddress { get; set; }

    public int? MaxHoldingRegisterAddress { get; set; }

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
