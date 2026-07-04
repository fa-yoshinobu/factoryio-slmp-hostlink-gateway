using GatewayApp.Models;
using GatewayApp.Services;
using System.IO;

namespace GatewayApp.Tests;

public sealed class CsvImportServiceTests
{
    [Fact]
    public void Apply_UpdatesExistingCommentAndDisplayTypeWithoutOverwritingPlcAddress()
    {
        using var tempFile = new TempCsv(
            "Name,Data Type,Address",
            "Mixer Level,REAL,Holding Reg 0");
        var existing = new MappingEntry(ModbusType.HoldingRegister, 0)
        {
            PlcAddress = "D100",
            Comment = "Old",
            DisplayType = DisplayType.Int16,
        };
        var mappings = new List<MappingEntry> { existing };

        var preview = CsvImportService.Preview(tempFile.Path, mappings, realScale: 100);
        var result = CsvImportService.Apply(preview, mappings);

        Assert.Equal(new CsvImportResult(0, 1, 0), result);
        Assert.Single(mappings);
        Assert.Equal("D100", existing.PlcAddress);
        Assert.Equal("Mixer Level", existing.Comment);
        Assert.Equal(DisplayType.ScaledReal, existing.DisplayType);
    }

    [Fact]
    public void Preview_SkipsUnsupportedRegisterDataType()
    {
        using var tempFile = new TempCsv(
            "Name,Data Type,Address",
            "Recipe Name,STRING,Holding Reg 1");

        var preview = CsvImportService.Preview(tempFile.Path, [], realScale: 100);

        var item = Assert.Single(preview);
        Assert.Equal(CsvImportAction.Skip, item.Action);
        Assert.Equal(Loc.Text("RegisterTypeOnly"), item.Reason);
    }

    [Fact]
    public void Preview_SkipsMissingDataTypeInsteadOfGuessing()
    {
        using var tempFile = new TempCsv(
            "Name,Data Type,Address",
            "Valve,,Coil 0");

        var preview = CsvImportService.Preview(tempFile.Path, [], realScale: 100);

        var item = Assert.Single(preview);
        Assert.Equal(CsvImportAction.Skip, item.Action);
        Assert.Equal(Loc.Text("DataTypeMissing"), item.Reason);
    }

    private sealed class TempCsv : IDisposable
    {
        public TempCsv(params string[] lines)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"factoryio-csv-{Guid.NewGuid():N}.csv");
            File.WriteAllLines(Path, lines);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
