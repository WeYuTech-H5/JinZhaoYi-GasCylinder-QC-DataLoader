using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class CalculationServiceTests
{
    private readonly CalculationService _service = new();

    [Fact]
    public void CreateAverageRow_matches_excel_std_avg_row()
    {
        var first = Row("90830", "STD", "20251030001", ("Acetone", 1093994m), ("IPA", 3920610m));
        var second = Row("90831", "STD", "20251030001", ("Acetone", 1094611m), ("IPA", 3980830m));

        var average = _service.CreateAverageRow("AVG(90830:90831)", first, second);

        average.Areas["Acetone"].Should().Be(1094302.5m);
        average.Areas["IPA"].Should().Be(3950720m);
        average.Id1.Should().Be("90830");
        average.Id2.Should().Be("90831");
    }

    [Fact]
    public void CreateRpdRow_matches_excel_std_rpd_row()
    {
        var first = Row("90830", "STD", "20251030001", ("Acetone", 1093994m), ("IPA", 3920610m));
        var second = Row("90831", "STD", "20251030001", ("Acetone", 1094611m), ("IPA", 3980830m));

        var rpd = _service.CreateRpdRow("RPD(90830:90831)", first, second);

        rpd.Areas["Acetone"].Should().BeApproximately(0.0005638294344m, 0.0000000001m);
        rpd.Areas["IPA"].Should().BeApproximately(0.0152427911874m, 0.0000000001m);
    }

    [Fact]
    public void ApplyPortRawPpb_matches_excel_port_raw_ppb_formula()
    {
        var rf = Row("RF,ppb(5841)", "STD", "20251030001", ("Acetone", 98.682473396043093m), ("IPA", 112.797628775872m));
        var stdAverage = Row("AVG(90830:90831)", "STD", "20251030001", ("Acetone", 1094302.5m), ("IPA", 3950720m));
        var portRaw = Row("90788", "PORT 2", "20251117006", ("Acetone", 1105797m), ("IPA", 3852743m));

        _service.ApplyPortRawPpb(portRaw, rf, stdAverage);

        portRaw.Ppbs["Acetone"].Should().BeApproximately(99.7190292757m, 0.0000000001m);
        portRaw.Ppbs["IPA"].Should().BeApproximately(110.0002720220m, 0.0000000001m);
    }

    [Fact]
    public void CreatePortAveragePpbAndRpd_match_excel_rows_16_to_18()
    {
        var rf = Row("RF,ppb(5841)", "STD", "20251030001", ("Acetone", 98.682473396043093m), ("IPA", 112.797628775872m));
        var stdAverage = Row("AVG(90830:90831)", "STD", "20251030001", ("Acetone", 1094302.5m), ("IPA", 3950720m));
        var first = Row("90791", "PORT 2", "20251117006", ("Acetone", 1105792m), ("IPA", 3654182m));
        var second = Row("90792", "PORT 2", "20251117006", ("Acetone", 1072747m), ("IPA", 3734347m));

        var average = _service.CreateAverageRow("AVG(90791:90792)", first, second);
        var ppb = _service.CreatePortPpbRow("ppb(5900)", average, rf, stdAverage);
        var rpd = _service.CreateRpdRow("RPD(90791:90792)", first, second);

        average.Areas["Acetone"].Should().Be(1089269.5m);
        average.Areas["IPA"].Should().Be(3694264.5m);
        ppb.Areas["IPA"].Should().BeApproximately(105.4755274155m, 0.0000000001m);
        rpd.Areas["IPA"].Should().BeApproximately(0.0216998539222m, 0.0000000001m);
    }

    [Fact]
    public void Port4_uses_newer_std_average_row_29()
    {
        var rf = Row("RF,ppb(5841)", "STD", "20251030001", ("IPA", 112.797628775872m));
        var row29StdAverage = Row("AVG(90832:90833)", "STD", "20251030001", ("IPA", 3804340m));
        var port4Raw = Row("90808", "PORT 4", "20251118001", ("IPA", 3757363m));

        _service.ApplyPortRawPpb(port4Raw, rf, row29StdAverage);

        port4Raw.Ppbs["IPA"].Should().BeApproximately(111.4047737190m, 0.0000000001m);
    }

    [Fact]
    public void CreatePortPpbRow_returns_null_when_std_average_is_zero()
    {
        var rf = Row("RF,ppb(5841)", "STD", "20251030001", ("IPA", 112.797628775872m));
        var stdAverage = Row("AVG(90830:90831)", "STD", "20251030001", ("IPA", 0m));
        var portAverage = Row("AVG(90791:90792)", "PORT 2", "20251117006", ("IPA", 3694264.5m));

        var ppb = _service.CreatePortPpbRow("ppb(5900)", portAverage, rf, stdAverage);

        ppb.Areas["IPA"].Should().BeNull();
    }

    [Fact]
    public void CreateAverageRow_returns_null_when_area_is_negative()
    {
        var first = Row("90791", "PORT 2", "20251117006", ("IPA", -1m));
        var second = Row("90792", "PORT 2", "20251117006", ("IPA", 3734347m));

        var average = _service.CreateAverageRow("AVG(90791:90792)", first, second);

        average.Areas["IPA"].Should().BeNull();
    }

    [Fact]
    public void CreateRpdRow_returns_null_when_denominator_is_zero()
    {
        var first = Row("90791", "PORT 2", "20251117006", ("IPA", 0m));
        var second = Row("90792", "PORT 2", "20251117006", ("IPA", 0m));

        var rpd = _service.CreateRpdRow("RPD(90791:90792)", first, second);

        rpd.Areas["IPA"].Should().BeNull();
    }

    [Fact]
    public void CreateRpdRow_returns_null_when_area_is_negative()
    {
        var first = Row("90791", "PORT 2", "20251117006", ("IPA", -10m));
        var second = Row("90792", "PORT 2", "20251117006", ("IPA", 10m));

        var rpd = _service.CreateRpdRow("RPD(90791:90792)", first, second);

        rpd.Areas["IPA"].Should().BeNull();
    }

    private static QcDataRow Row(string id, string port, string lotNo, params (string Suffix, decimal Value)[] areas)
    {
        var row = new QcDataRow
        {
            Id = id,
            Port = port,
            LotNo = lotNo,
            Si0Id = port == "STD" ? "5841" : "5900"
        };

        foreach (var (suffix, value) in areas)
        {
            row.Areas[suffix] = value;
        }

        return row;
    }
}
