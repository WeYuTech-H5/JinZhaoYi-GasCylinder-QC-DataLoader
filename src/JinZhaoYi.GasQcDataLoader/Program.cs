using System.Globalization;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Logging;
using JinZhaoYi.GasQcDataLoader.Services.Infrastructure;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Processing;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = SerilogConfigurator.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var schedulerOptions = builder.Configuration
        .GetSection(SchedulerOptions.SectionName)
        .Get<SchedulerOptions>() ?? new SchedulerOptions();

    builder.Services.AddWindowsService(options => options.ServiceName = schedulerOptions.ServiceName);
    builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection(SchedulerOptions.SectionName));
    builder.Services.Configure<AppLoggingOptions>(builder.Configuration.GetSection(AppLoggingOptions.SectionName));

    builder.Services.AddSerilog((services, loggerConfiguration) =>
    {
        var loggingOptions = services.GetRequiredService<IOptions<AppLoggingOptions>>().Value;
        SerilogConfigurator.Configure(loggerConfiguration, loggingOptions);
    });

    builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
    builder.Services.AddSingleton<IGasFolderScanner, GasFolderScanner>();
    builder.Services.AddSingleton<IQuantParser, QuantParser>();
    builder.Services.AddSingleton<IRawRowFactory, RawRowFactory>();
    builder.Services.AddSingleton<ICalculationService, CalculationService>();
    builder.Services.AddSingleton<IDapperRepository, DapperRepository>();
    builder.Services.AddSingleton<IProcessedQuantFileStore, ProcessedQuantFileStore>();
    builder.Services.AddSingleton<IImportWriteSetBuilder, ImportWriteSetBuilder>();
    builder.Services.AddSingleton<IQuery2SelectionExportBuilder, Query2SelectionExportBuilder>();
    builder.Services.AddSingleton<IQuery2WorkbookExporter, Query2WorkbookExporter>();
    builder.Services.AddSingleton<IPortPpbCsvExporter, PortPpbCsvExporter>();
    builder.Services.AddSingleton<IImportErrorReportExporter, ImportErrorReportExporter>();
    builder.Services.AddSingleton<IQcDownloadFileResolver, QcDownloadFileResolver>();
    builder.Services.AddSingleton<IImportOrchestrator, ImportOrchestrator>();
    builder.Services.AddSingleton<IJob, GasQcImportJob>();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("Content-Disposition");
        });
    });

    builder.Services.AddHostedService<Worker>();

    var app = builder.Build();

    app.UseCors();
    app.UseDefaultFiles();
    app.UseStaticFiles();

    if (schedulerOptions.DownloadApi.Enabled)
    {
        MapDownloadEndpoints(app);
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Gas QC DataLoader failed to start.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static void MapDownloadEndpoints(WebApplication app)
{
    var contentTypeProvider = new FileExtensionContentTypeProvider();

    app.MapGet("/api/export-options", async (
        string batchDate,
        IDapperRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!TryParseBatchDate(batchDate, out var parsedBatchDate))
        {
            return Results.BadRequest(new { message = "batchDate must use yyyyMMdd format." });
        }

        var options = await repository.GetExportOptionsAsync(parsedBatchDate, cancellationToken);
        return Results.Ok(options);
    });

    app.MapGet("/api/export-groups", async (
        string startDate,
        string endDate,
        IDapperRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!TryValidateDateRange(startDate, endDate, out var parsedStartDate, out var parsedEndDate, out var validationMessage))
        {
            return Results.BadRequest(new { message = validationMessage });
        }

        var options = await repository.GetExportOptionsAsync(parsedStartDate, parsedEndDate, cancellationToken);
        return Results.Ok(BuildExportGroupResponse(parsedStartDate, parsedEndDate, options));
    });

    app.MapGet("/api/rf-options", async (
        IDapperRepository repository,
        CancellationToken cancellationToken) =>
    {
        var options = await repository.GetRfOptionsAsync(cancellationToken);
        return Results.Ok(options);
    });

    app.MapGet("/api/port-ppb-options", async (
        string batchDate,
        IDapperRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!TryParseBatchDate(batchDate, out var parsedBatchDate))
        {
            return Results.BadRequest(new { message = "batchDate must use yyyyMMdd format." });
        }

        var options = await repository.GetPortPpbExportOptionsAsync(parsedBatchDate, cancellationToken);
        return Results.Ok(BuildPortPpbGroupResponse(parsedBatchDate, options));
    });

    app.MapPost("/api/exports/query2-excel", async (
        Query2ExcelExportRequest request,
        IDapperRepository repository,
        IQuery2SelectionExportBuilder exportBuilder,
        IQuery2WorkbookExporter exporter,
        CancellationToken cancellationToken) =>
    {
        if (!TryValidateQuery2ExportRequest(request, out var startDate, out var endDate, out var rfId, out var stdRawIds, out var portRawIds, out var validationMessage))
        {
            return Results.BadRequest(new { message = validationMessage });
        }

        var rf = await repository.GetRfByIdAsync(rfId, cancellationToken);
        if (rf is null)
        {
            return Results.NotFound(new { message = $"RF '{rfId}' not found." });
        }

        var stdRows = await repository.GetRawRowsForExportAsync(startDate, endDate, stdRawIds, cancellationToken);
        var portRows = await repository.GetRawRowsForExportAsync(startDate, endDate, portRawIds, cancellationToken);

        if (stdRows.Count != stdRawIds.Length || portRows.Count != portRawIds.Length)
        {
            return Results.NotFound(new { message = "One or more selected raw rows were not found in the requested date range." });
        }

        var rows = exportBuilder.BuildRows(rf, stdRows, portRows);
        if (rows.Count == 0)
        {
            return Results.NotFound(new { message = "No DB rows found for selected export data." });
        }

        var startDateText = startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var endDateText = endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var exportDateText = startDate.Date == endDate.Date ? startDateText : $"{startDateText}-{endDateText}";
        var content = await exporter.ExportAsync(exportDateText, rows, cancellationToken);
        return content is null
            ? Results.NotFound(new { message = "No Query2 Excel content was generated." })
            : Results.File(
                content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Cylinder_Qc[{exportDateText}].xlsx");
    });

    app.MapPost("/api/exports/port-ppb-csv", async (
        ExportRequest request,
        IDapperRepository repository,
        IPortPpbCsvExporter exporter,
        CancellationToken cancellationToken) =>
    {
        if (!TryValidateExportRequest(request, out var batchDate, out var selectedIds, out var validationMessage))
        {
            return Results.BadRequest(new { message = validationMessage });
        }

        var rows = await repository.GetPortPpbRowsForExportAsync(batchDate, selectedIds, cancellationToken);
        if (rows.Count == 0)
        {
            return Results.NotFound(new { message = "No PORT PPB rows found for selected export data." });
        }

        var batchDateText = batchDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Results.File(
            exporter.ExportToBytes(rows),
            "text/csv; charset=utf-8",
            $"TO14C_PPB[{batchDateText}].csv");
    });

    app.MapGet("/api/downloads/cylinder-qc/{batchDate}", (
        string batchDate,
        IQcDownloadFileResolver resolver) =>
    {
        var path = resolver.ResolveCylinderQcWorkbook(batchDate);
        return path is null
            ? Results.NotFound(new { message = $"Cylinder_Qc[{batchDate}].xlsx not found." })
            : DownloadFile(path, contentTypeProvider);
    });

    app.MapGet("/api/downloads/to14c-csv/{sampleName}", (
        string sampleName,
        IQcDownloadFileResolver resolver) =>
    {
        var path = resolver.ResolveCsvBySampleName(sampleName);
        return path is null
            ? Results.NotFound(new { message = $"CSV for sampleName '{sampleName}' not found." })
            : DownloadFile(path, contentTypeProvider);
    });
}

