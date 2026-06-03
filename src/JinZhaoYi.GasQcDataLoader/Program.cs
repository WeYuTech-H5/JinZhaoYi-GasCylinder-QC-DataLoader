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
    builder.Services.AddSingleton<IQuery2PreviewService, Query2PreviewService>();
    builder.Services.AddSingleton<IQuery2WorkbookExporter, Query2WorkbookExporter>();
    builder.Services.AddSingleton<IPortPpbCsvExporter, PortPpbCsvExporter>();
    builder.Services.AddSingleton<ICoaWorkbookExporter, CoaWorkbookExporter>();
    builder.Services.AddSingleton<ISpreadsheetPdfConverter, SpreadsheetPdfConverter>();
    builder.Services.AddSingleton<ICoaPackageExporter, CoaPackageExporter>();
    builder.Services.AddSingleton<IMfgJsonParser, MfgJsonParser>();
    builder.Services.AddSingleton<IMfgJsonImportStateStore, MfgJsonImportStateStore>();
    builder.Services.AddSingleton<IMfgJsonImportService, MfgJsonImportService>();
    builder.Services.AddSingleton<IImportErrorReportExporter, ImportErrorReportExporter>();
    builder.Services.AddSingleton<IQcDownloadFileResolver, QcDownloadFileResolver>();
    builder.Services.AddSingleton<IImportOrchestrator, ImportOrchestrator>();
    builder.Services.AddSingleton<IJob, GasQcImportJob>();
    builder.Services.AddHttpClient<IRfExtractorImportService, RfExtractorImportService>();

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
    builder.Services.AddHostedService<MfgJsonImportWorker>();

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

    app.MapGet("/api/std-rf-source-options", async (
        string? search,
        int? limit,
        int? page,
        int? pageSize,
        IDapperRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (page.HasValue || pageSize.HasValue)
        {
            if (!TryValidatePagination(page, pageSize, out var normalizedPage, out var normalizedPageSize, out var validationMessage))
            {
                return Results.BadRequest(new { message = validationMessage });
            }

            var pagedOptions = await repository.GetStdRawOptionsForRfAsync(search, normalizedPage, normalizedPageSize, cancellationToken);
            return Results.Ok(pagedOptions);
        }

        var options = await repository.GetStdRawOptionsForRfAsync(search, limit ?? 200, cancellationToken);
        return Results.Ok(options);
    });

    app.MapPost("/api/rf/import-from-std", async (
        RfImportFromStdRequest request,
        IRfExtractorImportService importer,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.StdRawId))
        {
            return Results.BadRequest(new { message = "stdRawId is required." });
        }

        try
        {
            var result = await importer.ImportFromStdAsync(request.StdRawId.Trim(), cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    });

    app.MapGet("/api/port-ppb-options", async (
        string batchDate,
        int? page,
        int? pageSize,
        IDapperRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!TryParseBatchDate(batchDate, out var parsedBatchDate))
        {
            return Results.BadRequest(new { message = "batchDate must use yyyyMMdd format." });
        }

        if (page.HasValue || pageSize.HasValue)
        {
            if (!TryValidatePagination(page, pageSize, out var normalizedPage, out var normalizedPageSize, out var validationMessage))
            {
                return Results.BadRequest(new { message = validationMessage });
            }

            var pagedOptions = await repository.GetPortPpbExportOptionsAsync(parsedBatchDate, normalizedPage, normalizedPageSize, cancellationToken);
            return Results.Ok(BuildPagedPortPpbGroupResponse(parsedBatchDate, pagedOptions));
        }

        var options = await repository.GetPortPpbExportOptionsAsync(parsedBatchDate, cancellationToken);
        return Results.Ok(BuildPortPpbGroupResponse(parsedBatchDate, options));
    });

    app.MapGet("/api/excel-ppb-options", async (
        string batchDate,
        int? page,
        int? pageSize,
        IDapperRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!TryParseBatchDate(batchDate, out var parsedBatchDate))
        {
            return Results.BadRequest(new { message = "batchDate must use yyyyMMdd format." });
        }

        if (!TryValidatePagination(page, pageSize, out var normalizedPage, out var normalizedPageSize, out var validationMessage))
        {
            return Results.BadRequest(new { message = validationMessage });
        }

        var pagedOptions = await repository.GetExcelPpbExportOptionsAsync(parsedBatchDate, normalizedPage, normalizedPageSize, cancellationToken);
        return Results.Ok(BuildPagedExcelPpbGroupResponse(parsedBatchDate, pagedOptions));
    });

    app.MapPost("/api/exports/query2-excel/preview", async (
        Query2ExcelPreviewRequest request,
        IDapperRepository repository,
        IQuery2SelectionExportBuilder exportBuilder,
        IQuery2PreviewService previewService,
        CancellationToken cancellationToken) =>
    {
        var validationRequest = new Query2ExcelExportRequest
        {
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            RfId = request.RfId,
            StdRawIds = request.StdRawIds,
            PortRawIds = request.PortRawIds
        };
        if (!TryValidateQuery2ExportRequest(validationRequest, out var startDate, out var endDate, out var rfId, out var stdRawIds, out var portRawIds, out var validationMessage))
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

        return Results.Ok(previewService.CreatePreview(startDate, endDate, rfId, stdRawIds, portRawIds, rows));
    });

    app.MapPost("/api/exports/query2-excel/recalculate", (
        Query2PreviewRecalculateRequest request,
        IQuery2PreviewService previewService) =>
    {
        if (request.Preview is null)
        {
            return Results.BadRequest(new { message = "preview is required." });
        }

        try
        {
            return Results.Ok(previewService.Recalculate(request.Preview));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    });

    app.MapPost("/api/exports/query2-excel/from-preview", async (
        Query2PreviewExportRequest request,
        IQuery2PreviewService previewService,
        IQuery2WorkbookExporter exporter,
        IDapperRepository repository,
        IOptions<SchedulerOptions> options,
        CancellationToken cancellationToken) =>
    {
        if (request.Preview is null)
        {
            return Results.BadRequest(new { message = "preview is required." });
        }

        if (!TryValidatePreviewExportRequest(request.Preview, out var startDate, out var endDate, out var rfId, out var stdRawIds, out var portRawIds, out var validationMessage))
        {
            return Results.BadRequest(new { message = validationMessage });
        }

        Query2PreviewState finalPreview;
        IReadOnlyList<Query2ExportRow> rows;
        try
        {
            finalPreview = previewService.Recalculate(request.Preview);
            rows = previewService.ToExportRows(finalPreview);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        if (rows.Count == 0)
        {
            return Results.NotFound(new { message = "No Query2 preview rows were provided." });
        }

        var startDateText = startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var endDateText = endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var exportDateText = startDate.Date == endDate.Date ? startDateText : $"{startDateText}-{endDateText}";
        var content = await exporter.ExportAsync(exportDateText, rows, cancellationToken);
        if (content is null)
        {
            return Results.NotFound(new { message = "No Query2 Excel content was generated." });
        }

        var exportedAt = DateTime.Now;
        var exportUser = options.Value.CreateUser;
        var historyRequest = new ExcelPpbHistorySaveRequest(
            startDate,
            endDate,
            rfId,
            stdRawIds,
            portRawIds,
            rows.Where(row => row.RowType == Query2ExportRowType.Ppb).Select(row => row.Row).ToArray(),
            exportedAt,
            exportUser);

        await repository.UpsertExcelPpbHistoryAsync(historyRequest, cancellationToken);

        var excelExportKey = DapperRepository.ComputeExcelExportKey(historyRequest);
        var exportSessionId = Guid.NewGuid();
        var editLogs = previewService.BuildEditLogs(
            finalPreview,
            excelExportKey,
            exportSessionId,
            exportedAt,
            exportUser);
        await repository.InsertQuery2PreviewEditLogsAsync(editLogs, cancellationToken);

        return Results.File(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Cylinder_Qc[{exportDateText}][{exportSessionId:D}].xlsx");
    });

    app.MapPost("/api/exports/query2-excel", async (
        Query2ExcelExportRequest request,
        IDapperRepository repository,
        IQuery2SelectionExportBuilder exportBuilder,
        IQuery2WorkbookExporter exporter,
        IOptions<SchedulerOptions> options,
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
        if (content is null)
        {
            return Results.NotFound(new { message = "No Query2 Excel content was generated." });
        }

        // 只有成功產生 Excel 的 PPB 才能進入 CSV 候選清單，避免使用者下載到沒有對應快照的資料。
        await repository.UpsertExcelPpbHistoryAsync(
            new ExcelPpbHistorySaveRequest(
                startDate,
                endDate,
                rfId,
                stdRawIds,
                portRawIds,
                rows.Where(row => row.RowType == Query2ExportRowType.Ppb).Select(row => row.Row).ToArray(),
                DateTime.Now,
                options.Value.CreateUser),
            cancellationToken);

        return Results.File(
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
        var download = exporter.ExportForDownload(rows, batchDateText);
        return Results.File(
            download.Content,
            download.ContentType,
            download.FileName);
    });

    app.MapPost("/api/exports/excel-ppb-csv", async (
        ExportRequest request,
        IDapperRepository repository,
        IPortPpbCsvExporter exporter,
        CancellationToken cancellationToken) =>
    {
        if (!TryValidateExportRequest(request, out var batchDate, out var selectedIds, out var validationMessage))
        {
            return Results.BadRequest(new { message = validationMessage });
        }

        var rows = await repository.GetExcelPpbRowsForCsvAsync(batchDate, selectedIds, cancellationToken);
        if (rows.Count == 0)
        {
            return Results.NotFound(new { message = "No Excel PPB history rows found for selected export data." });
        }

        var batchDateText = batchDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var download = exporter.ExportForDownload(rows, batchDateText);
        return Results.File(
            download.Content,
            download.ContentType,
            download.FileName);
    });

    app.MapPost("/api/exports/excel-ppb-coa-large", async (
        CoaLargeExportRequest request,
        IDapperRepository repository,
        ICoaPackageExporter exporter,
        CancellationToken cancellationToken) =>
    {
        if (!TryValidateCoaLargeExportRequest(request, out var batchDate, out var selectedIds, out var templateType, out var validationMessage))
        {
            return Results.BadRequest(new { message = validationMessage });
        }

        var rows = await repository.GetExcelPpbRowsForCsvAsync(batchDate, selectedIds, cancellationToken);
        if (rows.Count == 0)
        {
            return Results.NotFound(new { message = "No Excel PPB history rows found for selected COA export data." });
        }

        var batchDateText = batchDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        try
        {
            var download = await exporter.ExportLargePackageForDownloadAsync(rows, batchDateText, templateType, cancellationToken);
            return Results.File(
                download.Content,
                download.ContentType,
                download.FileName);
        }
        catch (FileNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    });

    app.MapPost("/api/exports/excel-ppb-coa-small", async (
        CoaSmallExportRequest request,
        IDapperRepository repository,
        ICoaPackageExporter exporter,
        IOptions<SchedulerOptions> options,
        CancellationToken cancellationToken) =>
    {
        if (!TryValidateCoaSmallExportRequest(request, options.Value.CoaExport.DefaultSmallCardsPerPage, out var batchDate, out var selectedIds, out var cardsPerPage, out var validationMessage))
        {
            return Results.BadRequest(new { message = validationMessage });
        }

        var rows = await repository.GetExcelPpbRowsForCsvAsync(batchDate, selectedIds, cancellationToken);
        if (rows.Count == 0)
        {
            return Results.NotFound(new { message = "No Excel PPB history rows found for selected COA export data." });
        }

        var batchDateText = batchDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        try
        {
            var download = await exporter.ExportSmallPackageForDownloadAsync(rows, batchDateText, cardsPerPage, cancellationToken);
            return Results.File(
                download.Content,
                download.ContentType,
                download.FileName);
        }
        catch (FileNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
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
    out string message) =>
    TryValidateExportRequestParts(request.BatchDate, request.SelectedIds, out batchDate, out selectedIds, out message);

static bool TryValidateExportRequestParts(
    string? batchDateText,
    IReadOnlyList<string> requestSelectedIds,
    out DateTime batchDate,
    out string[] selectedIds,
    out string message)
{
    selectedIds = requestSelectedIds
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (!TryParseBatchDate(batchDateText, out batchDate))
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

static bool TryValidateCoaLargeExportRequest(
    CoaLargeExportRequest request,
    out DateTime batchDate,
    out string[] selectedIds,
    out CoaLargeTemplateType templateType,
    out string message)
{
    templateType = CoaLargeTemplateType.Standard;
    if (!TryValidateExportRequestParts(request.BatchDate, request.SelectedIds, out batchDate, out selectedIds, out message))
    {
        return false;
    }

    var templateTypeText = string.IsNullOrWhiteSpace(request.TemplateType)
        ? "standard"
        : request.TemplateType.Trim();

    if (string.Equals(templateTypeText, "standard", StringComparison.OrdinalIgnoreCase))
    {
        templateType = CoaLargeTemplateType.Standard;
        return true;
    }

    if (string.Equals(templateTypeText, "yadong", StringComparison.OrdinalIgnoreCase))
    {
        templateType = CoaLargeTemplateType.Yadong;
        return true;
    }

    message = "templateType must be 'standard' or 'yadong'.";
    return false;
}

static bool TryValidateCoaSmallExportRequest(
    CoaSmallExportRequest request,
    int defaultCardsPerPage,
    out DateTime batchDate,
    out string[] selectedIds,
    out int cardsPerPage,
    out string message)
{
    cardsPerPage = request.CardsPerPage ?? defaultCardsPerPage;
    if (!TryValidateExportRequestParts(request.BatchDate, request.SelectedIds, out batchDate, out selectedIds, out message))
    {
        return false;
    }

    if (cardsPerPage < 1)
    {
        message = "cardsPerPage must be greater than or equal to 1.";
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

static bool TryValidatePreviewExportRequest(
    Query2PreviewState preview,
    out DateTime startDate,
    out DateTime endDate,
    out string rfId,
    out string[] stdRawIds,
    out string[] portRawIds,
    out string message)
{
    rfId = preview.RfId?.Trim() ?? string.Empty;
    stdRawIds = NormalizeIds(preview.StdRawIds);
    portRawIds = NormalizeIds(preview.PortRawIds);

    if (!TryValidateDateRange(preview.StartDate, preview.EndDate, out startDate, out endDate, out message))
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

    if (preview.Rows.Count == 0)
    {
        message = "preview rows are required.";
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

static bool TryValidatePagination(
    int? pageValue,
    int? pageSizeValue,
    out int page,
    out int pageSize,
    out string message)
{
    page = pageValue ?? 1;
    pageSize = pageSizeValue ?? 50;

    if (page < 1)
    {
        message = "page must be greater than or equal to 1.";
        return false;
    }

    if (pageSize is < 1 or > 500)
    {
        message = "pageSize must be between 1 and 500.";
        return false;
    }

    message = string.Empty;
    return true;
}

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
    var groups = BuildPortPpbGroups(options);

    var batchDateText = batchDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    return new ExportGroupResponse(batchDateText, batchDateText, [], groups);
}

static PagedExportGroupResponse BuildPagedPortPpbGroupResponse(
    DateTime batchDate,
    PagedResponse<ExportOption> options)
{
    var groups = BuildPortPpbGroups(options.Items);

    var batchDateText = batchDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    return new PagedExportGroupResponse(
        batchDateText,
        batchDateText,
        [],
        groups,
        options.Page,
        options.PageSize,
        options.TotalCount);
}

static PagedExportGroupResponse BuildPagedExcelPpbGroupResponse(
    DateTime batchDate,
    PagedResponse<ExportOption> options)
{
    var groups = BuildExcelPpbGroups(options.Items);

    var batchDateText = batchDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    return new PagedExportGroupResponse(
        batchDateText,
        batchDateText,
        [],
        groups,
        options.Page,
        options.PageSize,
        options.TotalCount);
}

static ExportGroup[] BuildPortPpbGroups(IReadOnlyCollection<ExportOption> options) =>
    options
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

static ExportGroup[] BuildExcelPpbGroups(IReadOnlyCollection<ExportOption> options) =>
    options
        .GroupBy(option => new
        {
            ExportKey = option.GroupKey ?? string.Empty,
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
            var groupId = string.Join("|", "ExcelPpb", group.Key.ExportKey, first.Port, first.LotNo, first.SampleName);
            return new ExportGroup(groupId, "ExcelPpb", first.Port, first.LotNo, first.SampleName, rows);
        })
        .OrderBy(group => group.Port, StringComparer.OrdinalIgnoreCase)
        .ThenBy(group => group.LotNo, StringComparer.OrdinalIgnoreCase)
        .ThenBy(group => group.SampleName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

// 1
