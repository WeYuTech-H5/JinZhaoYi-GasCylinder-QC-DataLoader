using System.Reflection;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Services.Infrastructure;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class DapperRepositoryStdRawForRfSqlTests
{
    [Theory]
    [InlineData("StdRawForRfSqlFormat")]
    [InlineData("StdRawForRfPagedSqlFormat")]
    [InlineData("StdRawForRfCountSqlFormat")]
    public void StdRawForRf_queries_deduplicate_by_lot_sample_and_prefer_mfg_sample_no(string fieldName)
    {
        var sql = GetSqlFormat(fieldName);

        sql.Should().Contain("OUTER APPLY");
        sql.Should().Contain("FROM dbo.{1} AS mfg");
        sql.Should().Contain("mfg.LotNo = rows.LotNo");
        sql.Should().Contain("mfgMatch.SampleNo AS MfgSampleNo");
        sql.Should().Contain("ISNULL(LotNo, '')");
        sql.Should().Contain("ISNULL(SampleName, '')");
        sql.Should().Contain("WHEN MfgSampleNo IS NOT NULL AND TRY_CONVERT(int, SampleNo) = MfgSampleNo THEN 0");
        sql.Should().Contain("AnlzTime DESC");
        sql.Should().NotContain("PARTITION BY ISNULL(LotNo, ''), SampleNo, ISNULL(SampleName, '')");
        sql.Should().NotContain("ISNULL(SourceFolderName, ''),");
        sql.Should().NotContain("ISNULL(DataFilename, ''),");
    }

    private static string GetSqlFormat(string fieldName)
    {
        var field = typeof(DapperRepository).GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Static);

        return field?.GetRawConstantValue() as string
            ?? throw new InvalidOperationException($"{fieldName} was not found.");
    }
}
