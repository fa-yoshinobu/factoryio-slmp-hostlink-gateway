namespace GatewayApp.Services;

using GatewayApp.Models;

public sealed record PlcTypedAddress(string BaseAddress, string DataType);

public static class PlcTypedAddressParser
{
    public static PlcTypedAddress ParseRequired(string rawAddress, MappingEntry entry)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            throw new InvalidOperationException(Loc.Format("PlcAddressMissing", entry.ModbusLabel));
        }

        var address = NormalizeAddress(rawAddress);
        if (address.Contains(':'))
        {
            throw new InvalidOperationException($"{entry.ModbusLabel} PLC address is invalid.");
        }

        return new PlcTypedAddress(address, DefaultDataType(entry));
    }

    public static string NormalizeAddress(string rawAddress)
    {
        return rawAddress.Trim().ToUpperInvariant();
    }

    private static string DefaultDataType(MappingEntry entry)
    {
        return entry.IsRegister ? "S" : "BIT";
    }
}
