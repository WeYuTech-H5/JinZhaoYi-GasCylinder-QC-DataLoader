using ClosedXML.Excel;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class StdCylinderSummaryExporterTests
{
    [Fact]
    public void ExportForDownload_writes_sample_types_ppb_columns_and_excel_guid()
    {
        var exportSessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var rows = new[]
        {
            Row("STD-T132", "20260507007", 97.825m, exportSessionId),
            Row("TSMC-002", "20260516001", 98.125m, exportSessionId),
            Row("RF-001", "20260410008", 99.525m, exportSessionId)
        };

        var download = new StdCylinderSummaryExporter().ExportForDownload(rows);

        using var stream = new MemoryStream(download.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("STD Cylinder總表");

        worksheet.Cell("A1").GetString().Should().Be("si0,id@@4,1");
        worksheet.Cell("AV3").GetString().Should().Be("FailDesc");
        worksheet.Cell("AW1").GetString().Should().Be("si2,ppb,67641");
        worksheet.Cell("AY3").GetString().Should().Be("Methylene Chloride");
        worksheet.Cell("CI3").GetString().Should().Be("HCBD");
        worksheet.Cell("CJ3").GetString().Should().Be("Note");
        worksheet.Cell("CK3").GetString().Should().Be("ExcelGuid");

        worksheet.Cell("B4").GetString().Should().Be("STD-T132");
        worksheet.Cell("B5").GetString().Should().Be("TSMC-002");
        worksheet.Cell("B6").GetString().Should().Be("RF-001");
        worksheet.Cell("AW5").GetDouble().Should().BeApproximately(98.125, 0.000001);
        worksheet.Cell("CK4").GetString().Should().Be(exportSessionId.ToString("D"));
        worksheet.Cell("CK5").GetString().Should().Be(exportSessionId.ToString("D"));
        worksheet.Cell("CK6").GetString().Should().Be(exportSessionId.ToString("D"));
    }

    private static StdCylinderSummaryRow Row(
        string sampleName,
        string lotNo,
        decimal acetone,
        Guid exportSessionId)
    {
        var row = new StdCylinderSummaryRow
        {
            ExcelExportSessionId = exportSessionId
        };
        row.Values["id"] = 1;
        row.Values["SampleName"] = sampleName;
        row.Values["ProdDate"] = new DateTime(2026, 5, 7);
        row.Values["LotNo"] = lotNo;
        row.Values["Result"] = "Pass";
        row.Values["FailDesc"] = null;
        row.Areas["Acetone"] = acetone;
        row.Areas["HCBD"] = 100.892m;
        return row;
    }
}
