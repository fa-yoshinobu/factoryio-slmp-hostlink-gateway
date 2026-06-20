namespace GatewayApp.Models;

public enum CsvImportAction
{
    Add,
    Update,
    Skip,
}

public sealed class CsvImportPreviewItem
{
    public CsvImportAction Action { get; init; }

    public string ActionText => Action switch
    {
        CsvImportAction.Add => "追加",
        CsvImportAction.Update => "更新",
        _ => "スキップ",
    };

    public ModbusType? ModbusType { get; init; }

    public int? ModbusAddress { get; init; }

    public string ModbusLabel => ModbusType is null || ModbusAddress is null
        ? string.Empty
        : MappingEntry.FormatModbusLabel(ModbusType.Value, ModbusAddress.Value);

    public string Name { get; init; } = string.Empty;

    public DisplayType? DisplayType { get; init; }

    public string ExistingPlcAddress { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public MappingEntry? Existing { get; init; }

    public MappingEntry? Proposed { get; init; }
}
