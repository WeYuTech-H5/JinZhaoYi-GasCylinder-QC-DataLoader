using System.Reflection;
using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.Services.Infrastructure;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class DapperRepositoryMfgJsonSqlTests
{
    [Fact]
    public void MfgJson_update_preserves_existing_nullable_values_when_incoming_value_is_null()
    {
        var sql = GetMfgJsonUpdateSqlFormat();
        var expectedAssignments = new[]
        {
            "[SamplName] = COALESCE(@SampleName, [SamplName])",
            "[ProdDate] = COALESCE(@ProdDate, [ProdDate])",
            "[ProdType] = COALESCE(@ProdType, [ProdType])",
            "[Prod_Operator] = COALESCE(@ProdOperator, [Prod_Operator])",
            "[Prod_IniPrs] = COALESCE(@ProdIniPrs, [Prod_IniPrs])",
            "[Prod_LeakTest1] = COALESCE(@ProdLeakTest1, [Prod_LeakTest1])",
            "[Prod_vacumPrs] = COALESCE(@ProdVacuumPrs, [Prod_vacumPrs])",
            "[Prod_Can1_FillingPrs] = COALESCE(@ProdCan1FillingPrs, [Prod_Can1_FillingPrs])",
            "[Prod_Can2_FillingPrs] = COALESCE(@ProdCan2FillingPrs, [Prod_Can2_FillingPrs])",
            "[Prod_Bomb2_FillingPrs] = COALESCE(@ProdBomb2FillingPrs, [Prod_Bomb2_FillingPrs])",
            "[Prod_Bomb1_FillingPrs] = COALESCE(@ProdBomb1FillingPrs, [Prod_Bomb1_FillingPrs])",
            "[Prod_Bomb3_FillingPrs] = COALESCE(@ProdBomb3FillingPrs, [Prod_Bomb3_FillingPrs])",
            "[Prod_LeakTest2] = COALESCE(@ProdLeakTest2, [Prod_LeakTest2])",
            "[Prod_Can1_LotNo] = COALESCE(@ProdCan1LotNo, [Prod_Can1_LotNo])",
            "[Prod_Can1_Flow] = COALESCE(@ProdCan1Flow, [Prod_Can1_Flow])",
            "[Prod_Can1_Sec] = COALESCE(@ProdCan1Sec, [Prod_Can1_Sec])",
            "[Prod_Can1_Prs] = COALESCE(@ProdCan1Prs, [Prod_Can1_Prs])",
            "[Prod_Can2_LotNo] = COALESCE(@ProdCan2LotNo, [Prod_Can2_LotNo])",
            "[Prod_Can2_Flow] = COALESCE(@ProdCan2Flow, [Prod_Can2_Flow])",
            "[Prod_Can2_Sec] = COALESCE(@ProdCan2Sec, [Prod_Can2_Sec])",
            "[Prod_Can2_Prs] = COALESCE(@ProdCan2Prs, [Prod_Can2_Prs])",
            "[Prod_Bomb2_LotNo] = COALESCE(@ProdBomb2LotNo, [Prod_Bomb2_LotNo])",
            "[Prod_Bomb2_Flow] = COALESCE(@ProdBomb2Flow, [Prod_Bomb2_Flow])",
            "[Prod_Bomb2_Sec] = COALESCE(@ProdBomb2Sec, [Prod_Bomb2_Sec])",
            "[Prod_Bomb2_Prs] = COALESCE(@ProdBomb2Prs, [Prod_Bomb2_Prs])",
            "[Prod_Bomb1_LotNo] = COALESCE(@ProdBomb1LotNo, [Prod_Bomb1_LotNo])",
            "[Prod_Bomb1_SetFillingPrs] = COALESCE(@ProdBomb1SetFillingPrs, [Prod_Bomb1_SetFillingPrs])",
            "[Prod_Bomb1_Prs] = COALESCE(@ProdBomb1Prs, [Prod_Bomb1_Prs])",
            "[Prod_Bomb3_LotNo] = COALESCE(@ProdBomb3LotNo, [Prod_Bomb3_LotNo])",
            "[Prod_Bomb3_SetFillingPrs] = COALESCE(@ProdBomb3SetFillingPrs, [Prod_Bomb3_SetFillingPrs])",
            "[Prod_Bomb3_Prs] = COALESCE(@ProdBomb3Prs, [Prod_Bomb3_Prs])",
            "[SampleNo] = COALESCE(@SampleNo, [SampleNo])",
            "[SampleType] = COALESCE(@SampleType, [SampleType])",
            "[Container] = COALESCE(@Container, [Container])"
        };

        sql.Should().ContainAll(expectedAssignments);
    }

    [Fact]
    public void MfgJson_update_does_not_write_qc_system_fields()
    {
        var sql = GetMfgJsonUpdateSqlFormat();

        sql.Should().NotContain("[ProdOrder]");
        sql.Should().NotContain("[CalType]");
        sql.Should().NotContain("[Cal_id]");
        sql.Should().NotContain("[IniPrs]");
        sql.Should().NotContain("[QCComplete]");
        sql.Should().NotContain("[QCInst]");
        sql.Should().NotContain("[QCPort]");
        sql.Should().NotContain("[QCTime]");
        sql.Should().NotContain("[Result]");
        sql.Should().NotContain("[RF_ID]");
        sql.Should().NotContain("[FnlPrs]");
    }

    [Fact]
    public void MfgJson_insert_does_not_seed_qc_system_fields()
    {
        var sql = GetMfgJsonInsertSqlFormat();

        sql.Should().NotContain("[ProdOrder]");
        sql.Should().NotContain("[CalType]");
        sql.Should().NotContain("[Cal_id]");
        sql.Should().NotContain("[IniPrs]");
        sql.Should().NotContain("[QCComplete]");
        sql.Should().NotContain("[QCInst]");
        sql.Should().NotContain("[QCPort]");
        sql.Should().NotContain("[QCTime]");
        sql.Should().NotContain("[Result]");
        sql.Should().NotContain("[RF_ID]");
        sql.Should().NotContain("[FnlPrs]");
    }

    [Fact]
    public void MfgJson_update_still_replaces_required_identity_values()
    {
        var sql = GetMfgJsonUpdateSqlFormat();

        sql.Should().Contain("[ID] = @Id");
        sql.Should().Contain("[LotNo] = @LotNo");
        sql.Should().Contain("[si0_id] = @Si0Id");
    }

    private static string GetMfgJsonUpdateSqlFormat()
    {
        var field = typeof(DapperRepository).GetField(
            "MfgJsonUpdateSqlFormat",
            BindingFlags.NonPublic | BindingFlags.Static);

        return field?.GetRawConstantValue() as string
            ?? throw new InvalidOperationException("MfgJsonUpdateSqlFormat was not found.");
    }

    private static string GetMfgJsonInsertSqlFormat()
    {
        var field = typeof(DapperRepository).GetField(
            "MfgJsonInsertSqlFormat",
            BindingFlags.NonPublic | BindingFlags.Static);

        return field?.GetRawConstantValue() as string
            ?? throw new InvalidOperationException("MfgJsonInsertSqlFormat was not found.");
    }
}
