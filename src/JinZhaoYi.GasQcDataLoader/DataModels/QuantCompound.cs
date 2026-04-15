namespace JinZhaoYi.GasQcDataLoader.DataModels;

public sealed record QuantCompound(
    AnalyteDefinition Analyte,
    decimal RetentionTime,
    decimal Response,
    decimal? ConcentrationPpb);
