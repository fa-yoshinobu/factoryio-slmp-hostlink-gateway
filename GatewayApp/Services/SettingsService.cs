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

    public string SettingsDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public AppSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(Loc.Text("SettingsPathEmpty"), nameof(path));
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
            ?? throw new InvalidDataException(Loc.Text("SettingsContentEmpty"));
    }

    public void Save(string path, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(Loc.Text("SettingsPathEmpty"), nameof(path));
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
