using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.Logging;
using JinZhaoYi.GasQcDataLoader.Services.Infrastructure;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using JinZhaoYi.GasQcDataLoader.Services.Processing;
using JinZhaoYi.GasQcDataLoader.Services.Service;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = SerilogConfigurator.CreateBootstrapLogger();

try
{
    // 建立 .NET Generic Host。此專案可部署成 Windows Service，也保留 RunOnce 測試模式。
    var builder = Host.CreateApplicationBuilder(args);
    var schedulerOptions = builder.Configuration
        .GetSection(SchedulerOptions.SectionName)
        .Get<SchedulerOptions>() ?? new SchedulerOptions();

    builder.Services.AddWindowsService(options => options.ServiceName = schedulerOptions.ServiceName);

    // 將 appsettings.json / user-secrets / 環境變數中的設定綁定成強型別 options。
    builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection(SchedulerOptions.SectionName));
    builder.Services.Configure<AppLoggingOptions>(builder.Configuration.GetSection(AppLoggingOptions.SectionName));

    builder.Services.AddSerilog((services, loggerConfiguration) =>
    {
        var loggingOptions = services.GetRequiredService<IOptions<AppLoggingOptions>>().Value;
        SerilogConfigurator.Configure(loggerConfiguration, loggingOptions);
    });

    // 註冊主要模組。Worker 只負責排程，實際解析、計算、DB 存取由各 service 處理。
    builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
    builder.Services.AddSingleton<IGasFolderScanner, GasFolderScanner>();
    builder.Services.AddSingleton<IQuantParser, QuantParser>();
    builder.Services.AddSingleton<IRawRowFactory, RawRowFactory>();
    builder.Services.AddSingleton<ICalculationService, CalculationService>();
    builder.Services.AddSingleton<IDapperRepository, DapperRepository>();
    builder.Services.AddSingleton<IImportOrchestrator, ImportOrchestrator>();
    builder.Services.AddSingleton<IJob, GasQcImportJob>();
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();

    // 啟動 Host 後，BackgroundService 會進入 Worker.ExecuteAsync。
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Gas QC DataLoader 發生未預期錯誤並已停止。");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
