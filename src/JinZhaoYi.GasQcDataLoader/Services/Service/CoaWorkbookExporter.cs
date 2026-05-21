using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class CoaWorkbookExporter(IOptions<SchedulerOptions> options) : ICoaWorkbookExporter
{
    private const string Large500MlSheetName = "COA(500 mL)";
    private const string Large1LSheetName = "COA(1 L)";
    private const string LargeYadongSheetName = "COA(亞東)";
    private const string SmallBlankSheetName = "Report(空白)";

    private static readonly IReadOnlyDictionary<string, string> LargeComponentSuffixes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dichlorotetrafluoroethane (FC-114)"] = "Freon114",
            ["1,1-Dichloroethylene"] = "1,1-Dichloroethene",
            ["1,1,2-Trichlorotrifluoroethane(FC-113)"] = "Freon113",
            ["Methylene Chloride"] = "Methlene",
            ["1,1-Dichloroethane"] = "1,1-Dichloroethane",
            ["cis-1,2-Dichloroethylene"] = "cis-1,2-Dichloroethene",
            ["Trichloromethane (HC-20)"] = "Freon20",
            ["1,1,1-Trichloroethane"] = "1,1,1-Trichloroethane",
            ["1,2-Dichloroethane"] = "1,2-Dichloroethane",
            ["Benzene"] = "Benzene",
            ["Carbon Tetrachloride"] = "Carbon Tetrachloride",
            ["Trichloroethylene"] = "Trichloroethylene",
            ["1,2-Dichloropropane"] = "1,2-Dichloropropane",
            ["cis-1,3-Dichloropropylene"] = "cis-1,3-Dichloropropene",
            ["trans-1,3-Dichloropropylene"] = "trans-1,3-Dichloropropene",
            ["Toluene"] = "Toluene",
            ["1,1,2-Trichloroethane"] = "1,1,2-Trichloroethane",
            ["Tetrachloroethylene"] = "Tetrachloroethylene",
            ["1,2-Dibromoethane"] = "1,2-Dibromoethane",
            ["Chlorobenzene"] = "ChloroBenzene",
            ["Ethyl Benzene"] = "Ethylbenzene",
            ["p&m Xylenes(mixed)"] = "p-Xylene",
            ["Styrene"] = "Styrene",
            ["o-Xylene"] = "o-Xylene",
            ["1,1,2,2-Tetrachloroethane"] = "1,1,2,2-Tetrachloroethane",
            ["1,3,5-Trimethylbenzene"] = "1,3,5-TMB",
            ["1,2,4-Trimethylbenzene"] = "1,2,4-TMB",
            ["1,3-Dichlorobenzene"] = "1,3-Dichlorobenzene",
            ["1,4-Dichlorobenzene"] = "1,4-Dichlorobenzene",
            ["1,2-Dichlorobenzene"] = "1,2-Dichlorobenzene",
            ["1,2,4-Trichlorobenzene"] = "1,2,4-TCB",
            ["Hexachloro-1,3-Butadiene"] = "HCBD",
            ["Isopropanol"] = "IPA",
            ["Acetone"] = "Acetone",
            ["Perfluorotributylamine (HC-43)"] = "CNF",
            ["2-Butanone (MEK)"] = "2-Butanone",
            ["Ethyl Acetate"] = "Ethyl Acetate",
            ["Cyclopentane"] = "Cyclopentane"
        };

    private static readonly IReadOnlyList<string> SmallCardSuffixes =
    [
        "Acetone",
        "IPA",
        "CNF",
        "Cyclopentane",
        "2-Butanone",
        "Ethyl Acetate",
        "Benzene",
        "Toluene",
        "1,2,4-TMB"
    ];

    private static readonly IReadOnlyList<SmallCardLayout> SmallCardLayouts =
    [
        new("F6", "F8", "B19", "F19", "B20"),
        new("O6", "O8", "K19", "O19", "K20"),
        new("X6", "X8", "T19", "X19", "T20"),
        new("F28", "F30", "B41", "F41", "B42"),
        new("O28", "O30", "K41", "O41", "K42"),
        new("X28", "X30", "T41", "X41", "T42"),
        new("F50", "F52", "B63", "F63", "B64"),
        new("O50", "O52", "K63", "O63", "K64"),
        new("X50", "X52", "T63", "X63", "T64")
    ];

    private readonly SchedulerOptions _options = options.Value;

    public CoaWorkbookDownload ExportLargeForDownload(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        CoaLargeTemplateType templateType)
    {
        var orderedRows = OrderRows(rows);
        var templatePath = ResolveTemplatePath(_options.CoaExport.LargeTemplatePath, "COA(大卡).xlsx");

        // COA 只從 Excel PPB history 匯出，避免和匯入流程即時計算的 PORT_PPB 混用。
        using var templateWorkbook = new XLWorkbook(templatePath);
        using var workbook = new XLWorkbook();
        foreach (var templateSheetName in new[] { Large500MlSheetName, Large1LSheetName, LargeYadongSheetName })
        {
            RenamePictures(templateWorkbook.Worksheet(templateSheetName), 0);
        }

        for (var index = 0; index < orderedRows.Count; index++)
        {
            var row = orderedRows[index];
            var sourceSheetName = ResolveLargeSourceSheetName(row, templateType);
            var worksheet = templateWorkbook.Worksheet(sourceSheetName)
                .CopyTo(workbook, BuildUniqueSheetName(workbook, $"COA_{row.SampleName}", index + 1));
            RenamePictures(worksheet, index + 1);
            WriteLargeRow(worksheet, row);
        }

        return BuildDownload(workbook, $"COA大卡[{batchDateText}].xlsx");
    }

    public CoaWorkbookDownload ExportSmallForDownload(
        IReadOnlyCollection<QcDataRow> rows,
        string batchDateText,
        int cardsPerPage)
    {
        if (cardsPerPage is < 1 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(cardsPerPage), "cardsPerPage must be between 1 and 9.");
        }

        var orderedRows = OrderRows(rows);
        var templatePath = ResolveTemplatePath(_options.CoaExport.SmallTemplatePath, "COA(小卡).xlsx");

        // 小卡一頁最多九格；超過使用者選擇的格數就複製下一頁 sheet。
        using var templateWorkbook = new XLWorkbook(templatePath);
        using var workbook = new XLWorkbook();
        var template = templateWorkbook.Worksheet(SmallBlankSheetName);
        RenamePictures(template, 0);
        var pageCount = Math.Max(1, (int)Math.Ceiling(orderedRows.Count / (double)cardsPerPage));

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var worksheet = template.CopyTo(workbook, BuildUniqueSheetName(workbook, $"COA小卡{pageIndex + 1}", pageIndex + 1));
            RenamePictures(worksheet, pageIndex + 1);
            ClearSmallDynamicCells(worksheet);

            var pageRows = orderedRows
                .Skip(pageIndex * cardsPerPage)
                .Take(cardsPerPage)
                .ToArray();

            for (var cardIndex = 0; cardIndex < pageRows.Length; cardIndex++)
            {
                WriteSmallCard(worksheet, SmallCardLayouts[cardIndex], pageRows[cardIndex]);
            }
        }

        return BuildDownload(workbook, $"COA小卡[{batchDateText}].xlsx");
    }

    private string ResolveLargeSourceSheetName(QcDataRow row, CoaLargeTemplateType templateType)
    {
        if (templateType == CoaLargeTemplateType.Yadong)
        {
            return LargeYadongSheetName;
        }

        return row.Container?.Contains("0.5", StringComparison.OrdinalIgnoreCase) == true
            ? Large500MlSheetName
            : Large1LSheetName;
    }

    private void WriteLargeRow(IXLWorksheet worksheet, QcDataRow row)
    {
        worksheet.Cell("B12").Value = FormatDate(row.AnlzTime);
        worksheet.Cell("B13").Value = FormatDate(ResolveExpirationDate(row));
        worksheet.Cell("B15").Value = row.SampleName ?? string.Empty;

        for (var rowNumber = 19; rowNumber <= 57; rowNumber++)
        {
            var componentName = worksheet.Cell(rowNumber, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(componentName) ||
                !LargeComponentSuffixes.TryGetValue(componentName, out var suffix))
            {
                continue;
            }

            WriteDecimal(worksheet.Cell(rowNumber, 5), row.Areas.GetValueOrDefault(suffix));
        }
    }

    private void WriteSmallCard(IXLWorksheet worksheet, SmallCardLayout layout, QcDataRow row)
    {
        worksheet.Cell(layout.SampleCell).Value = row.SampleName ?? string.Empty;
        worksheet.Cell(layout.MotherLotCell).Value = $"母瓶 NO.  {_options.CsvExport.RawLotId}";
        worksheet.Cell(layout.QcDateCell).Value = $"QC: {FormatDate(row.AnlzTime)}";
        worksheet.Cell(layout.ExpirationCell).Value = $"{row.SampleName}有效期限：{FormatDate(ResolveExpirationDate(row))}";

        var resultCell = worksheet.Cell(layout.FirstResultCell);
        for (var index = 0; index < SmallCardSuffixes.Count; index++)
        {
            WriteDecimal(resultCell.CellBelow(index), row.Areas.GetValueOrDefault(SmallCardSuffixes[index]));
        }
    }

    private static void ClearSmallDynamicCells(IXLWorksheet worksheet)
    {
        foreach (var layout in SmallCardLayouts)
        {
            worksheet.Cell(layout.SampleCell).Clear(XLClearOptions.Contents);
            worksheet.Cell(layout.MotherLotCell).Clear(XLClearOptions.Contents);
            worksheet.Cell(layout.QcDateCell).Clear(XLClearOptions.Contents);
            worksheet.Cell(layout.ExpirationCell).Clear(XLClearOptions.Contents);

            var resultCell = worksheet.Cell(layout.FirstResultCell);
            for (var index = 0; index < SmallCardSuffixes.Count; index++)
            {
                resultCell.CellBelow(index).Clear(XLClearOptions.Contents);
            }
        }
    }

    private static IReadOnlyList<QcDataRow> OrderRows(IReadOnlyCollection<QcDataRow> rows) =>
        rows
            .OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.SampleNo)
            .ThenBy(row => row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SampleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string ResolveTemplatePath(string? configuredPath, string defaultTemplateFileName)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "templates", defaultTemplateFileName)
            : configuredPath;

        path = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"COA template not found: {path}", path);
        }

        return path;
    }

    private static DateTime? ResolveExpirationDate(QcDataRow row) =>
        row.AnlzTime?.Date.AddDays(364);

    private static string FormatDate(DateTime? value) =>
        value?.ToString("yyyy/M/d", CultureInfo.InvariantCulture) ?? string.Empty;

    private static void WriteDecimal(IXLCell cell, decimal? value)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            return;
        }

        cell.Clear(XLClearOptions.Contents);
    }

    private static void RenamePictures(IXLWorksheet worksheet, int sheetIndex)
    {
        var pictureIndex = 1;
        foreach (var picture in worksheet.Pictures)
        {
            picture.Name = $"{worksheet.Name}_Image_{sheetIndex}_{pictureIndex++}";
        }
    }

    private static CoaWorkbookDownload BuildDownload(XLWorkbook workbook, string fileName)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new CoaWorkbookDownload(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static string BuildUniqueSheetName(XLWorkbook workbook, string? preferredName, int index)
    {
        var baseName = SanitizeSheetName(string.IsNullOrWhiteSpace(preferredName) ? $"COA{index}" : preferredName);
        if (baseName.Length > 25)
        {
            baseName = baseName[..25];
        }

        for (var attempt = 0; ; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : $"_{attempt + 1}";
            var candidate = $"{baseName}{suffix}";
            if (candidate.Length > 31)
            {
                candidate = candidate[..31];
            }

            if (!workbook.Worksheets.Any(sheet => string.Equals(sheet.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeSheetName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? '_' : character);
        }

        var sheetName = builder.ToString().Trim('\'', ' ');
        return string.IsNullOrWhiteSpace(sheetName) ? "COA" : sheetName;
    }

    private sealed record SmallCardLayout(
        string SampleCell,
        string FirstResultCell,
        string MotherLotCell,
        string QcDateCell,
        string ExpirationCell);
}
