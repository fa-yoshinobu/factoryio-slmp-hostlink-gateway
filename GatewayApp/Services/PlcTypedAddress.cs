namespace GatewayApp.Services;

using GatewayApp.Models;

public sealed record PlcTypedAddress(string BaseAddress, string DataType);

public static class PlcTypedAddressParser
{
    private static readonly HashSet<string> RegisterDataTypes = ["S", "U"];

    public static PlcTypedAddress ParseRequired(string rawAddress, MappingEntry entry)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            throw new InvalidOperationException(Loc.Format("PlcAddressMissing", entry.ModbusLabel));
        }

        var separatorIndex = rawAddress.LastIndexOf(':');
        if (separatorIndex < 0)
        {
            return new PlcTypedAddress(rawAddress.Trim().ToUpperInvariant(), DefaultDataType(entry));
        }

        if (separatorIndex == 0 || separatorIndex == rawAddress.Length - 1)
        {
            throw new InvalidOperationException($"{entry.ModbusLabel} PLC address must include a base address and data type suffix.");
        }

        var baseAddress = rawAddress[..separatorIndex].Trim();
        var dataType = rawAddress[(separatorIndex + 1)..].Trim().TrimStart('.').ToUpperInvariant();
        if (baseAddress.Length == 0 || dataType.Length == 0)
        {
            throw new InvalidOperationException($"{entry.ModbusLabel} PLC address must include a base address and data type suffix.");
        }

        if (entry.IsBool)
        {
            if (dataType != "BIT")
            {
                throw new InvalidOperationException($"{entry.ModbusLabel} PLC address must use :BIT.");
            }
        }
        else if (!RegisterDataTypes.Contains(dataType))
        {
            throw new InvalidOperationException($"{entry.ModbusLabel} PLC address must use :S or :U.");
        }

        return new PlcTypedAddress(baseAddress.ToUpperInvariant(), dataType);
    }

    public static string AppendDefaultSuffix(ModbusType modbusType, string baseAddress)
    {
        var dataType = MappingEntry.IsRegisterType(modbusType) ? "S" : "BIT";
        return $"{baseAddress}:{dataType}";
    }

    private static string DefaultDataType(MappingEntry entry)
    {
        return entry.IsRegister ? "S" : "BIT";
    }
}
