using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class RawDataIdentityTests
{
    [Fact]
    public void FromParsed_prefers_sample_no_from_mfg_lot()
    {
        var parsed = CreateParsedFile(sampleNo: 903);
        var lot = new MfgLot
        {
            LotNo = "20260527001",
            SampleName = "RF-904",
            SampleNo = "904"
        };

        var identity = RawDataIdentity.FromParsed(parsed, lot);

        identity.SampleNo.Should().Be(904);
    }

    [Fact]
    public void FromParsed_falls_back_to_quant_sample_no_when_mfg_sample_no_is_not_numeric()
    {
        var parsed = CreateParsedFile(sampleNo: 903);
        var lot = new MfgLot
        {
            LotNo = "20260527001",
            SampleName = "SIM-20251030001",
            SampleNo = "RF-904"
        };

        var identity = RawDataIdentity.FromParsed(parsed, lot);

        identity.SampleNo.Should().Be(903);
    }

    [Fact]
    public void FromParsed_uses_sample_no_from_mfg_sample_name_when_mfg_sample_no_is_empty()
    {
        var parsed = CreateParsedFile(sampleNo: 903);
        var lot = new MfgLot
        {
            LotNo = "20260527001",
            SampleName = "RF-904",
            SampleNo = null
        };

        var identity = RawDataIdentity.FromParsed(parsed, lot);

        identity.SampleNo.Should().Be(904);
    }

    [Fact]
    public void FromParsed_reads_std_style_sample_name_from_mfg_json()
    {
        var parsed = CreateParsedFile(sampleNo: 903);
        var lot = new MfgLot
        {
            LotNo = "20260527001",
            SampleName = "STD-N267(6/15)",
            SampleNo = null
        };

        var identity = RawDataIdentity.FromParsed(parsed, lot);

        identity.SampleNo.Should().Be(267);
    }

    private static ParsedQuantFile CreateParsedFile(int sampleNo) =>
        new()
        {
            Source = new QuantFileCandidate(
                FullPath: @$"C:\GAS\20260701\STD\STD[20260701 0355]_{sampleNo:000}.D\Quant.txt",
                DayFolderPath: @"C:\GAS\20260701",
                SourceRootPath: @"C:\GAS\20260701\STD",
                OutputRootPath: @"C:\GAS",
                LogicalBatchDate: "20260701",
                IsArchivedInput: false,
                TopFolderName: "STD",
                SourceKind: QuantSourceKind.Std,
                Port: "STD",
                DataFilename: @$"STD[20260701 0355]_{sampleNo:000}.D\Quant.txt",
                DataFilepath: @$"C:\GAS\20260701\STD\STD[20260701 0355]_{sampleNo:000}.D"),
            AcquiredAt = new DateTime(2026, 7, 1, 3, 55, 0),
            DataFile = $"{sampleNo:000}.D",
            DataPath = @"D:\data\",
            Sample = $"Sample {sampleNo}",
            Misc = "desc #20260527001",
            LotNo = "20260527001",
            SampleNo = sampleNo,
            Compounds = new Dictionary<string, QuantCompound>(StringComparer.OrdinalIgnoreCase)
        };
}