static IResult DownloadFile(string path, FileExtensionContentTypeProvider contentTypeProvider)
{
    var fileName = Path.GetFileName(path);
    var contentType = contentTypeProvider.TryGetContentType(path, out var resolvedContentType)
        ? resolvedContentType
        : "application/octet-stream";

    return Results.File(
        path,
        contentType,
        fileDownloadName: fileName,
        enableRangeProcessing: true);
}

static bool TryValidateExportRequest(
    ExportRequest request,
    out DateTime batchDate,
    out string[] selectedIds,
    out string message)
{
    selectedIds = request.SelectedIds
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (!TryParseBatchDate(request.BatchDate, out batchDate))
    {
        message = "batchDate must use yyyyMMdd format.";
        return false;
    }

    if (selectedIds.Length == 0)
    {
        message = "selectedIds must contain at least one export option id.";
        return false;
    }

    message = string.Empty;
    return true;
}

static bool TryParseBatchDate(string? value, out DateTime batchDate) =>
    DateTime.TryParseExact(
        value,
        "yyyyMMdd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out batchDate);

static bool TryValidateQuery2ExportRequest(
    Query2ExcelExportRequest request,
    out DateTime startDate,
    out DateTime endDate,
    out string rfId,
    out string[] stdRawIds,
    out string[] portRawIds,
    out string message)
{
    rfId = request.RfId?.Trim() ?? string.Empty;
    stdRawIds = NormalizeIds(request.StdRawIds);
    portRawIds = NormalizeIds(request.PortRawIds);

    if (!TryValidateDateRange(request.StartDate, request.EndDate, out startDate, out endDate, out message))
    {
        return false;
    }

    if (string.IsNullOrWhiteSpace(rfId))
    {
        message = "rfId is required.";
        return false;
    }

    if (stdRawIds.Length == 0)
    {
        message = "stdRawIds must contain at least one selected STD raw row.";
        return false;
    }

    if (portRawIds.Length == 0)
    {
        message = "portRawIds must contain at least one selected PORT raw row.";
        return false;
    }

    message = string.Empty;
    return true;
}

static bool TryValidateDateRange(
    string? startDateValue,
    string? endDateValue,
    out DateTime startDate,
    out DateTime endDate,
    out string message)
{
    if (!TryParseBatchDate(startDateValue, out startDate))
    {
        endDate = default;
        message = "startDate must use yyyyMMdd format.";
        return false;
    }

    if (!TryParseBatchDate(endDateValue, out endDate))
    {
        message = "endDate must use yyyyMMdd format.";
        return false;
    }

    if (startDate.Date > endDate.Date)
    {
        message = "startDate must be less than or equal to endDate.";
        return false;
    }

    message = string.Empty;
    return true;
}

static string[] NormalizeIds(IEnumerable<string> ids) =>
    ids.Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

static ExportGroupResponse BuildExportGroupResponse(
    DateTime startDate,
    DateTime endDate,
    IReadOnlyCollection<ExportOption> options)
{
    var groups = options
        .GroupBy(option => new
        {
            SourceKind = option.SourceKind ?? string.Empty,
            Port = option.Port,
            LotNo = option.LotNo,
            SampleName = option.SampleName ?? string.Empty
        })
        .Select(group =>
        {
            var rows = group
                .OrderBy(option => option.AnlzTime)
                .ThenBy(option => option.SampleNo)
                .ThenBy(option => option.SourceFolderName, StringComparer.OrdinalIgnoreCase)
                .Select(option => new ExportRawOption(
                    option.Id,
                    option.SourceKind,
                    option.SourceFolderName,
                    option.Port,
                    option.LotNo,
                    option.SampleName,
                    option.SampleNo,
                    option.AnlzTime))
                .ToArray();

            var first = group.First();
            var groupId = string.Join("|", first.SourceKind, first.Port, first.LotNo, first.SampleName);
            return new ExportGroup(groupId, first.SourceKind, first.Port, first.LotNo, first.SampleName, rows);
        })
        .OrderBy(group => group.SourceKind, StringComparer.OrdinalIgnoreCase)
        .ThenBy(group => group.Port, StringComparer.OrdinalIgnoreCase)
        .ThenBy(group => group.LotNo, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return new ExportGroupResponse(
        startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        groups.Where(group => string.Equals(group.SourceKind, "Std", StringComparison.OrdinalIgnoreCase)).ToArray(),
        groups.Where(group => !string.Equals(group.SourceKind, "Std", StringComparison.OrdinalIgnoreCase)).ToArray());
}

static ExportGroupResponse BuildPortPpbGroupResponse(
    DateTime batchDate,
    IReadOnlyCollection<ExportOption> options)
{
    var groups = options
        .GroupBy(option => new
        {
            Port = option.Port,
            LotNo = option.LotNo,
            SampleName = option.SampleName ?? string.Empty
        })
        .Select(group =>
        {
            var rows = group
                .OrderBy(option => option.AnlzTime)
                .ThenBy(option => option.SampleNo)
                .ThenBy(option => option.SourceFolderName, StringComparer.OrdinalIgnoreCase)
                .Select(option => new ExportRawOption(
                    option.Id,
                    option.SourceKind,
                    option.SourceFolderName,
                    option.Port,
                    option.LotNo,
                    option.SampleName,
                    option.SampleNo,
                    option.AnlzTime))
                .ToArray();

            var first = group.First();
            var groupId = string.Join("|", "Ppb", first.Port, first.LotNo, first.SampleName);
            return new ExportGroup(groupId, "Ppb", first.Port, first.LotNo, first.SampleName, rows);
        })
        .OrderBy(group => group.Port, StringComparer.OrdinalIgnoreCase)
        .ThenBy(group => group.LotNo, StringComparer.OrdinalIgnoreCase)
        .ThenBy(group => group.SampleName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var batchDateText = batchDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    return new ExportGroupResponse(batchDateText, batchDateText, [], groups);
}
