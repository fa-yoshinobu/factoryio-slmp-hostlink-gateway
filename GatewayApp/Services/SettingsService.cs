using GatewayApp.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GatewayApp.Services;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FactoryIOGateway",
        "settings.json");

    public string SettingsDirectory => Path.GetDirectoryName(SettingsPath) ?? Environment.CurrentDirectory;

    public AppSettings? Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        return Load(SettingsPath);
    }

    public AppSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("設定ファイルのパスが空です。", nameof(path));
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
            ?? throw new InvalidDataException("設定ファイルの内容が空です。");
    }

    public void Save(AppSettings settings)
    {
        Save(SettingsPath, settings);
    }

    public void Save(string path, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("設定ファイルのパスが空です。", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(path, json);
    }
}
