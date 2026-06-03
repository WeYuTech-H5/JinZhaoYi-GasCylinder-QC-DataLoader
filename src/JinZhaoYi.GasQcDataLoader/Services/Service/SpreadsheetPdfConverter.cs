using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class SpreadsheetPdfConverter(IOptions<SchedulerOptions> options) : ISpreadsheetPdfConverter
{
    private readonly SchedulerCoaExportOptions _options = options.Value.CoaExport;

    public async Task<byte[]> ConvertXlsxToPdfAsync(
        byte[] workbookContent,
        string workbookFileName,
        CancellationToken cancellationToken)
    {
        var excelPdfContent = TryConvertWithExcel(workbookContent);
        if (excelPdfContent is not null)
        {
            return excelPdfContent;
        }

        if (string.IsNullOrWhiteSpace(_options.LibreOfficePath) &&
            !IsExecutableAvailable("soffice"))
        {
            return ConvertWithFallback(workbookContent, workbookFileName);
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"gas-qc-coa-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var workbookPath = Path.Combine(tempDirectory, "input.xlsx");
            var pdfWorkbookContent = PrepareWorkbookForPdfConversion(workbookContent);
            await File.WriteAllBytesAsync(workbookPath, pdfWorkbookContent, cancellationToken);
            var userProfileDirectory = Path.Combine(tempDirectory, "lo-profile");
            Directory.CreateDirectory(userProfileDirectory);

            var executablePath = string.IsNullOrWhiteSpace(_options.LibreOfficePath)
                ? "soffice"
                : _options.LibreOfficePath.Trim();

            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = tempDirectory
            };
            processStartInfo.ArgumentList.Add("--headless");
            processStartInfo.ArgumentList.Add($"-env:UserInstallation={BuildFileUri(userProfileDirectory)}");
            processStartInfo.ArgumentList.Add("--convert-to");
            processStartInfo.ArgumentList.Add("pdf");
            processStartInfo.ArgumentList.Add("--outdir");
            processStartInfo.ArgumentList.Add(tempDirectory);
            processStartInfo.ArgumentList.Add(workbookPath);

            using var process = Process.Start(processStartInfo)
                ?? throw new InvalidOperationException("Unable to start LibreOffice PDF converter.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.PdfConversionTimeoutSeconds));
            var exited = await WaitForExitAsync(process, timeout, cancellationToken);

            if (!exited)
            {
                TryKill(process);
                throw new InvalidOperationException($"LibreOffice PDF conversion timed out after {timeout.TotalSeconds:0} seconds.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"LibreOffice PDF conversion failed with exit code {process.ExitCode}. {stderr}{stdout}");
            }

            var pdfPath = Path.ChangeExtension(workbookPath, ".pdf");
            if (!File.Exists(pdfPath))
            {
                throw new InvalidOperationException($"LibreOffice did not create PDF output for '{workbookFileName}'. {stderr}{stdout}");
            }

            return await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (_options.UseBasicPdfFallback)
            {
                return ConvertWithFallback(workbookContent, workbookFileName);
            }

            throw new InvalidOperationException(
                "LibreOffice PDF converter was not found. Set Scheduler:CoaExport:LibreOfficePath or add soffice to PATH.",
                ex);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string BuildFileUri(string directoryPath)
    {
        var path = directoryPath.EndsWith(Path.DirectorySeparatorChar)
            ? directoryPath
            : directoryPath + Path.DirectorySeparatorChar;
        return new Uri(path).AbsoluteUri;
    }

    private static byte[] PrepareWorkbookForPdfConversion(byte[] workbookContent)
    {
        using var stream = new MemoryStream();
        stream.Write(workbookContent, 0, workbookContent.Length);
        stream.Position = 0;

        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            var workbookPart = document.WorkbookPart;
            if (workbookPart is null)
            {
                return workbookContent;
            }

            var pdfHeaderImagePath = ResolvePdfHeaderImagePath();
            foreach (var worksheetPart in workbookPart.WorksheetParts)
            {
                ReplaceHeaderFooterWithPdfHeaderImage(worksheetPart, pdfHeaderImagePath);
            }

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static string? ResolvePdfHeaderImagePath()
    {
        var baseDirectory = Path.GetDirectoryName(typeof(SpreadsheetPdfConverter).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var imageDirectory = Path.Combine(baseDirectory, "wwwroot", "image");
        if (!Directory.Exists(imageDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(imageDirectory, "*.png")
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void ReplaceHeaderFooterWithPdfHeaderImage(WorksheetPart worksheetPart, string? pdfHeaderImagePath)
    {
        if (string.IsNullOrWhiteSpace(pdfHeaderImagePath) ||
            !File.Exists(pdfHeaderImagePath) ||
            !worksheetPart.Worksheet.Elements<LegacyDrawingHeaderFooter>().Any())
        {
            return;
        }

        AddWorksheetHeaderImageForPdf(worksheetPart, pdfHeaderImagePath);
        ReplaceHeaderFooterImagesWithPdfHeaderImage(worksheetPart, pdfHeaderImagePath);
        worksheetPart.Worksheet.Save();
    }

    private static void AddWorksheetHeaderImageForPdf(WorksheetPart worksheetPart, string pdfHeaderImagePath)
    {
        var drawingsPart = worksheetPart.DrawingsPart;
        if (drawingsPart?.WorksheetDrawing is null)
        {
            drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
            drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
            worksheetPart.Worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });
        }

        var imagePart = drawingsPart.AddImagePart(ImagePartType.Png);
        using (var sourceStream = File.OpenRead(pdfHeaderImagePath))
        using (var targetStream = imagePart.GetStream(FileMode.Create, FileAccess.Write))
        {
            sourceStream.CopyTo(targetStream);
        }

        drawingsPart.WorksheetDrawing.Append(CreateOneCellImageAnchor(
            drawingsPart.WorksheetDrawing,
            drawingsPart.GetIdOfPart(imagePart),
            "COA PDF Header",
            columnId: "0",
            rowId: "0",
            widthPoints: 350,
            heightPoints: 70));
        drawingsPart.WorksheetDrawing.Save();
    }

    private static void ReplaceHeaderFooterImagesWithPdfHeaderImage(WorksheetPart worksheetPart, string pdfHeaderImagePath)
    {
        var vmlPart = worksheetPart.VmlDrawingParts.FirstOrDefault();
        if (vmlPart is null)
        {
            return;
        }

        var headerImagePart = vmlPart.AddImagePart(ImagePartType.Png);
        using (var sourceStream = File.OpenRead(pdfHeaderImagePath))
        using (var targetStream = headerImagePart.GetStream(FileMode.Create, FileAccess.Write))
        {
            sourceStream.CopyTo(targetStream);
        }

        var headerRelationshipId = vmlPart.GetIdOfPart(headerImagePart);
        XNamespace v = "urn:schemas-microsoft-com:vml";
        XNamespace o = "urn:schemas-microsoft-com:office:office";

        XDocument vmlDocument;
        using (var vmlStream = vmlPart.GetStream(FileMode.Open, FileAccess.Read))
        {
            vmlDocument = XDocument.Load(vmlStream);
        }

        foreach (var shape in vmlDocument.Descendants(v + "shape").ToArray())
        {
            var shapeId = shape.Attribute("id")?.Value;
            if (shapeId == "CH")
            {
                AddVmlShapeImageAsWorksheetImage(worksheetPart, vmlPart, shape, v, o, shapeId);
            }
            if (shapeId == "LH")
            {
                shape.SetAttributeValue(
                    "style",
                    "position:absolute;margin-left:0;margin-top:0;width:550pt;height:110.6pt;z-index:2");
                shape.Element(v + "imagedata")?.SetAttributeValue(o + "relid", headerRelationshipId);
            }
            else if (shapeId == "RH")
            {
                shape.Remove();
            }
        }

        using (var vmlStream = vmlPart.GetStream(FileMode.Create, FileAccess.Write))
        {
            vmlDocument.Save(vmlStream);
        }

        var headerFooter = worksheetPart.Worksheet.Elements<HeaderFooter>().FirstOrDefault();
        if (headerFooter?.OddHeader is not null)
        {
            headerFooter.OddHeader.Text = "&C\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n";
        }
    }

    private static byte[]? TryConvertWithExcel(byte[] workbookContent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var excelType = Type.GetTypeFromProgID("Excel.Application");
        if (excelType is null)
        {
            return null;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"gas-qc-coa-excel-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        dynamic? excel = null;
        dynamic? workbook = null;

        try
        {
            var workbookPath = Path.Combine(tempDirectory, "input.xlsx");
            var pdfPath = Path.Combine(tempDirectory, "input.pdf");
            File.WriteAllBytes(workbookPath, workbookContent);

            excel = Activator.CreateInstance(excelType);
            if (excel is null)
            {
                return null;
            }

            excel.Visible = false;
            excel.DisplayAlerts = false;
            workbook = excel.Workbooks.Open(workbookPath);
            workbook.ExportAsFixedFormat(0, pdfPath);
            workbook.Close(false);
            workbook = null;
            excel.Quit();
            excel = null;

            return File.Exists(pdfPath) ? File.ReadAllBytes(pdfPath) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseExcelComObject(workbook, closeWorkbook: true);
            ReleaseExcelComObject(excel, closeWorkbook: false);
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void ReleaseExcelComObject(dynamic? comObject, bool closeWorkbook)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            if (closeWorkbook)
            {
                comObject.Close(false);
            }
            else
            {
                comObject.Quit();
            }
        }
        catch
        {
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
        }
    }

    private static void AddVmlShapeImageAsWorksheetImage(
        WorksheetPart worksheetPart,
        VmlDrawingPart vmlPart,
        XElement shape,
        XNamespace v,
        XNamespace o,
        string shapeId)
    {
        var relationshipId = shape.Element(v + "imagedata")?.Attribute(o + "relid")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId) ||
            vmlPart.GetPartById(relationshipId) is not ImagePart sourceImagePart)
        {
            return;
        }

        var drawingsPart = worksheetPart.DrawingsPart;
        if (drawingsPart?.WorksheetDrawing is null)
        {
            drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
            drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
            worksheetPart.Worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });
        }

        var imagePart = drawingsPart.AddImagePart(sourceImagePart.ContentType);
        using (var sourceStream = sourceImagePart.GetStream(FileMode.Open, FileAccess.Read))
        using (var targetStream = imagePart.GetStream(FileMode.Create, FileAccess.Write))
        {
            sourceStream.CopyTo(targetStream);
        }

        drawingsPart.WorksheetDrawing.Append(CreateOneCellImageAnchor(
            drawingsPart.WorksheetDrawing,
            drawingsPart.GetIdOfPart(imagePart),
            $"COA PDF {shapeId}",
            ResolvePdfHeaderColumnId(shapeId),
            ResolvePdfHeaderRowId(shapeId),
            ResolvePdfHeaderWidthPoints(shapeId),
            ResolvePdfHeaderHeightPoints(shapeId)));
        drawingsPart.WorksheetDrawing.Save();
    }

    private static string ResolvePdfHeaderColumnId(string shapeId) =>
        shapeId switch
        {
            "RH" => "4",
            "CH" => "2",
            _ => "0"
        };

    private static string ResolvePdfHeaderRowId(string shapeId) =>
        shapeId == "CH" ? "20" : "0";

    private static long ResolvePdfHeaderWidthPoints(string shapeId) =>
        shapeId switch
        {
            "RH" => 181,
            "CH" => 162,
            _ => 348
        };

    private static long ResolvePdfHeaderHeightPoints(string shapeId) =>
        shapeId switch
        {
            "RH" => 102,
            "CH" => 190,
            _ => 78
        };

    private static Xdr.OneCellAnchor CreateOneCellImageAnchor(
        Xdr.WorksheetDrawing worksheetDrawing,
        string relationshipId,
        string name,
        string columnId,
        string rowId,
        long widthPoints,
        long heightPoints)
    {
        const long emusPerPoint = 12700;
        var widthEmus = widthPoints * emusPerPoint;
        var heightEmus = heightPoints * emusPerPoint;
        var drawingId = worksheetDrawing
            .Descendants<Xdr.NonVisualDrawingProperties>()
            .Select(properties => properties.Id?.Value ?? 0U)
            .DefaultIfEmpty(0U)
            .Max() + 1;

        var picture = new Xdr.Picture(
            new Xdr.NonVisualPictureProperties(
                new Xdr.NonVisualDrawingProperties
                {
                    Id = drawingId,
                    Name = name
                },
                new Xdr.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true })),
            new Xdr.BlipFill(
                new A.Blip { Embed = relationshipId },
                new A.Stretch(new A.FillRectangle())),
            new Xdr.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = 0, Y = 0 },
                    new A.Extents { Cx = widthEmus, Cy = heightEmus }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));

        return new Xdr.OneCellAnchor(
            new Xdr.FromMarker(
                new Xdr.ColumnId(columnId),
                new Xdr.ColumnOffset("0"),
                new Xdr.RowId(rowId),
                new Xdr.RowOffset("0")),
            new Xdr.Extent { Cx = widthEmus, Cy = heightEmus },
            picture,
            new Xdr.ClientData());
    }

    private byte[] ConvertWithFallback(byte[] workbookContent, string workbookFileName)
    {
        if (!_options.UseBasicPdfFallback)
        {
            throw new InvalidOperationException(
                "LibreOffice PDF converter was not found. Set Scheduler:CoaExport:LibreOfficePath or enable Scheduler:CoaExport:UseBasicPdfFallback.");
        }

        return BasicSpreadsheetPdfExporter.Export(workbookContent, workbookFileName);
    }

    private static bool IsExecutableAvailable(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return paths.Any(path =>
        {
            var candidate = Path.Combine(path, OperatingSystem.IsWindows() ? $"{fileName}.exe" : fileName);
            return File.Exists(candidate);
        });
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var completedTask = await Task.WhenAny(exitTask, Task.Delay(timeout, cancellationToken));
        return completedTask == exitTask;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static class BasicSpreadsheetPdfExporter
    {
        private const int MaxLinesPerPage = 48;

        public static byte[] Export(byte[] workbookContent, string workbookFileName)
        {
            var lines = ExtractLines(workbookContent, workbookFileName);
            var pages = lines
                .Chunk(MaxLinesPerPage)
                .Select(chunk => chunk.ToArray())
                .ToArray();

            return BuildPdf(pages.Length == 0 ? [Array.Empty<string>()] : pages);
        }

        private static IReadOnlyList<string> ExtractLines(byte[] workbookContent, string workbookFileName)
        {
            using var workbook = new XLWorkbook(new MemoryStream(workbookContent));
            var lines = new List<string>
            {
                Path.GetFileNameWithoutExtension(workbookFileName)
            };

            foreach (var worksheet in workbook.Worksheets)
            {
                lines.Add(worksheet.Name);
                var range = worksheet.RangeUsed();
                if (range is null)
                {
                    continue;
                }

                foreach (var row in range.RowsUsed())
                {
                    var values = row.CellsUsed()
                        .Select(cell => FormatCellValue(cell))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToArray();

                    if (values.Length > 0)
                    {
                        lines.Add(string.Join("  ", values));
                    }
                }
            }

            return lines;
        }

        private static string FormatCellValue(IXLCell cell)
        {
            if (cell.Value.IsBlank)
            {
                return string.Empty;
            }

            if (cell.Value.IsNumber)
            {
                return cell.Value.GetNumber().ToString("0.####", CultureInfo.InvariantCulture);
            }

            if (cell.Value.IsDateTime)
            {
                return cell.Value.GetDateTime().ToString("yyyy/M/d", CultureInfo.InvariantCulture);
            }

            return cell.GetFormattedString();
        }

        private static byte[] BuildPdf(IReadOnlyList<IReadOnlyList<string>> pages)
        {
            var objectCount = 3 + pages.Count * 2;
            var objects = new string[objectCount + 1];
            var pageObjectIds = new List<int>();

            objects[1] = "<< /Type /Catalog /Pages 2 0 R >>";
            objects[3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>";

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var pageObjectId = 4 + pageIndex * 2;
                var contentObjectId = pageObjectId + 1;
                pageObjectIds.Add(pageObjectId);

                var content = BuildPageContent(pages[pageIndex]);
                objects[pageObjectId] =
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectId} 0 R >>";
                objects[contentObjectId] =
                    $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream";
            }

            objects[2] = $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>";

            using var stream = new MemoryStream();
            WriteAscii(stream, "%PDF-1.4\n");
            var offsets = new long[objectCount + 1];
            for (var objectId = 1; objectId <= objectCount; objectId++)
            {
                offsets[objectId] = stream.Position;
                WriteAscii(stream, $"{objectId} 0 obj\n{objects[objectId]}\nendobj\n");
            }

            var xrefOffset = stream.Position;
            WriteAscii(stream, $"xref\n0 {objectCount + 1}\n");
            WriteAscii(stream, "0000000000 65535 f \n");
            for (var objectId = 1; objectId <= objectCount; objectId++)
            {
                WriteAscii(stream, $"{offsets[objectId].ToString("0000000000", CultureInfo.InvariantCulture)} 00000 n \n");
            }

            WriteAscii(stream, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
            return stream.ToArray();
        }

        private static string BuildPageContent(IReadOnlyList<string> lines)
        {
            var builder = new StringBuilder();
            builder.AppendLine("BT");
            builder.AppendLine("/F1 9 Tf");
            builder.AppendLine("40 800 Td");

            foreach (var line in lines)
            {
                builder.Append('(')
                    .Append(EscapePdfText(ToPdfAscii(line)))
                    .AppendLine(") Tj");
                builder.AppendLine("0 -15 Td");
            }

            builder.AppendLine("ET");
            return builder.ToString();
        }

        private static string ToPdfAscii(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(character is >= ' ' and <= '~' ? character : '?');
            }

            return builder.ToString();
        }

        private static string EscapePdfText(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
