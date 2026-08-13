using System.Text;
using JinZhaoYi.GasQcDataLoader.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace JinZhaoYi.GasQcDataLoader.Logging;

internal static class SerilogConfigurator
{
    private const string LogDirectoryName = "LOG";
    private const string ApplicationLogDirectoryName = "Application";
    private const string MfgJsonLogDirectoryName = "MFGJSON";
    private const string SyncLogDirectoryName = "Sync";
    private const string DataReadLogDirectoryName = "DataRead";
    private const string LogFilePattern = "app-.log";
    private const string MfgJsonLogFilePattern = "scan-.log";
    private const string SyncCycleLogFilePattern = "cycle-.log";
    private const string SyncImportLogFilePattern = "import-.log";
    private const string SyncStateLogFilePattern = "state-.log";
    private const string DataReadScannerLogFilePattern = "scanner-.log";
    private const string DataReadQuantLogFilePattern = "quant-.log";
    private const string MfgJsonSourceContextPrefix = "JinZhaoYi.GasQcDataLoader.Services.";
    private static readonly string[] SyncCycleSourceContexts =
    [
        "JinZhaoYi.GasQcDataLoader.Services.Processing.Worker",
        "JinZhaoYi.GasQcDataLoader.Services.Service.GasQcImportJob"
    ];

    private static readonly string[] SyncImportSourceContexts =
    [
        "JinZhaoYi.GasQcDataLoader.Services.Service.ImportOrchestrator",
        "JinZhaoYi.GasQcDataLoader.Services.Service.ImportWriteSetBuilder"
    ];

    private static readonly string[] SyncStateSourceContexts =
    [
        "JinZhaoYi.GasQcDataLoader.Services.Service.ProcessedQuantFileStore"
    ];

    private static readonly string[] DataReadScannerSourceContexts =
    [
        "JinZhaoYi.GasQcDataLoader.Services.Service.GasFolderScanner"
    ];

    private static readonly string[] DataReadQuantSourceContexts =
    [
        "JinZhaoYi.GasQcDataLoader.Services.Service.QuantParser"
    ];

