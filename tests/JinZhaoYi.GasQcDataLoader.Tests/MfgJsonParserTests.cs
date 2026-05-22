using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Services.Service;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class MfgJsonParserTests
{
    [Fact]
    public void Parse_maps_json_fields_to_mfg_lot_record()
    {
        var rows = new MfgJsonParser().Parse(
            """
            [
              {
                "si0_id": 6377,
                "si0_SampleName": "STD-N254",
                "si0_ProdDate": "2026-05-08 00:00:00",
                "LotNo": "20260508002",
                "ProdType": "測試",
                "Prod_Operator": "Shawn",
                "Prod_VacuumPrs": "-13",
                "Prod_Can1_Prs": "29.8",
                "SampleNo": "254",
                "SampleType": "TO14C1",
                "Container": "0.5L_Cylinder",
                "QCTime": "2026-05-11 15:28:00",
                "Result": "Pass"
              }
            ]
            """);

        rows.Should().ContainSingle();
        var row = rows[0];
        row.Id.Should().Be(6377m);
        row.Si0Id.Should().Be("6377");
        row.LotNo.Should().Be("20260508002");
        row.SampleName.Should().Be("STD-N254");
        row.ProdDate.Should().Be(new DateTime(2026, 5, 8));
        row.ProdVacuumPrs.Should().Be(-13);
        row.ProdCan1Prs.Should().Be(29.8m);
        row.QcTime.Should().Be(new DateTime(2026, 5, 11, 15, 28, 0));
    }

    [Fact]
    public void Parse_rejects_missing_required_fields()
    {
        var act = () => new MfgJsonParser().Parse("""[{ "si0_id": 6377 }]""");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LotNo*");
    }

    [Fact]
    public void Parse_rejects_duplicate_lot_no_in_same_file()
    {
        var act = () => new MfgJsonParser().Parse(
            """
            [
              { "si0_id": 6377, "LotNo": "20260508002" },
              { "si0_id": 6378, "LotNo": "20260508002" }
            ]
            """);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate LotNo*");
    }
}
