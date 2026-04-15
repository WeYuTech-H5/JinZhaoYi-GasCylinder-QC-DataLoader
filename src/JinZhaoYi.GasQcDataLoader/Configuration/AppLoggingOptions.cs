namespace JinZhaoYi.GasQcDataLoader.Configuration;

public sealed class AppLoggingOptions
{
    public const string SectionName = "AppLogging";

    public string ApplicationName { get; init; } = "JinZhaoYi.GasQcDataLoader";

    public string MinimumLevel { get; init; } = "Information";

    public FileLoggingOptions File { get; init; } = new();

    public SeqLoggingOptions Seq { get; init; } = new();
}

public sealed class FileLoggingOptions
{
    public bool Enabled { get; init; } = true;

    public int RetainDays { get; init; } = 14;

    public int FileSizeLimitMB { get; init; } = 10;
}

public sealed class SeqLoggingOptions
{
    public bool Enabled { get; init; }

    public string ServerUrl { get; init; } = "http://localhost:5341";

    public string BufferRelativePath { get; init; } = "seq-buffer";

    public int PeriodSeconds { get; init; } = 2;
}
