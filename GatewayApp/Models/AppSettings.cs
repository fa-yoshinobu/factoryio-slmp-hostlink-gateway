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

    public DataDirection Direction { get; set; }

    public DisplayType DisplayType { get; set; }

    public string Comment { get; set; } = string.Empty;
}

public sealed class PlcSettings
{
    public const string DefaultSlmpProfile = "melsec:iq-r";

    public string Protocol { get; set; } = "SLMP";

    public string Host { get; set; } = "192.168.250.100";

    public int Port { get; set; } = 1025;

    public int TimeoutSec { get; set; } = 3;

    public int PollingMs { get; set; } = 100;

    public string SlmpProfile { get; set; } = DefaultSlmpProfile;

    public PlcSettings Clone()
    {
        return new PlcSettings
        {
            Protocol = string.IsNullOrWhiteSpace(Protocol) ? "SLMP" : Protocol,
            Host = string.IsNullOrWhiteSpace(Host) ? "192.168.250.100" : Host,
            Port = Port,
            TimeoutSec = TimeoutSec,
            PollingMs = PollingMs,
            SlmpProfile = NormalizeSlmpProfile(SlmpProfile),
        }.Normalize();
    }

    public PlcSettings Normalize()
    {
        Protocol = string.IsNullOrWhiteSpace(Protocol) ? "SLMP" : Protocol;
        Host = string.IsNullOrWhiteSpace(Host) ? "192.168.250.100" : Host;
        Port = Port is < 1 or > 65535 ? 1025 : Port;
        TimeoutSec = Math.Max(1, TimeoutSec);
        PollingMs = Math.Max(10, PollingMs);
        SlmpProfile = NormalizeSlmpProfile(SlmpProfile);
        return this;
    }

    public static string NormalizeSlmpProfile(string? value)
    {
        return value?.Trim() switch
        {
            null or "" => DefaultSlmpProfile,
            "iQ-R" => "melsec:iq-r",
            "iQ-F" => "melsec:iq-f",
            "Q Series" => "melsec:qcpu",
            "L Series" => "melsec:lcpu",
            var text => text,
        };
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
