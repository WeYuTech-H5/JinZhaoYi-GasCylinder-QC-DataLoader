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
        var parsed = new ParsedQuantFile
        {
            Source = new QuantFileCandidate(
                FullPath: @"C:\GAS\20251119\STD\STD[20251119 0947]_903.D\Quant.txt",
                DayFolderPath: @"C:\GAS\20251119",
                TopFolderName: "STD",
                SourceKind: QuantSourceKind.Std,
                Port: "STD",
                DataFilename: @"STD[20251119 0947]_903.D\Quant.txt",
                DataFilepath: @"C:\GAS\20251119\STD\STD[20251119 0947]_903.D"),
            AcquiredAt = new DateTime(2025, 11, 19, 9, 47, 0),
            DataFile = "0001.D",
            Sample = "Sample 1",
            Misc = " port 1  903  872>  #20251030001",
            LotNo = "20251030001",
            SampleNo = 903,
            Compounds = new Dictionary<string, QuantCompound>(StringComparer.OrdinalIgnoreCase)
        };
        var lot = new MfgLot(
            Id: 5841m,
            LotNo: "20251030001",
            SampleName: "SIM-20251030001",
            SampleNo: "903",
            SampleType: "TO14C1",
            Container: "SIM",
            EMVolts: "1294",
            RelativeEM: "1.05");

        var row = factory.Create(parsed, lot, "20251119903");

        row.EmVolts.Should().Be("1294");
        row.RelativeEm.Should().Be("1.05");
    }
}
