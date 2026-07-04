using GatewayApp.Models;
using PlcComm.Slmp;

namespace GatewayApp.Services;

internal delegate bool TryCreatePlcAccessPoint(MappingEntry entry, out PlcAccessPoint point);

internal static class PlcAccessBlockPlanner
{
    public const int MaxBlockPoints = 64;

    public static (List<PlcAccessBlock> Blocks, List<MappingEntry> Singles) BuildBlocks(
        IEnumerable<MappingEntry> entries,
        TryCreatePlcAccessPoint tryCreateAccessPoint)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(tryCreateAccessPoint);

        var points = new List<PlcAccessPoint>();
        var singles = new List<MappingEntry>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.PlcAddress))
            {
                singles.Add(entry);
                continue;
            }

            if (tryCreateAccessPoint(entry, out var point))
            {
                points.Add(point);
            }
            else
            {
                singles.Add(entry);
            }
        }

        var blocks = new List<PlcAccessBlock>();
        PlcAccessBlock? current = null;

        foreach (var point in points
            .OrderBy(static point => point.IsRegister)
            .ThenBy(static point => point.DeviceKey, StringComparer.Ordinal)
            .ThenBy(static point => point.Number))
        {
            if (current is null || !current.CanAdd(point))
            {
                current = new PlcAccessBlock(point);
                blocks.Add(current);
                continue;
            }

            current.Add(point);
        }

        return (blocks, singles);
    }
}

internal sealed record PlcAccessPoint(
    MappingEntry Entry,
    bool IsRegister,
    string DeviceKey,
    uint Number,
    string AddressText,
    SlmpDeviceAddress? SlmpAddress);

internal sealed class PlcAccessBlock
{
    public PlcAccessBlock(PlcAccessPoint firstPoint)
    {
        IsRegister = firstPoint.IsRegister;
        DeviceKey = firstPoint.DeviceKey;
        StartNumber = firstPoint.Number;
        EndNumber = firstPoint.Number;
        StartAddressText = firstPoint.AddressText;
        StartSlmpAddress = firstPoint.SlmpAddress;
        Points.Add(firstPoint);
    }

    public bool IsRegister { get; }

    public string DeviceKey { get; }

    public uint StartNumber { get; }

    public uint EndNumber { get; private set; }

    public string StartAddressText { get; }

    public SlmpDeviceAddress? StartSlmpAddress { get; }

    public List<PlcAccessPoint> Points { get; } = [];

    public int Count => checked((int)(EndNumber - StartNumber + 1));

    public bool CanAdd(PlcAccessPoint point)
    {
        if (point.IsRegister != IsRegister || point.DeviceKey != DeviceKey)
        {
            return false;
        }

        var isSameOrNext = point.Number <= EndNumber
            || (EndNumber < uint.MaxValue && point.Number == EndNumber + 1);
        return isSameOrNext && Count < PlcAccessBlockPlanner.MaxBlockPoints;
    }

    public void Add(PlcAccessPoint point)
    {
        Points.Add(point);
        if (point.Number > EndNumber)
        {
            EndNumber = point.Number;
        }
    }
}
