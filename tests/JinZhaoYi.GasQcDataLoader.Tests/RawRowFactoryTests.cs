using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class RawRowFactoryTests
{
    [Fact]
    public void Create_uses_em_values_from_mfg_lot()
    {
        var factory = new RawRowFactory(Options.Create(new SchedulerOptions()));
        var parsed = CreateParsedFile();
        var lot = CreateLot(si0Id: "5907");

        var row = factory.Create(parsed, lot, "20251119903");

        row.EmVolts.Should().Be("1294");
        row.RelativeEm.Should().Be("1.05");
        row.DataFilename.Should().Be(@"STD[20251119 0947]_903.D\0001.D");
        row.DataFilepath.Should().Be(@"D:\data\");
    }

    [Fact]
    public void Create_uses_si0_id_from_mfg_lot_si0_id()
    {
        var factory = new RawRowFactory(Options.Create(new SchedulerOptions()));
        var parsed = CreateParsedFile();
        var lot = CreateLot(id: 5841m, si0Id: "5907");

        var row = factory.Create(parsed, lot, "20251119903");

        row.Si0Id.Should().Be("5907");
        row.Si0Id.Should().NotBe("5841");
    }

    [Fact]
    public void Create_allows_null_si0_id_from_mfg_lot()
    {
        var factory = new RawRowFactory(Options.Create(new SchedulerOptions()));
        var parsed = CreateParsedFile();
        var lot = CreateLot(id: 5841m, si0Id: null);

        var row = factory.Create(parsed, lot, "20251119903");

        row.Si0Id.Should().BeNull();
    }

    private static ParsedQuantFile CreateParsedFile() =>
        new()
        {
            Source = new QuantFileCandidate(
                FullPath: @"C:\GAS\20251119\STD\STD[20251119 0947]_903.D\Quant.txt",
                DayFolderPath: @"C:\GAS\20251119",
                SourceRootPath: @"C:\GAS\20251119\STD",
                OutputRootPath: @"C:\GAS",
                LogicalBatchDate: "20251119",
                IsArchivedInput: false,
                TopFolderName: "STD",
                SourceKind: QuantSourceKind.Std,
                Port: "STD",
                DataFilename: @"STD[20251119 0947]_903.D\Quant.txt",
                DataFilepath: @"C:\GAS\20251119\STD\STD[20251119 0947]_903.D"),
            AcquiredAt = new DateTime(2025, 11, 19, 9, 47, 0),
            DataFile = "0001.D",
            DataPath = @"D:\data\",
            Sample = "Sample 1",
            Misc = " port 1  903  872>  #20251030001",
            LotNo = "20251030001",
            SampleNo = 903,
            Compounds = new Dictionary<string, QuantCompound>(StringComparer.OrdinalIgnoreCase)
        };

    private static MfgLot CreateLot(decimal id = 5841m, string? si0Id = "5907") =>
        new(
            Id: id,
            LotNo: "20251030001",
            Si0Id: si0Id,
            SampleName: "SIM-20251030001",
            SampleNo: "903",
            SampleType: "TO14C1",
            Container: "SIM",
            EMVolts: "1294",
            RelativeEM: "1.05");
}