    public static Logger CreateBootstrapLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();
    }

    public static void Configure(LoggerConfiguration loggerConfiguration, AppLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(options);

        var logDirectory = ResolveLogDirectory(options.ApplicationName);
        SetupSelfLog(logDirectory);

        SetMinimumLevel(loggerConfiguration, options.MinimumLevel);

        loggerConfiguration
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", options.ApplicationName);

        if (options.File.Enabled)
        {
            var applicationLogDirectory = Path.Combine(logDirectory, ApplicationLogDirectoryName);
            var mfgJsonLogDirectory = Path.Combine(logDirectory, MfgJsonLogDirectoryName);
            var syncLogDirectory = Path.Combine(logDirectory, SyncLogDirectoryName);
            var dataReadLogDirectory = Path.Combine(logDirectory, DataReadLogDirectoryName);
            Directory.CreateDirectory(applicationLogDirectory);
            Directory.CreateDirectory(mfgJsonLogDirectory);
            Directory.CreateDirectory(syncLogDirectory);
            Directory.CreateDirectory(dataReadLogDirectory);

            // 程式、同步流程、讀檔解析與 MFG JSON 掃描分資料夾保存，正式區查問題時不用混在同一份 app log。
            loggerConfiguration.WriteTo.Logger(config => config
                .Filter.ByExcluding(logEvent =>
                    IsMfgJsonScanEvent(logEvent) ||
                    IsSyncTraceEvent(logEvent) ||
                    IsDataReadTraceEvent(logEvent))
                .WriteTo.File(
                    path: Path.Combine(applicationLogDirectory, LogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.File.RetainDays,
                    encoding: Encoding.UTF8,
                    fileSizeLimitBytes: options.File.FileSizeLimitMB * 1024L * 1024L,
                    rollOnFileSizeLimit: true,
                    shared: true));

            loggerConfiguration.WriteTo.Logger(config => config
                .Filter.ByIncludingOnly(IsSyncCycleEvent)
                .WriteTo.File(
                    path: Path.Combine(syncLogDirectory, SyncCycleLogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.File.RetainDays,
                    encoding: Encoding.UTF8,
                    fileSizeLimitBytes: options.File.FileSizeLimitMB * 1024L * 1024L,
                    rollOnFileSizeLimit: true,
                    shared: true));

            loggerConfiguration.WriteTo.Logger(config => config
                .Filter.ByIncludingOnly(IsSyncImportEvent)
                .WriteTo.File(
                    path: Path.Combine(syncLogDirectory, SyncImportLogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.File.RetainDays,
                    encoding: Encoding.UTF8,
                    fileSizeLimitBytes: options.File.FileSizeLimitMB * 1024L * 1024L,
                    rollOnFileSizeLimit: true,
                    shared: true));

            loggerConfiguration.WriteTo.Logger(config => config
                .Filter.ByIncludingOnly(IsSyncStateEvent)
                .WriteTo.File(
                    path: Path.Combine(syncLogDirectory, SyncStateLogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.File.RetainDays,
                    encoding: Encoding.UTF8,
                    fileSizeLimitBytes: options.File.FileSizeLimitMB * 1024L * 1024L,
                    rollOnFileSizeLimit: true,
                    shared: true));

            loggerConfiguration.WriteTo.Logger(config => config
                .Filter.ByIncludingOnly(IsDataReadScannerEvent)
                .WriteTo.File(
                    path: Path.Combine(dataReadLogDirectory, DataReadScannerLogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.File.RetainDays,
                    encoding: Encoding.UTF8,
                    fileSizeLimitBytes: options.File.FileSizeLimitMB * 1024L * 1024L,
                    rollOnFileSizeLimit: true,
                    shared: true));

            loggerConfiguration.WriteTo.Logger(config => config
                .Filter.ByIncludingOnly(IsDataReadQuantEvent)
                .WriteTo.File(
                    path: Path.Combine(dataReadLogDirectory, DataReadQuantLogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.File.RetainDays,
                    encoding: Encoding.UTF8,
                    fileSizeLimitBytes: options.File.FileSizeLimitMB * 1024L * 1024L,
                    rollOnFileSizeLimit: true,
                    shared: true));

            loggerConfiguration.WriteTo.Logger(config => config
                .Filter.ByIncludingOnly(IsMfgJsonScanEvent)
                .WriteTo.File(
                    path: Path.Combine(mfgJsonLogDirectory, MfgJsonLogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.File.RetainDays,
                    encoding: Encoding.UTF8,
                    fileSizeLimitBytes: options.File.FileSizeLimitMB * 1024L * 1024L,
                    rollOnFileSizeLimit: true,
                    shared: true));
        }

        if (options.Seq.Enabled)
        {
            loggerConfiguration.WriteTo.Seq(
                serverUrl: options.Seq.ServerUrl,
                bufferBaseFilename: Path.Combine(logDirectory, options.Seq.BufferRelativePath),
                period: TimeSpan.FromSeconds(options.Seq.PeriodSeconds));
        }
    }

    private static string ResolveLogDirectory(string applicationName)
    {
        try
        {
            var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            if (Directory.Exists(Path.Combine(projectRoot, "bin")))
            {
                var dev = Path.Combine(projectRoot, LogDirectoryName);
                Directory.CreateDirectory(dev);
                return dev;
            }

            var normal = Path.Combine(AppContext.BaseDirectory, LogDirectoryName);
            Directory.CreateDirectory(normal);
            return normal;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                applicationName,
                LogDirectoryName);

            Directory.CreateDirectory(fallback);
            SelfLog.WriteLine("日誌目錄改用備援路徑 {0}：{1}", fallback, ex.Message);
            return fallback;
        }
    }

    private static void SetupSelfLog(string logDirectory)
    {
        var selfLogPath = Path.Combine(logDirectory, "serilog-selflog.txt");

        SelfLog.Enable(msg =>
        {
            try
            {
                File.AppendAllText(selfLogPath, msg);
            }
            catch
            {
                // SelfLog 寫入失敗時不可再拋例外，避免 logging 造成主程式失敗。
            }
        });
    }

    private static void SetMinimumLevel(LoggerConfiguration config, string level)
    {
        var normalized = (level ?? string.Empty).Trim().ToLowerInvariant();

        config.MinimumLevel.Is(normalized switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        });
    }

    private static bool IsMfgJsonScanEvent(LogEvent logEvent)
    {
        if (!TryGetSourceContext(logEvent, out var sourceContext))
        {
            return false;
        }

        return sourceContext.StartsWith(MfgJsonSourceContextPrefix, StringComparison.Ordinal) &&
               sourceContext.Contains("MfgJson", StringComparison.Ordinal);
    }

    private static bool IsSyncTraceEvent(LogEvent logEvent) =>
        IsSyncCycleEvent(logEvent) ||
        IsSyncImportEvent(logEvent) ||
        IsSyncStateEvent(logEvent);

    private static bool IsDataReadTraceEvent(LogEvent logEvent) =>
        IsDataReadScannerEvent(logEvent) ||
        IsDataReadQuantEvent(logEvent);

    private static bool IsSyncCycleEvent(LogEvent logEvent) =>
        IsSourceContext(logEvent, SyncCycleSourceContexts);

    private static bool IsSyncImportEvent(LogEvent logEvent) =>
        IsSourceContext(logEvent, SyncImportSourceContexts);

    private static bool IsSyncStateEvent(LogEvent logEvent) =>
        IsSourceContext(logEvent, SyncStateSourceContexts);

    private static bool IsDataReadScannerEvent(LogEvent logEvent) =>
        IsSourceContext(logEvent, DataReadScannerSourceContexts);

    private static bool IsDataReadQuantEvent(LogEvent logEvent) =>
        IsSourceContext(logEvent, DataReadQuantSourceContexts);

    private static bool IsSourceContext(LogEvent logEvent, IReadOnlyCollection<string> sourceContexts)
    {
        if (!TryGetSourceContext(logEvent, out var sourceContext))
        {
            return false;
        }

        return sourceContexts.Any(expected =>
            sourceContext.Equals(expected, StringComparison.Ordinal) ||
            sourceContext.StartsWith($"{expected}.", StringComparison.Ordinal));
    }

    private static bool TryGetSourceContext(LogEvent logEvent, out string sourceContext)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContextValue))
        {
            sourceContext = sourceContextValue.ToString().Trim('"');
            return true;
        }

        sourceContext = string.Empty;
        return false;
    }
}
