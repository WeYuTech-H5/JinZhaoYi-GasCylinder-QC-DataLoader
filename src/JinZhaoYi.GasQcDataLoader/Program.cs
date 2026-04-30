using JinZhaoYi.GasQcDataLoader.Configuration;
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
    builder.Services.AddSingleton<IImportWriteSetBuilder, ImportWriteSetBuilder>();
    builder.Services.AddSingleton<IQuery2WorkbookExporter, Query2WorkbookExporter>();
    builder.Services.AddSingleton<IPortPpbCsvExporter, PortPpbCsvExporter>();
    builder.Services.AddSingleton<IQcDownloadFileResolver, QcDownloadFileResolver>();
    builder.Services.AddSingleton<IImportOrchestrator, ImportOrchestrator>();
    builder.Services.AddSingleton<IJob, GasQcImportJob>();

    if (!schedulerOptions.DownloadApi.Enabled)
    {
        builder.Services.AddHostedService<Worker>();
    }

    var app = builder.Build();

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
