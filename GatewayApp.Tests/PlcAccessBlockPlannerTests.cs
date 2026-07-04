using GatewayApp.Models;
using GatewayApp.Services;
using System.Globalization;

namespace GatewayApp.Tests;

public sealed class PlcAccessBlockPlannerTests
{
    [Fact]
    public void BuildBlocks_MergesConsecutiveAddresses()
    {
        var entries = new[]
        {
            Entry(ModbusType.HoldingRegister, 0, "D100"),
            Entry(ModbusType.HoldingRegister, 1, "D101"),
            Entry(ModbusType.HoldingRegister, 2, "D102"),
        };

        var (blocks, singles) = PlcAccessBlockPlanner.BuildBlocks(entries, TryCreatePoint);

        Assert.Empty(singles);
        var block = Assert.Single(blocks);
        Assert.True(block.IsRegister);
        Assert.Equal("D", block.DeviceKey);
        Assert.Equal((uint)100, block.StartNumber);
        Assert.Equal((uint)102, block.EndNumber);
        Assert.Equal(3, block.Count);
    }

    [Fact]
    public void BuildBlocks_SplitsAtSixtyFourPoints()
    {
        var entries = Enumerable.Range(0, 65)
            .Select(index => Entry(ModbusType.HoldingRegister, index, $"D{index.ToString(CultureInfo.InvariantCulture)}"))
            .ToArray();

        var (blocks, singles) = PlcAccessBlockPlanner.BuildBlocks(entries, TryCreatePoint);

        Assert.Empty(singles);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(PlcAccessBlockPlanner.MaxBlockPoints, blocks[0].Count);
        Assert.Equal(1, blocks[1].Count);
    }

    [Fact]
    public void BuildBlocks_SeparatesDeviceKindsAndFamilies()
    {
        var entries = new[]
        {
            Entry(ModbusType.HoldingRegister, 0, "D100"),
            Entry(ModbusType.HoldingRegister, 1, "R101"),
            Entry(ModbusType.Coil, 2, "D102"),
        };

        var (blocks, singles) = PlcAccessBlockPlanner.BuildBlocks(entries, TryCreatePoint);

        Assert.Empty(singles);
        Assert.Equal(3, blocks.Count);
        Assert.Contains(blocks, block => block.DeviceKey == "D" && block.IsRegister);
        Assert.Contains(blocks, block => block.DeviceKey == "R" && block.IsRegister);
        Assert.Contains(blocks, block => block.DeviceKey == "D" && !block.IsRegister);
    }

    [Fact]
    public void BuildBlocks_LeavesUnparseableAddressesAsSingles()
    {
        var entries = new[]
        {
            Entry(ModbusType.HoldingRegister, 0, "D100"),
            Entry(ModbusType.HoldingRegister, 1, "not-an-address"),
            Entry(ModbusType.HoldingRegister, 2, ""),
        };

        var (blocks, singles) = PlcAccessBlockPlanner.BuildBlocks(entries, TryCreatePoint);

        Assert.Single(blocks);
        Assert.Equal(2, singles.Count);
        Assert.Equal("not-an-address", singles[0].PlcAddress);
        Assert.Equal(string.Empty, singles[1].PlcAddress);
    }

    private static MappingEntry Entry(ModbusType type, int modbusAddress, string plcAddress) =>
        new(type, modbusAddress)
        {
            PlcAddress = plcAddress,
        };

    private static bool TryCreatePoint(MappingEntry entry, out PlcAccessPoint point)
    {
        point = default!;
        var text = entry.PlcAddress.Trim();
        var prefix = new string(text.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var digits = text[prefix.Length..];
        if (prefix.Length == 0 || !uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        point = new PlcAccessPoint(entry, entry.IsRegister, prefix, number, text, null);
        return true;
    }
}
