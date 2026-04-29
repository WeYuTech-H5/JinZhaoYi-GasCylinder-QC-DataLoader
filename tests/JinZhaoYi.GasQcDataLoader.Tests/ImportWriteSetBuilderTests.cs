using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class ImportWriteSetBuilderTests
{
    private readonly ImportWriteSetBuilder _builder = new(
        new RawRowFactory(Options.Create(new SchedulerOptions())),
        new CalculationService(),
        NullLogger<ImportWriteSetBuilder>.Instance);

    [Fact]
    public void BuildWriteSet_outputs_query2_rows_in_sample_like_order()
    {
        var rf = CreateRfRow();
        var parsedFiles = new[]
        {
            Parsed("STD", QuantSourceKind.Std, "20251030001", 903, new DateTime(2025, 11, 19, 9, 47, 0), ("Acetone", 100m), ("IPA", 200m)),
            Parsed("STD", QuantSourceKind.Std, "20251030001", 904, new DateTime(2025, 11, 19, 10, 2, 0), ("Acetone", 110m), ("IPA", 210m)),
            Parsed("PORT 2", QuantSourceKind.Port, "20251117006", 23, new DateTime(2025, 11, 19, 18, 18, 0), ("Acetone", 90m), ("IPA", 180m)),
            Parsed("PORT 2", QuantSourceKind.Port, "20251117006", 24, new DateTime(2025, 11, 19, 18, 33, 0), ("Acetone", 95m), ("IPA", 190m)),
            Parsed("STD", QuantSourceKind.Std, "20251030001", 905, new DateTime(2025, 11, 19, 18, 48, 0), ("Acetone", 120m), ("IPA", 220m)),
            Parsed("STD", QuantSourceKind.Std, "20251030001", 906, new DateTime(2025, 11, 19, 19, 3, 0), ("Acetone", 130m), ("IPA", 230m))
        };

        var lots = new Dictionary<string, MfgLot>(StringComparer.OrdinalIgnoreCase)
        {
            ["20251030001"] = Lot("20251030001", 5841, "RF-903", "1L_Cylinder"),
            ["20251117006"] = Lot("20251117006", 5900, "TSMC-023", "0.5L_Cylinder")
        };

        var writeSet = _builder.BuildWriteSet(parsedFiles, lots, rf);

        writeSet.Query2Rows.Select(row => row.RowType).Should().Equal(
            Query2ExportRowType.Rf,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Rpd,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Ppb,
            Query2ExportRowType.Rpd,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Raw,
            Query2ExportRowType.Avg,
            Query2ExportRowType.Qc,
            Query2ExportRowType.Rpd);
    }

    [Fact]
    public void BuildWriteSet_populates_std_qc_row_when_second_std_group_exists()
    {
        var rf = CreateRfRow();
        var parsedFiles = new[]
        {
            Parsed("STD", QuantSourceKind.Std, "20251030001", 903, new DateTime(2025, 11, 19, 9, 47, 0), ("IPA", 200m)),
            Parsed("STD", QuantSourceKind.Std, "20251030001", 904, new DateTime(2025, 11, 19, 10, 2, 0), ("IPA", 220m)),
            Parsed("PORT 2", QuantSourceKind.Port, "20251117006", 23, new DateTime(2025, 11, 19, 18, 18, 0), ("IPA", 180m)),
            Parsed("PORT 2", QuantSourceKind.Port, "20251117006", 24, new DateTime(2025, 11, 19, 18, 33, 0), ("IPA", 190m)),
            Parsed("STD", QuantSourceKind.Std, "20251030001", 905, new DateTime(2025, 11, 19, 18, 48, 0), ("IPA", 240m)),
            Parsed("STD", QuantSourceKind.Std, "20251030001", 906, new DateTime(2025, 11, 19, 19, 3, 0), ("IPA", 260m))
        };

        var lots = new Dictionary<string, MfgLot>(StringComparer.OrdinalIgnoreCase)
        {
            ["20251030001"] = Lot("20251030001", 5841, "RF-903", "1L_Cylinder"),
            ["20251117006"] = Lot("20251117006", 5900, "TSMC-023", "0.5L_Cylinder")
        };

        var writeSet = _builder.BuildWriteSet(parsedFiles, lots, rf);
        var qcRow = writeSet.Query2Rows.Single(row => row.RowType == Query2ExportRowType.Qc).Row;
        writeSet.StdQcRows.Should().ContainSingle();

        qcRow.Id.Should().StartWith("QC(");
        qcRow.Id1.Should().StartWith("AVG(");
        qcRow.Id2.Should().StartWith("AVG(");
        qcRow.Areas["IPA"].Should().BeApproximately(0.1739130434m, 0.0000000001m);
    }

    private static ParsedQuantFile Parsed(
        string port,
        QuantSourceKind sourceKind,
        string lotNo,
        int sampleNo,
        DateTime acquiredAt,
        params (string Suffix, decimal Area)[] areas)
    {
        var candidate = new QuantFileCandidate(
            FullPath: $@"C:\GAS\20251119\{port}\{port}[{acquiredAt:yyyyMMdd HHmm}]_{sampleNo:000}.D\Quant.txt",
            DayFolderPath: @"C:\GAS\20251119",
            SourceRootPath: $@"C:\GAS\20251119\{port}",
            OutputRootPath: @"C:\GAS",
            LogicalBatchDate: "20251119",
            IsArchivedInput: false,
            TopFolderName: port,
            SourceKind: sourceKind,
            Port: port,
            DataFilename: $@"{port}[{acquiredAt:yyyyMMdd HHmm}]_{sampleNo:000}.D\Quant.txt",
            DataFilepath: $@"C:\GAS\20251119\{port}\{port}[{acquiredAt:yyyyMMdd HHmm}]_{sampleNo:000}.D");

        var compounds = new Dictionary<string, QuantCompound>(StringComparer.OrdinalIgnoreCase);
        foreach (var (suffix, area) in areas)
        {
            var analyte = CompoundMap.Analytes.Single(item => item.Suffix == suffix);
            compounds[suffix] = new QuantCompound(analyte, 1m, area, null);
        }

        return new ParsedQuantFile
        {
            Source = candidate,
            AcquiredAt = acquiredAt,
            DataFile = $"{sampleNo:000}.D",
            DataPath = @"C:\Cylinder\QC-01\20251120 101838\",
            Sample = $"Sample {sampleNo}",
            Misc = $"desc #{lotNo}",
            LotNo = lotNo,
            SampleNo = sampleNo,
            Compounds = compounds
        };
    }

    private static MfgLot Lot(string lotNo, int? si0Id, string sampleName, string container) =>
        new()
        {
            Id = si0Id ?? 0,
            LotNo = lotNo,
            Si0Id = si0Id,
            SampleName = sampleName,
            SampleNo = null,
            SampleType = "TO14C1",
            Container = container,
            EMVolts = "1458.82",
            RelativeEM = "-23.529"
        };

    private static QcDataRow CreateRfRow()
    {
        var row = new QcDataRow
        {
            Id = "RF,ppb(5841)",
            Port = "STD",
            LotNo = "20251030001",
            Si0Id = 5841
        };
        row.Areas["Acetone"] = 98.68m;
        row.Areas["IPA"] = 112.79m;
        return row;
    }
}
