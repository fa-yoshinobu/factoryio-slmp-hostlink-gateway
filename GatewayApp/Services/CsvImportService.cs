using CsvHelper;
using CsvHelper.Configuration;
using GatewayApp.Models;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace GatewayApp.Services;

public static class CsvImportService
{
    public static IReadOnlyList<CsvImportPreviewItem> Preview(string path, IEnumerable<MappingEntry> existingMappings, int realScale)
    {
        var existing = new Dictionary<(ModbusType, int), MappingEntry>();
        foreach (var mapping in existingMappings)
        {
            existing.TryAdd((mapping.ModbusType, mapping.ModbusAddress), mapping);
        }

        var items = new List<CsvImportPreviewItem>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var reader = File.OpenText(path);
        using var csv = new CsvReader(reader, config, leaveOpen: false);
        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            var name = GetFieldAny(csv, "Name", "Tag", "Tag Name", "タグ名") ?? string.Empty;
            var factoryType = GetFieldAny(csv, "Type", "I/O Type", "Tag Type", "種別") ?? string.Empty;
            var dataType = GetFieldAny(csv, "Data Type", "DataType", "Datatype", "データ型") ?? string.Empty;
            var address = GetFieldAny(csv, "Address", "Modbus Address", "アドレス") ?? string.Empty;

            if (!TryParseAddress(address, out var modbusType, out var modbusAddress))
            {
                items.Add(new CsvImportPreviewItem
                {
                    Action = CsvImportAction.Skip,
                    Name = name,
                    Reason = Loc.Format("UnsupportedAddress", address),
                });
                continue;
            }

            if (!TryParseDisplayType(dataType, modbusType, out var displayType, out var displayTypeError))
            {
                items.Add(new CsvImportPreviewItem
                {
                    Action = CsvImportAction.Skip,
                    Name = name,
                    ModbusType = modbusType,
                    ModbusAddress = modbusAddress,
                    Reason = displayTypeError,
                });
                continue;
            }

            var proposed = new MappingEntry(modbusType, modbusAddress)
            {
                Comment = name,
                DisplayType = displayType,
                Direction = MappingEntry.GetDefaultDirection(modbusType),
                RealScale = realScale,
            };

            if (existing.TryGetValue((modbusType, modbusAddress), out var current))
            {
                var changed = current.Comment != proposed.Comment || current.DisplayType != proposed.DisplayType;
                items.Add(new CsvImportPreviewItem
                {
                    Action = changed ? CsvImportAction.Update : CsvImportAction.Skip,
                    Name = name,
                    ModbusType = modbusType,
                    ModbusAddress = modbusAddress,
                    DisplayType = displayType,
                    ExistingPlcAddress = current.PlcAddress,
                    Existing = current,
                    Proposed = proposed,
                    Reason = changed ? Loc.Text("CsvReasonUpdate") : Loc.Text("CsvReasonNoChange"),
                });
            }
            else
            {
                items.Add(new CsvImportPreviewItem
                {
                    Action = CsvImportAction.Add,
                    Name = name,
                    ModbusType = modbusType,
                    ModbusAddress = modbusAddress,
                    DisplayType = displayType,
                    Proposed = proposed,
                    Reason = Loc.Text("CsvReasonNew"),
                });
            }
        }

        return items;
    }

    public static CsvImportResult Apply(IEnumerable<CsvImportPreviewItem> previewItems, ICollection<MappingEntry> mappings)
    {
        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var item in previewItems)
        {
            if (item.Action == CsvImportAction.Add && item.Proposed is not null)
            {
                mappings.Add(item.Proposed);
                added++;
                continue;
            }

            if (item.Action == CsvImportAction.Update && item.Existing is not null && item.Proposed is not null)
            {
                item.Existing.Comment = item.Proposed.Comment;
                item.Existing.DisplayType = item.Proposed.DisplayType;
                updated++;
                continue;
            }

            skipped++;
        }

        return new CsvImportResult(added, updated, skipped);
    }

    private static bool TryParseAddress(string value, out ModbusType type, out int address)
    {
        type = ModbusType.Coil;
        address = 0;
        var text = Regex.Replace(value.Trim(), @"\s+", " ");
        var match = Regex.Match(text, @"^(?<kind>.+?)\s+(?<address>\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        var kind = Regex.Replace(match.Groups["kind"].Value, @"[\s_-]+", string.Empty).ToUpperInvariant();
        if (!int.TryParse(match.Groups["address"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out address))
        {
            return false;
        }

        type = kind switch
        {
            "COIL" or "COILS" => ModbusType.Coil,
            "INPUT" or "INPUTS" or "DISCRETEINPUT" or "DISCRETEINPUTS" => ModbusType.DiscreteInput,
            "HOLDINGREG" or "HOLDINGREGS" or "HOLDINGREGISTER" or "HOLDINGREGISTERS" => ModbusType.HoldingRegister,
            "INPUTREG" or "INPUTREGS" or "INPUTREGISTER" or "INPUTREGISTERS" => ModbusType.InputRegister,
            _ => type,
        };

        return kind is "COIL" or "COILS"
            or "INPUT" or "INPUTS" or "DISCRETEINPUT" or "DISCRETEINPUTS"
            or "HOLDINGREG" or "HOLDINGREGS" or "HOLDINGREGISTER" or "HOLDINGREGISTERS"
            or "INPUTREG" or "INPUTREGS" or "INPUTREGISTER" or "INPUTREGISTERS";
    }

    private static bool TryParseDisplayType(string value, ModbusType modbusType, out DisplayType displayType, out string error)
    {
        var text = value.Trim();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            displayType = MappingEntry.IsRegisterType(modbusType) ? DisplayType.Int16 : DisplayType.Bool;
            error = Loc.Text("DataTypeMissing");
            return false;
        }

        if (!MappingEntry.IsRegisterType(modbusType))
        {
            displayType = DisplayType.Bool;
            if (text.Equals("Bool", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            error = Loc.Text("BoolOnly");
            return false;
        }

        switch (text.ToUpperInvariant())
        {
            case "INT":
            case "INT16":
            case "INTEGER":
                displayType = DisplayType.Int16;
                return true;
            case "REAL":
            case "FLOAT":
                displayType = DisplayType.ScaledReal;
                return true;
            default:
                displayType = DisplayType.Int16;
                error = Loc.Text("RegisterTypeOnly");
                return false;
        }
    }

    private static string? GetFieldAny(CsvReader csv, params string[] names)
    {
        foreach (var name in names)
        {
            if (csv.TryGetField<string>(name, out var value))
            {
                return value;
            }
        }

        return null;
    }
}

public sealed record CsvImportResult(int Added, int Updated, int Skipped);
