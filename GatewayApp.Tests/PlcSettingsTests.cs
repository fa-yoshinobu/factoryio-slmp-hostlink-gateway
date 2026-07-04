using GatewayApp.Models;
using GatewayApp.Services;
using System.IO;

namespace GatewayApp.Tests;

public sealed class PlcSettingsTests
{
    [Fact]
    public void Clone_copies_auto_reconnect()
    {
        var settings = new PlcSettings
        {
            AutoReconnect = false,
        };

        var clone = settings.Clone();

        Assert.False(clone.AutoReconnect);
    }

    [Fact]
    public void Load_defaults_auto_reconnect_for_older_settings_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gateway-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "plc": {
                    "protocol": "SLMP",
                    "host": "192.168.250.100",
                    "port": 1025,
                    "transport": "TCP",
                    "timeoutSec": 3,
                    "pollingMs": 50,
                    "slmpProfile": "melsec:iq-r",
                    "hostLinkProfile": "keyence:kv-8000",
                    "useSimulator": false
                  },
                  "modbus": {
                    "listenIp": "127.0.0.1",
                    "port": 502,
                    "unitId": 1,
                    "realScale": 100,
                    "maxCoilAddress": null,
                    "maxDiscreteInputAddress": null,
                    "maxHoldingRegisterAddress": null,
                    "maxInputRegisterAddress": null
                  },
                  "realScale": 100,
                  "mappings": []
                }
                """);

            var loaded = new SettingsService().Load(path);

            Assert.True(loaded.Plc.AutoReconnect);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_persists_auto_reconnect()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gateway-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings
            {
                Plc = new PlcSettings { AutoReconnect = false },
                Modbus = new ModbusSettings(),
                RealScale = 100,
                Mappings = [],
            };

            new SettingsService().Save(path, settings);

            var json = File.ReadAllText(path);
            Assert.Contains("\"autoReconnect\": false", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
