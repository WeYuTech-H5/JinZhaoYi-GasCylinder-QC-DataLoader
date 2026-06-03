using System.Diagnostics;
using System.Globalization;
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

            foreach (var worksheetPart in workbookPart.WorksheetParts)
            {
                PromoteHeaderFooterImagesForPdf(worksheetPart);
            }

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static void PromoteHeaderFooterImagesForPdf(WorksheetPart worksheetPart)
    {
        var vmlPart = worksheetPart.VmlDrawingParts.FirstOrDefault();
        if (vmlPart is null || !worksheetPart.Worksheet.Elements<LegacyDrawingHeaderFooter>().Any())
        {
            return;
        }

        var headerImages = ResolveHeaderFooterImages(vmlPart);
        if (headerImages.Count == 0)
        {
            return;
        }

        foreach (var image in headerImages)
        {
            AddWorksheetImage(worksheetPart, image);
        }

        EnsureWorksheetDimensionStartsAtA1(worksheetPart);
        FitWorksheetToSinglePdfPage(worksheetPart);
        worksheetPart.Worksheet.Elements<HeaderFooter>().ToList().ForEach(element => element.Remove());
        worksheetPart.Worksheet.Elements<LegacyDrawingHeaderFooter>().ToList().ForEach(element => element.Remove());
        worksheetPart.DeletePart(vmlPart);
        worksheetPart.Worksheet.Save();
    }

    private static IReadOnlyList<HeaderFooterImage> ResolveHeaderFooterImages(VmlDrawingPart vmlPart)
    {
        XNamespace v = "urn:schemas-microsoft-com:vml";
        XNamespace o = "urn:schemas-microsoft-com:office:office";
        using var vmlStream = vmlPart.GetStream(FileMode.Open, FileAccess.Read);
        var document = XDocument.Load(vmlStream);

        var images = new List<HeaderFooterImage>();
        foreach (var shape in document.Descendants(v + "shape"))
        {
            var shapeId = shape.Attribute("id")?.Value;
            HeaderFooterImagePlacement? placement = shapeId switch
            {
                "LH" => HeaderFooterImagePlacement.Left,
                "RH" => HeaderFooterImagePlacement.Right,
                _ => null
            };

            if (placement is null)
            {
                continue;
            }

            var relationshipId = shape.Element(v + "imagedata")?.Attribute(o + "relid")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId) ||
                vmlPart.GetPartById(relationshipId) is not ImagePart imagePart)
            {
                continue;
            }

            var size = ParseVmlImageSize(shape.Attribute("style")?.Value);
            images.Add(new HeaderFooterImage(placement.Value, imagePart, size.WidthEmus, size.HeightEmus));
        }

        return images;
    }

    private static (long WidthEmus, long HeightEmus) ParseVmlImageSize(string? style)
    {
        const long emusPerPoint = 12700;
        var widthPoints = TryParseVmlPointValue(style, "width") ?? 0m;
        var heightPoints = TryParseVmlPointValue(style, "height") ?? 0m;
        return ((long)(widthPoints * emusPerPoint), (long)(heightPoints * emusPerPoint));
    }

    private static decimal? TryParseVmlPointValue(string? style, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            style,
            $@"(?:^|;)\s*{propertyName}\s*:\s*(?<value>[0-9.]+)pt",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success &&
            decimal.TryParse(match.Groups["value"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
    }

    private static void AddWorksheetImage(WorksheetPart worksheetPart, HeaderFooterImage image)
    {
        var drawingsPart = worksheetPart.DrawingsPart;
        if (drawingsPart?.WorksheetDrawing is null)
        {
            drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
            drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
            worksheetPart.Worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });
        }

        var targetImagePart = drawingsPart.AddImagePart(image.ImagePart.ContentType);
        using (var sourceStream = image.ImagePart.GetStream(FileMode.Open, FileAccess.Read))
        using (var targetStream = targetImagePart.GetStream(FileMode.Create, FileAccess.Write))
        {
            sourceStream.CopyTo(targetStream);
        }

        var relationshipId = drawingsPart.GetIdOfPart(targetImagePart);
        var anchor = CreateHeaderImageAnchor(drawingsPart.WorksheetDrawing, image, relationshipId);
        drawingsPart.WorksheetDrawing.Append(anchor);
        drawingsPart.WorksheetDrawing.Save();
    }

    private static Xdr.OneCellAnchor CreateHeaderImageAnchor(
        Xdr.WorksheetDrawing worksheetDrawing,
        HeaderFooterImage image,
        string relationshipId)
    {
        var drawingId = ResolveNextDrawingId(worksheetDrawing);
        var columnId = image.Placement == HeaderFooterImagePlacement.Left ? "0" : "7";
        var widthEmus = image.WidthEmus > 0 ? image.WidthEmus : 348L * 12700L;
        var heightEmus = image.HeightEmus > 0 ? image.HeightEmus : 102L * 12700L;

        var picture = new Xdr.Picture(
            new Xdr.NonVisualPictureProperties(
                new Xdr.NonVisualDrawingProperties
                {
                    Id = drawingId,
                    Name = image.Placement == HeaderFooterImagePlacement.Left ? "COA Header Left" : "COA Header Right"
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
                new Xdr.RowId("0"),
                new Xdr.RowOffset("0")),
            new Xdr.Extent { Cx = widthEmus, Cy = heightEmus },
            picture,
            new Xdr.ClientData());
    }

    private static uint ResolveNextDrawingId(Xdr.WorksheetDrawing worksheetDrawing)
    {
        var maxId = worksheetDrawing
            .Descendants<Xdr.NonVisualDrawingProperties>()
            .Select(properties => properties.Id?.Value ?? 0U)
            .DefaultIfEmpty(0U)
            .Max();

        return maxId + 1;
    }

    private static void EnsureWorksheetDimensionStartsAtA1(WorksheetPart worksheetPart)
    {
        var dimension = worksheetPart.Worksheet.GetFirstChild<SheetDimension>();
        if (dimension?.Reference?.Value is null)
        {
            return;
        }

        var reference = dimension.Reference.Value;
        var separatorIndex = reference.IndexOf(':', StringComparison.Ordinal);
        dimension.Reference = separatorIndex < 0
            ? "A1"
            : $"A1{reference[separatorIndex..]}";
    }

    private static void FitWorksheetToSinglePdfPage(WorksheetPart worksheetPart)
    {
        var sheetProperties = worksheetPart.Worksheet.GetFirstChild<SheetProperties>()
            ?? worksheetPart.Worksheet.PrependChild(new SheetProperties());
        sheetProperties.PageSetupProperties ??= new PageSetupProperties();
        sheetProperties.PageSetupProperties.FitToPage = true;

        var pageSetup = worksheetPart.Worksheet.GetFirstChild<PageSetup>();
        if (pageSetup is null)
        {
            pageSetup = new PageSetup();
            worksheetPart.Worksheet.Append(pageSetup);
        }

        pageSetup.FitToWidth = 1;
        pageSetup.FitToHeight = 1;
        pageSetup.Scale = null;
    }

    private sealed record HeaderFooterImage(
        HeaderFooterImagePlacement Placement,
        ImagePart ImagePart,
        long WidthEmus,
        long HeightEmus);

    private enum HeaderFooterImagePlacement
    {
        Left,
        Right
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
