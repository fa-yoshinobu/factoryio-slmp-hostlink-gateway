using GatewayApp.Models;
using GatewayApp.Services;
using PlcComm.KvHostLink;
using PlcComm.Slmp;
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

    [Fact]
    public void Slmp_profile_options_include_local_ethernet_unit_profiles()
    {
        var values = PlcSettings.SlmpProfileOptions.Select(option => option.Value).ToArray();

        Assert.Contains("melsec:iq-r:rj71en71", values);
        Assert.Contains("melsec:mx-r:rj71en71", values);
        Assert.Contains("melsec:lcpu:lj71e71-100", values);
        Assert.Contains("melsec:qnu:qj71e71-100", values);
        Assert.Contains("melsec:qnudv:qj71e71-100", values);
        Assert.Contains("melsec:qcpu:qj71e71-100", values);
        Assert.DoesNotContain("melsec:qcpu", values);
    }

    [Fact]
    public void Slmp_profile_options_are_accepted_by_local_slmp_library()
    {
        foreach (var option in PlcSettings.SlmpProfileOptions)
        {
            var profile = SlmpPlcProfiles.Parse(option.Value);
            Assert.NotEqual(SlmpPlcProfile.Unspecified, profile);
            Assert.Equal(SlmpPlcProfiles.GetDisplayName(profile), option.Label);
        }
    }

    [Fact]
    public void Slmp_connection_options_use_own_station_target()
    {
        var options = PlcClientService.CreateSlmpConnectionOptions(new PlcSettings().Normalize());

        Assert.Equal(new SlmpTargetAddress(0x00, 0xFF, SlmpModuleIo.OwnStation, 0x00), options.Target);
    }

    [Fact]
    public void HostLink_profile_options_show_local_kv_model_families()
    {
        Assert.Equal(
            "KEYENCE KV-7000",
            PlcSettings.FormatHostLinkProfile("keyence:kv-7000"));
        Assert.Equal(
            "KEYENCE KV-X500 (XYM)",
            PlcSettings.FormatHostLinkProfile("keyence:kv-x500-xym"));
    }

    [Fact]
    public void HostLink_profile_options_are_accepted_by_local_kv_library()
    {
        var names = KvHostLinkPlcProfiles.GetNames();
        foreach (var option in PlcSettings.HostLinkProfileOptions)
        {
            Assert.Contains(option.Value, names);
            Assert.Equal(KvHostLinkPlcProfiles.GetDisplayName(option.Value), option.Label);
        }
    }
}
