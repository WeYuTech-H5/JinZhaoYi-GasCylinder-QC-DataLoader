using System.Globalization;
using ClosedXML.Excel;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class StdCylinderSummaryExporter : IStdCylinderSummaryExporter
{
    private const string SheetName = "STD Cylinder\u7e3d\u8868";
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int HeaderRowCount = 3;
    private const int DataStartRow = HeaderRowCount + 1;

    private static readonly IReadOnlyDictionary<string, string> CasCodeByAnalyte =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Acetone"] = "67641",
            ["IPA"] = "67630",
            ["Methlene"] = "75092",
            ["CNF"] = "311897",
            ["Cyclopentane"] = "287923",
            ["2-Butanone"] = "78933",
            ["Ethyl Acetate"] = "141786",
            ["Benzene"] = "71432",
            ["Carbon Tetrachloride"] = "56235",
            ["Toluene"] = "108883",
            ["1,2,4-TMB"] = "95636",
            ["Chlorobenzene-D5"] = "3114554",
            ["Freon114"] = "76142",
            ["1,1-Dichloroethene"] = "75354",
            ["Freon113"] = "76131",
            ["1,1-Dichloroethane"] = "75343",
            ["cis-1,2-Dichloroethene"] = "156592",
            ["Freon20"] = "67663",
            ["1,1,1-Trichloroethane"] = "71556",
            ["1,2-Dichloroethane"] = "107062",
            ["Trichloroethylene"] = "79016",
            ["1,2-Dichloropropane"] = "78875",
            ["cis-1,3-Dichloropropene"] = "10061015",
            ["trans-1,3-Dichloropropene"] = "10061026",
            ["1,1,2-Trichloroethane"] = "79005",
            ["Tetrachloroethylene"] = "127184",
            ["1,2-Dibromoethane"] = "106934",
            ["ChloroBenzene"] = "108907",
            ["Ethylbenzene"] = "100414",
            ["p-Xylene"] = "106423",
            ["Styrene"] = "100425",
            ["o-Xylene"] = "95476",
            ["1,1,2,2-Tetrachloroethane"] = "79345",
            ["1,3,5-TMB"] = "108678",
            ["1,3-Dichlorobenzene"] = "541731",
            ["1,4-Dichlorobenzene"] = "106467",
            ["1,2-Dichlorobenzene"] = "95501",
            ["1,2,4-TCB"] = "120821",
            ["HCBD"] = "87683"
        };

    private static readonly IReadOnlyDictionary<string, string> DisplayHeaderByAnalyte =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Methlene"] = "Methylene Chloride"
        };

    private static readonly SummaryColumn[] FixedColumns =
    [
        new("si0,id@@4,1", null, "id", "id"),
        new("si0,SampleName", null, "SampleName", "SampleName"),
        new("si0,ProdDate", null, "ProdDate", "ProdDate"),
        new("si1,LotNo", null, "LotNo", "LotNo"),
        new("si1,ProdType", null, "ProdType", "ProdType"),
        new("si1,Prod_Operator", null, "Prod_Operator", "Prod_Operator"),
        new("si1,Prod_IniPrs", null, "Prod_IniPrs", "Prod_IniPrs"),
        new("si1,Prod_LeakTest1", null, "Prod_LeakTest1", "Prod_LeakTest1"),
        new("si1,Prod_VacuumPrs", null, "Prod_VacuumPrs", "Prod_VacuumPrs"),
        new("si1,Prod_Can1_FillingPrs", null, "Prod_Can1_FillingPrs", "Prod_Can1_FillingPrs"),
        new("si1,Prod_Can2_FillingPrs", null, "Prod_Can2_FillingPrs", "Prod_Can2_FillingPrs"),
        new("si1,Prod_Bomb2_FillingPrs", null, "Prod_Bomb2_FillingPrs", "Prod_Bomb2_FillingPrs"),
        new("si1,Prod_Bomb1_FillingPrs", null, "Prod_Bomb1_FillingPrs", "Prod_Bomb1_FillingPrs"),
        new("si1,Prod_Bomb3_FillingPrs", null, "Prod_Bomb3_FillingPrs", "Prod_Bomb3_FillingPrs"),
        new("si1,Prod_LeakTest2", null, "Prod_LeakTest2", "Prod_LeakTest2"),
        new("si1,Prod_Can1_LotNo", null, "Prod_Can1_LotNo", "Prod_Can1_LotNo"),
        new("si1,Prod_Can1_Flow", null, "Prod_Can1_Flow", "Prod_Can1_Flow"),
        new("si1,Prod_Can1_Sec", null, "Prod_Can1_Sec", "Prod_Can1_Sec"),
        new("si1,Prod_Can1_Prs", null, "Prod_Can1_Prs", "Prod_Can1_Prs"),
        new("si1,Prod_Can2_LotNo", null, "Prod_Can2_LotNo", "Prod_Can2_LotNo"),
        new("si1,Prod_Can2_Flow", null, "Prod_Can2_Flow", "Prod_Can2_Flow"),
        new("si1,Prod_Can2_Sec", null, "Prod_Can2_Sec", "Prod_Can2_Sec"),
        new("si1,Prod_Can2_Prs", null, "Prod_Can2_Prs", "Prod_Can2_Prs"),
        new("si1,Prod_Bomb2_LotNo", null, "Prod_Bomb2_LotNo", "Prod_Bomb2_LotNo"),
        new("si1,Prod_Bomb2_Flow", null, "Prod_Bomb2_Flow", "Prod_Bomb2_Flow"),
        new("si1,Prod_Bomb2_Sec", null, "Prod_Bomb2_Sec", "Prod_Bomb2_Sec"),
        new("si1,Prod_Bomb2_Prs", null, "Prod_Bomb2_Prs", "Prod_Bomb2_Prs"),
        new("si1,Prod_Bomb1_LotNo", null, "Prod_Bomb1_LotNo", "Prod_Bomb1_LotNo"),
        new("si1,Prod_Bomb1_SetFillingPrs", null, "Prod_Bomb1_SetFillingPrs", "Prod_Bomb1_SetFillingPrs"),
        new("si1,Prod_Bomb1_Prs", null, "Prod_Bomb1_Prs", "Prod_Bomb1_Prs"),
        new("si1,Prod_Bomb3_LotNo", null, "Prod_Bomb3_LotNo", "Prod_Bomb3_LotNo"),
        new("si1,Prod_Bomb3_SetFillingPrs", null, "Prod_Bomb3_SetFillingPrs", "Prod_Bomb3_SetFillingPrs"),
        new("si1,Prod_Bomb3_Prs", null, "Prod_Bomb3_Prs", "Prod_Bomb3_Prs"),
        new("si1,SampleNo", null, "SampleNo", "SampleNo"),
        new("si1,SampleType", null, "SampleType", "SampleType"),
        new("si1,Container", null, "Container", "Container"),
        new("si1,ProdOrder", null, "ProdOrder", "ProdOrder"),
        new("si1,FnlPrs", null, "FnlPrs", "FnlPrs"),
        new("si1,IniPrs", null, "IniPrs", "IniPrs"),
        new("si1,QCTime", null, "QCTime", "QCTime"),
        new("si1,QCInst", null, "QCInst", "QCInst"),
        new("si1,QCPort", null, "QCPort", "QCPort"),
        new("si1,Cal_id", null, "Cal_id", "Cal_id"),
        new("si1,RF_ID", null, "RF_ID", "RF_ID"),
        new("si1,QCComplete", null, "QCComplete", "QCComplete"),
        new("si1,CalType", null, "CalType", "CalType"),
        new("si1,Result", null, "Result", "Result"),
        new("si1,FailDesc", null, "FailDesc", "FailDesc")
    ];

    public StdCylinderSummaryDownload ExportForDownload(IReadOnlyCollection<StdCylinderSummaryRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(SheetName);
        var columns = BuildColumns();

        WriteHeaders(worksheet, columns);
        WriteRows(worksheet, rows, columns);
        ApplyLayout(worksheet, rows.Count, columns.Count);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new StdCylinderSummaryDownload(
            stream.ToArray(),
            ContentType,
            $"STD_Cylinder\u7e3d\u8868[{DateTime.Now:yyyyMMddHHmmss}].xlsx");
    }

    private static List<SummaryColumn> BuildColumns()
    {
        var columns = FixedColumns.ToList();
        columns.AddRange(CompoundMap.Analytes.Select(analyte => new SummaryColumn(
            $"si2,ppb,{CasCodeByAnalyte.GetValueOrDefault(analyte.Suffix, analyte.Suffix)}",
            "ppb",
            DisplayHeaderByAnalyte.GetValueOrDefault(analyte.Suffix, analyte.Suffix),
            analyte.Suffix,
            IsPpbColumn: true)));
        columns.Add(new SummaryColumn("si1,Note", null, "Note", "Note"));
        columns.Add(new SummaryColumn("Excel,ExcelExportSessionId", null, "ExcelGuid", "ExcelExportSessionId"));
        return columns;
    }

    private static void WriteHeaders(IXLWorksheet worksheet, IReadOnlyList<SummaryColumn> columns)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            var column = index + 1;
            worksheet.Cell(1, column).Value = columns[index].SourceHeader;
            worksheet.Cell(2, column).Value = columns[index].PpbHeader ?? string.Empty;
            worksheet.Cell(3, column).Value = columns[index].DisplayHeader;
        }
    }

    private static void WriteRows(
        IXLWorksheet worksheet,
        IReadOnlyCollection<StdCylinderSummaryRow> rows,
        IReadOnlyList<SummaryColumn> columns)
    {
        var rowNumber = DataStartRow;
        foreach (var row in rows)
        {
            for (var index = 0; index < columns.Count; index++)
            {
                var column = columns[index];
                var value = column.IsPpbColumn
                    ? row.Areas.GetValueOrDefault(column.ValueKey)
                    : ResolveValue(row, column.ValueKey);
                SetCellValue(worksheet.Cell(rowNumber, index + 1), value);
            }

            rowNumber++;
        }
    }

    private static object? ResolveValue(StdCylinderSummaryRow row, string key)
    {
        if (string.Equals(key, "ExcelExportSessionId", StringComparison.OrdinalIgnoreCase))
        {
            return row.ExcelExportSessionId?.ToString("D");
        }

        return row.Values.GetValueOrDefault(key);
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        if (value is null or DBNull)
        {
            return;
        }

        switch (value)
        {
            case Guid guid:
                cell.Value = guid.ToString("D");
                return;
            case DateTime dateTime:
                cell.Value = dateTime;
                cell.Style.DateFormat.Format = "yyyy/m/d h:mm";
                return;
            case decimal decimalValue:
                cell.Value = (double)decimalValue;
                return;
            case double doubleValue:
                cell.Value = doubleValue;
                return;
            case float floatValue:
                cell.Value = (double)floatValue;
                return;
            case int intValue:
                cell.Value = intValue;
                return;
            case long longValue:
                cell.Value = longValue;
                return;
            case short shortValue:
                cell.Value = shortValue;
                return;
            case byte byteValue:
                cell.Value = byteValue;
                return;
            case bool boolValue:
                cell.Value = boolValue;
                return;
            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                return;
        }
    }

    private static void ApplyLayout(IXLWorksheet worksheet, int rowCount, int columnCount)
    {
        var headerRange = worksheet.Range(1, 1, HeaderRowCount, columnCount);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        worksheet.Row(1).Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        worksheet.Row(2).Style.Font.FontColor = XLColor.FromHtml("#C00000");
        worksheet.SheetView.FreezeRows(HeaderRowCount);

        var lastRow = Math.Max(HeaderRowCount, rowCount + HeaderRowCount);
        worksheet.Range(3, 1, lastRow, columnCount).SetAutoFilter();
        worksheet.Range(1, 1, lastRow, columnCount).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        worksheet.Range(1, 1, lastRow, columnCount).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        for (var column = 1; column <= columnCount; column++)
        {
            worksheet.Column(column).Width = column <= FixedColumns.Length ? 16D : 14D;
        }

        worksheet.Column(columnCount).Width = 38D;
    }

    private sealed record SummaryColumn(
        string SourceHeader,
        string? PpbHeader,
        string DisplayHeader,
        string ValueKey,
        bool IsPpbColumn = false);
}
