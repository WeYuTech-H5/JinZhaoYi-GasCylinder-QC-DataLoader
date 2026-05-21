using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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

        var content = ExportLargeWorkbookToBytes(templatePath, orderedRows, templateType);
        return new CoaWorkbookDownload(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"COA(大卡)_{batchDateText}.xlsx");
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

        var content = ExportSmallWorkbookToBytes(templatePath, orderedRows, cardsPerPage);
        return new CoaWorkbookDownload(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"COA(小卡)_{batchDateText}.xlsx");
    }

    private byte[] ExportLargeWorkbookToBytes(
        string templatePath,
        IReadOnlyList<QcDataRow> orderedRows,
        CoaLargeTemplateType templateType)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(templatePath));
        using var document = SpreadsheetDocument.Open(stream, true);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("COA large template has no workbook part.");
        var sheets = workbookPart.Workbook.Sheets ?? throw new InvalidOperationException("COA large template has no sheets.");

        // 大卡模板含頁首 logo 與公司資訊圖片；直接複製 OpenXML worksheet 與 drawing 關聯，避免 ClosedXML 存檔時遺失圖片內容。
        for (var index = 0; index < orderedRows.Count; index++)
        {
            var row = orderedRows[index];
            var sourceSheetName = ResolveLargeSourceSheetName(row, templateType);
            var sourceSheet = sheets.Elements<Sheet>()
                .FirstOrDefault(sheet => string.Equals(sheet.Name?.Value, sourceSheetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"COA large template does not contain worksheet '{sourceSheetName}'.");
            var sourcePart = (WorksheetPart)workbookPart.GetPartById(sourceSheet.Id!);
            var targetSheetName = BuildUniqueOpenXmlSheetName(workbookPart, $"COA_{row.SampleName}", index + 1);
            var targetPart = CloneWorksheetPartWithRelationships(workbookPart, sourcePart, targetSheetName);
            WriteLargeRow(targetPart, row);
        }

        DeleteOpenXmlSheets(workbookPart, Large500MlSheetName, Large1LSheetName, LargeYadongSheetName, "欄位註解");
        DeleteCalculationChain(workbookPart);
        ResetWorkbookView(workbookPart);
        workbookPart.Workbook.Save();
        document.Dispose();
        return stream.ToArray();
    }

    private byte[] ExportSmallWorkbookToBytes(string templatePath, IReadOnlyList<QcDataRow> orderedRows, int cardsPerPage)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(templatePath));
        using var document = SpreadsheetDocument.Open(stream, true);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("COA small template has no workbook part.");
        var sheets = workbookPart.Workbook.Sheets ?? throw new InvalidOperationException("COA small template has no sheets.");
        var templateSheet = sheets.Elements<Sheet>()
            .FirstOrDefault(sheet => string.Equals(sheet.Name?.Value, SmallBlankSheetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"COA small template does not contain worksheet '{SmallBlankSheetName}'.");
        var templatePart = (WorksheetPart)workbookPart.GetPartById(templateSheet.Id!);

        // 小卡的「格數」代表同一筆 Excel PPB history 要印幾張貼紙；例如 9 格就是 9 格都填同一筆資料。
        var sheetCount = Math.Max(1, orderedRows.Count);
        for (var pageIndex = 0; pageIndex < sheetCount; pageIndex++)
        {
            var row = orderedRows.ElementAtOrDefault(pageIndex);
            var targetSheetName = BuildUniqueOpenXmlSheetName(
                workbookPart,
                row is null ? $"COA小卡{pageIndex + 1}" : $"COA小卡_{row.SampleName}",
                pageIndex + 1);
            var worksheetPart = pageIndex == 0
                ? templatePart
                : CloneWorksheetPartWithoutDrawings(workbookPart, templatePart, targetSheetName);

            if (pageIndex == 0)
            {
                templateSheet.Name = targetSheetName;
            }

            ClearSmallDynamicCells(worksheetPart);

            if (row is null)
            {
                continue;
            }

            for (var cardIndex = 0; cardIndex < cardsPerPage; cardIndex++)
            {
                WriteSmallCard(worksheetPart, SmallCardLayouts[cardIndex], row);
            }
        }

        DeleteOpenXmlSheets(workbookPart, "Report(範本)", "欄位註解");
        DeleteCalculationChain(workbookPart);
        ResetWorkbookView(workbookPart);
        workbookPart.Workbook.Save();
        document.Dispose();
        return stream.ToArray();
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

    private void WriteLargeRow(WorksheetPart worksheetPart, QcDataRow row)
    {
        SetStringCell(worksheetPart, "B12", FormatDate(row.AnlzTime));
        // 目前來源資料沒有獨立的鋼瓶到期日欄位，先依既有規則用分析時間 AnlzTime + 364 天計算。
        SetStringCell(worksheetPart, "B13", FormatDate(ResolveExpirationDate(row)));
        SetStringCell(worksheetPart, "B15", row.SampleName ?? string.Empty);

        for (uint rowNumber = 19; rowNumber <= 57; rowNumber++)
        {
            var componentName = GetCellText(worksheetPart, $"A{rowNumber}").Trim();
            if (string.IsNullOrWhiteSpace(componentName) ||
                !LargeComponentSuffixes.TryGetValue(componentName, out var suffix))
            {
                continue;
            }

            SetDecimalCell(worksheetPart, $"E{rowNumber}", row.Areas.GetValueOrDefault(suffix));
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

    private void WriteSmallCard(WorksheetPart worksheetPart, SmallCardLayout layout, QcDataRow row)
    {
        SetStringCell(worksheetPart, layout.SampleCell, row.SampleName ?? string.Empty);
        SetStringCell(worksheetPart, layout.MotherLotCell, $"母瓶 NO.  {_options.CsvExport.RawLotId}");
        SetStringCell(worksheetPart, layout.QcDateCell, $"QC: {FormatDate(row.AnlzTime)}");
        SetStringCell(worksheetPart, layout.ExpirationCell, $"{row.SampleName}有效期限：{FormatDate(ResolveExpirationDate(row))}");

        var firstResult = SplitCellReference(layout.FirstResultCell);
        for (var index = 0; index < SmallCardSuffixes.Count; index++)
        {
            SetDecimalCell(
                worksheetPart,
                $"{firstResult.Column}{firstResult.Row + index}",
                row.Areas.GetValueOrDefault(SmallCardSuffixes[index]));
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

    private static void ClearSmallDynamicCells(WorksheetPart worksheetPart)
    {
        foreach (var layout in SmallCardLayouts)
        {
            SetStringCell(worksheetPart, layout.SampleCell, string.Empty);
            SetStringCell(worksheetPart, layout.MotherLotCell, string.Empty);
            SetStringCell(worksheetPart, layout.QcDateCell, string.Empty);
            SetStringCell(worksheetPart, layout.ExpirationCell, string.Empty);

            var firstResult = SplitCellReference(layout.FirstResultCell);
            for (var index = 0; index < SmallCardSuffixes.Count; index++)
            {
                SetStringCell(worksheetPart, $"{firstResult.Column}{firstResult.Row + index}", string.Empty);
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

    private static WorksheetPart CloneWorksheetPartWithoutDrawings(WorkbookPart workbookPart, WorksheetPart templatePart, string sheetName)
    {
        var newPart = workbookPart.AddNewPart<WorksheetPart>();
        using (var sourceStream = templatePart.GetStream(FileMode.Open, FileAccess.Read))
        using (var targetStream = newPart.GetStream(FileMode.Create, FileAccess.Write))
        {
            RemoveDrawingElements(sourceStream, targetStream);
        }

        var sheets = workbookPart.Workbook.Sheets ?? workbookPart.Workbook.AppendChild(new Sheets());
        var nextSheetId = sheets.Elements<Sheet>().Select(sheet => sheet.SheetId?.Value ?? 0U).DefaultIfEmpty().Max() + 1;
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(newPart),
            SheetId = nextSheetId,
            Name = sheetName
        });

        return newPart;
    }

    private static WorksheetPart CloneWorksheetPartWithRelationships(WorkbookPart workbookPart, WorksheetPart templatePart, string sheetName)
    {
        var newPart = workbookPart.AddNewPart<WorksheetPart>();
        using (var sourceStream = templatePart.GetStream(FileMode.Open, FileAccess.Read))
        using (var targetStream = newPart.GetStream(FileMode.Create, FileAccess.Write))
        {
            sourceStream.CopyTo(targetStream);
        }

        CopyPartRelationships(templatePart, newPart);

        var sheets = workbookPart.Workbook.Sheets ?? workbookPart.Workbook.AppendChild(new Sheets());
        var nextSheetId = sheets.Elements<Sheet>().Select(sheet => sheet.SheetId?.Value ?? 0U).DefaultIfEmpty().Max() + 1;
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(newPart),
            SheetId = nextSheetId,
            Name = sheetName
        });

        return newPart;
    }

    private static void CopyPartRelationships(OpenXmlPart sourcePart, OpenXmlPart targetPart)
    {
        foreach (var relationship in sourcePart.Parts)
        {
            targetPart.AddPart(relationship.OpenXmlPart, relationship.RelationshipId);
        }

        foreach (var relationship in sourcePart.ExternalRelationships)
        {
            targetPart.AddExternalRelationship(relationship.RelationshipType, relationship.Uri, relationship.Id);
        }

        foreach (var relationship in sourcePart.HyperlinkRelationships)
        {
            targetPart.AddHyperlinkRelationship(relationship.Uri, relationship.IsExternal, relationship.Id);
        }
    }

    private static void DeleteOpenXmlSheets(WorkbookPart workbookPart, params string[] sheetNames)
    {
        var sheets = workbookPart.Workbook.Sheets;
        if (sheets is null)
        {
            return;
        }

        foreach (var sheetName in sheetNames)
        {
            var sheet = sheets.Elements<Sheet>()
                .FirstOrDefault(item => string.Equals(item.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet?.Id is null)
            {
                continue;
            }

            var part = workbookPart.GetPartById(sheet.Id!);
            sheet.Remove();
            workbookPart.DeletePart(part);
        }
    }

    private static void ResetWorkbookView(WorkbookPart workbookPart)
    {
        foreach (var view in workbookPart.Workbook.BookViews?.Elements<WorkbookView>() ?? [])
        {
            view.ActiveTab = 0U;
            view.FirstSheet = 0U;
        }
    }

    private static void DeleteCalculationChain(WorkbookPart workbookPart)
    {
        if (workbookPart.CalculationChainPart is not null)
        {
            workbookPart.DeletePart(workbookPart.CalculationChainPart);
        }

        workbookPart.Workbook.CalculationProperties ??= new CalculationProperties();
        workbookPart.Workbook.CalculationProperties.ForceFullCalculation = true;
        workbookPart.Workbook.CalculationProperties.FullCalculationOnLoad = true;
    }

    private static void SetStringCell(WorksheetPart worksheetPart, string cellReference, string value)
    {
        var cell = GetOrCreateCell(worksheetPart, cellReference);
        cell.CellFormula?.Remove();
        cell.DataType = CellValues.String;
        cell.CellValue = new CellValue(value);
    }

    private static void SetDecimalCell(WorksheetPart worksheetPart, string cellReference, decimal? value)
    {
        var cell = GetOrCreateCell(worksheetPart, cellReference);
        cell.CellFormula?.Remove();
        if (!value.HasValue)
        {
            cell.DataType = CellValues.String;
            cell.CellValue = new CellValue(string.Empty);
            return;
        }

        cell.DataType = null;
        cell.CellValue = new CellValue(value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static Cell GetOrCreateCell(WorksheetPart worksheetPart, string cellReference)
    {
        var worksheet = worksheetPart.Worksheet;
        var sheetData = worksheet.GetFirstChild<SheetData>() ?? worksheet.AppendChild(new SheetData());
        var reference = SplitCellReference(cellReference);
        var row = sheetData.Elements<Row>().FirstOrDefault(item => item.RowIndex?.Value == reference.Row);
        if (row is null)
        {
            row = new Row { RowIndex = reference.Row };
            var nextRow = sheetData.Elements<Row>().FirstOrDefault(item => (item.RowIndex?.Value ?? 0U) > reference.Row);
            sheetData.InsertBefore(row, nextRow);
        }

        var cell = row.Elements<Cell>()
            .FirstOrDefault(item => string.Equals(item.CellReference?.Value, cellReference, StringComparison.OrdinalIgnoreCase));
        if (cell is not null)
        {
            return cell;
        }

        cell = new Cell { CellReference = cellReference };
        var nextCell = row.Elements<Cell>()
            .FirstOrDefault(item => ColumnIndex(SplitCellReference(item.CellReference?.Value ?? "A1").Column) > ColumnIndex(reference.Column));
        row.InsertBefore(cell, nextCell);
        return cell;
    }

    private static string GetCellText(WorksheetPart worksheetPart, string cellReference)
    {
        var cell = worksheetPart.Worksheet.Descendants<Cell>()
            .FirstOrDefault(item => string.Equals(item.CellReference?.Value, cellReference, StringComparison.OrdinalIgnoreCase));
        if (cell?.CellValue?.Text is null)
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            var sharedStringPart = worksheetPart.GetParentParts()
                .OfType<WorkbookPart>()
                .FirstOrDefault()
                ?.SharedStringTablePart;
            return sharedStringPart?.SharedStringTable
                ?.Elements<SharedStringItem>()
                .ElementAtOrDefault(int.Parse(cell.CellValue.Text, CultureInfo.InvariantCulture))
                ?.InnerText ?? string.Empty;
        }

        return cell.CellValue.Text;
    }

    private static IXLWorksheet CopySheetFromTemplate(
        string templatePath,
        string sourceSheetName,
        XLWorkbook targetWorkbook,
        string targetSheetName,
        int sheetIndex,
        bool stripDrawings = false)
    {
        using var templateWorkbook = stripDrawings
            ? OpenSmallTemplateWorkbook(templatePath)
            : new XLWorkbook(templatePath);
        var sourceSheet = templateWorkbook.Worksheet(sourceSheetName);
        RenamePictures(sourceSheet, sheetIndex);
        var worksheet = sourceSheet.CopyTo(targetWorkbook, targetSheetName);
        RenamePictures(worksheet, sheetIndex);
        return worksheet;
    }

    private static XLWorkbook OpenSmallTemplateWorkbook(string templatePath)
    {
        // 小卡原檔含舊式 WMF drawing；ClosedXML 重存後 Excel 可能無法開啟，因此匯出前移除 drawing parts，保留儲存格樣式與列印版型。
        return new XLWorkbook(RemoveDrawingParts(templatePath));
    }

    private static MemoryStream RemoveDrawingParts(string templatePath)
    {
        var stream = new MemoryStream();
        using (var source = ZipFile.OpenRead(templatePath))
        using (var target = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                if (entry.FullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.StartsWith("xl/worksheets/_rels/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var targetEntry = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var sourceStream = entry.Open();
                using var targetStream = targetEntry.Open();

                if (entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                    entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveDrawingElements(sourceStream, targetStream);
                    continue;
                }

                sourceStream.CopyTo(targetStream);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void RemoveDrawingElements(Stream sourceStream, Stream targetStream)
    {
        XNamespace spreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var document = XDocument.Load(sourceStream);
        document.Descendants(spreadsheetNamespace + "drawing").Remove();
        document.Descendants(spreadsheetNamespace + "legacyDrawing").Remove();
        document.Save(targetStream);
    }

    private static (string Column, uint Row) SplitCellReference(string cellReference)
    {
        var column = new StringBuilder();
        var row = new StringBuilder();
        foreach (var character in cellReference)
        {
            if (char.IsLetter(character))
            {
                column.Append(character);
            }
            else if (char.IsDigit(character))
            {
                row.Append(character);
            }
        }

        return (column.ToString().ToUpperInvariant(), uint.Parse(row.ToString(), CultureInfo.InvariantCulture));
    }

    private static int ColumnIndex(string column)
    {
        var index = 0;
        foreach (var character in column)
        {
            index = index * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return index;
    }

    private static void DeleteUnusedSheets(XLWorkbook workbook, params string[] templateSheetNames)
    {
        foreach (var sheetName in templateSheetNames)
        {
            var worksheet = workbook.Worksheets.FirstOrDefault(sheet => string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase));
            worksheet?.Delete();
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

    private static string BuildUniqueOpenXmlSheetName(WorkbookPart workbookPart, string preferredName, int index)
    {
        var usedNames = workbookPart.Workbook.Sheets?.Elements<Sheet>()
            .Select(sheet => sheet.Name?.Value ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

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

            if (!usedNames.Contains(candidate))
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
