using GatewayApp.Models;
using GatewayApp.Services;

namespace GatewayApp.Tests;

public sealed class RegisterInputParserTests
{
    [Fact]
    public void TryParse_ParsesScaledRealUsingEntryScale()
    {
        var entry = Register(DisplayType.ScaledReal, realScale: 100);

        var ok = RegisterInputParser.TryParse(entry, "12.34", out var raw, out var error);

        Assert.True(ok, error);
        Assert.Equal(1234, raw);
    }

    [Fact]
    public void TryParse_ParsesNegativeInt16AsRawWord()
    {
        var entry = Register(DisplayType.Int16);

        var ok = RegisterInputParser.TryParse(entry, "-1", out var raw, out var error);

        Assert.True(ok, error);
        Assert.Equal(65535, raw);
    }

    [Fact]
    public void TryParse_RejectsOutOfRangeScaledReal()
    {
        var entry = Register(DisplayType.ScaledReal, realScale: 100);

        var ok = RegisterInputParser.TryParse(entry, "400.00", out _, out var error);

        Assert.False(ok);
        Assert.Equal(Loc.Text("Int16Range"), error);
    }

    [Fact]
    public void TryParse_RejectsNonIntegerTextForInt16()
    {
        var entry = Register(DisplayType.Int16);

        var ok = RegisterInputParser.TryParse(entry, "12.5", out _, out var error);

        Assert.False(ok);
        Assert.Equal(Loc.Text("IntegerInputRequired"), error);
    }

    private static MappingEntry Register(DisplayType displayType, int realScale = 100) =>
        new(ModbusType.HoldingRegister, 0)
        {
            DisplayType = displayType,
            RealScale = realScale,
        };
}
