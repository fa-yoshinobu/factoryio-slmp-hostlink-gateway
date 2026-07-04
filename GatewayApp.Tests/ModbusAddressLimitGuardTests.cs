using GatewayApp.Services;

namespace GatewayApp.Tests;

public sealed class ModbusAddressLimitGuardTests
{
    [Fact]
    public void RequiresLargeMappingConfirmation_AllowsPracticalLimit()
    {
        var requiresConfirmation = ModbusAddressLimitGuard.RequiresLargeMappingConfirmation(
            null,
            ModbusAddressLimitGuard.PracticalAddressLimit);

        Assert.False(requiresConfirmation);
    }

    [Fact]
    public void RequiresLargeMappingConfirmation_FlagsAddressAbovePracticalLimit()
    {
        var requiresConfirmation = ModbusAddressLimitGuard.RequiresLargeMappingConfirmation(
            ModbusAddressLimitGuard.PracticalAddressLimit + 1);

        Assert.True(requiresConfirmation);
    }

    [Fact]
    public void CountGeneratedRows_SumsEachSpecifiedAddressRange()
    {
        var rowCount = ModbusAddressLimitGuard.CountGeneratedRows(0, 9, null, 99);

        Assert.Equal(111, rowCount);
    }
}
