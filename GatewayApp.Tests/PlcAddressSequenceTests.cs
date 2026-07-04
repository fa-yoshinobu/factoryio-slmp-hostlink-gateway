using GatewayApp.Services;

namespace GatewayApp.Tests;

public sealed class PlcAddressSequenceTests
{
    [Fact]
    public void TryFormat_UsesOctalForIqF_XYAddresses()
    {
        var ok = PlcAddressSequence.TryFormat(
            "SLMP",
            "melsec:iq-f",
            "X",
            "7",
            1,
            out var address,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("X10", address);
    }

    [Fact]
    public void TryFormat_UsesKeyenceBitBankForHostLinkInternalRelays()
    {
        var ok = PlcAddressSequence.TryFormat(
            "HostLink",
            "keyence:kv-x500",
            "R",
            "015",
            1,
            out var address,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("R100", address);
    }

    [Fact]
    public void TryFormat_UsesHexBitSuffixForHostLinkXymBits()
    {
        var ok = PlcAddressSequence.TryFormat(
            "HostLink",
            "keyence:kv-x500-xym",
            "X",
            "0F",
            1,
            out var address,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("X10", address);
    }

    [Fact]
    public void TryFormat_RejectsInvalidOctalStartNumber()
    {
        var ok = PlcAddressSequence.TryFormat(
            "SLMP",
            "melsec:iq-f",
            "X",
            "8",
            0,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Equal(Loc.Format("StartNumberInvalid", "X"), error);
    }

    [Fact]
    public void TryFormat_ReportsOutOfRangeKeyenceXymAddress()
    {
        var ok = PlcAddressSequence.TryFormat(
            "HostLink",
            "keyence:kv-x500-xym",
            "X",
            "4294967295F",
            0,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Equal(Loc.Text("PlcAddressOutOfRange"), error);
    }
}
