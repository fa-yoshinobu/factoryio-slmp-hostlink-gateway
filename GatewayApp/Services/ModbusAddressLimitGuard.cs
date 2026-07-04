namespace GatewayApp.Services;

internal static class ModbusAddressLimitGuard
{
    public const int MaximumAddress = 65535;
    public const int PracticalAddressLimit = 4096;

    public static bool RequiresLargeMappingConfirmation(params int?[] maxAddresses) =>
        maxAddresses.Any(address => address is > PracticalAddressLimit);

    public static int CountGeneratedRows(params int?[] maxAddresses)
    {
        long total = 0;
        foreach (var address in maxAddresses)
        {
            if (address.HasValue)
            {
                total += (long)address.Value + 1;
            }
        }

        return total > int.MaxValue ? int.MaxValue : (int)total;
    }
}
