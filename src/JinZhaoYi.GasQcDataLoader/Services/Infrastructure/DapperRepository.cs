using System.Data;
using Dapper;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Infrastructure;

public sealed class DapperRepository(
    ISqlConnectionFactory sqlConnectionFactory,
    ICalculationService calculationService,
    IOptions<SchedulerOptions> options) : IDapperRepository
{
    private const string LotLookupSqlFormat = """
        SELECT
            ID AS Id,
            LotNo,
            SamplName AS SampleName,
            SampleNo,
            SampleType,
            Container,
            EMVolts,
            RelativeEM
        FROM dbo.{0}
        WHERE LotNo IN @LotNos
        """;

    private const string LatestRfSqlFormat = """
        SELECT TOP (1) *
        FROM dbo.{0}
        WHERE
            CAST(AnlzTime AS date) = @AsOfDate
            OR AnlzTime <= @AsOf
            OR AnlzTime IS NULL
        ORDER BY
            CASE
                WHEN CAST(AnlzTime AS date) = @AsOfDate THEN 0
                WHEN AnlzTime <= @AsOf THEN 1
                ELSE 2
            END,
            AnlzTime DESC,
            CREATE_TIME DESC,
            SID DESC
        """;

    private const string LatestTwoRawRowsSqlFormat = """
        SELECT TOP (2) *
        FROM dbo.{0}
        WHERE LotNo = @LotNo
          AND Port = @Port
          AND (SampleType = @SampleType OR (SampleType IS NULL AND @SampleType IS NULL))
          AND (AnlzTime <= @AnlzTime OR @AnlzTime IS NULL OR AnlzTime IS NULL)
        ORDER BY
            CASE WHEN AnlzTime IS NULL THEN 1 ELSE 0 END,
            AnlzTime DESC,
            CREATE_TIME DESC,
            SID DESC
        """;

    private const string LatestTwoStdRawRowsForPortSqlFormat = """
        SELECT TOP (2) *
        FROM dbo.{0}
        WHERE Port = @StdPort
          AND (SampleType = @SampleType OR (SampleType IS NULL AND @SampleType IS NULL))
          AND (AnlzTime <= @AnlzTime OR @AnlzTime IS NULL OR AnlzTime IS NULL)
        ORDER BY
            CASE WHEN AnlzTime IS NULL THEN 1 ELSE 0 END,
            AnlzTime DESC,
            CREATE_TIME DESC,
            SID DESC
        """;

    private readonly SchedulerTableOptions _tables = options.Value.Tables;

    public async Task<IReadOnlyDictionary<string, MfgLot>> GetLotsByLotNoAsync(IEnumerable<string> lotNos, CancellationToken cancellationToken)
    {
        var distinctLotNos = lotNos.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinctLotNos.Length == 0)
        {
            return new Dictionary<string, MfgLot>(StringComparer.OrdinalIgnoreCase);
        }

        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(LotLookupSqlFormat, Quote(_tables.MfgLot));
        var rows = await connection.QueryAsync<MfgLot>(
            new CommandDefinition(
                sql,
                new { LotNos = distinctLotNos },
                cancellationToken: cancellationToken));

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.LotNo))
            .GroupBy(row => row.LotNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<QcDataRow?> GetLatestRfAsync(DateTime asOf, CancellationToken cancellationToken)
    {
        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(LatestRfSqlFormat, Quote(_tables.Rf));
        var row = await connection.QueryFirstOrDefaultAsync(
            new CommandDefinition(
                sql,
                new { AsOf = asOf, AsOfDate = asOf.Date },
                cancellationToken: cancellationToken));

        return row is null ? null : DynamicToQcDataRow(row);
    }

    public async Task ExecuteImportAsync(ImportWriteSet writeSet, QcDataRow rf, DateTime importDate, CancellationToken cancellationToken)
    {
        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // raw 與 AVG/RPD/PPB 必須在同一個交易內完成，任一錯誤就整批日期資料夾 rollback。
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var sidCounters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            // 依 Quant 時間還原 STD / PORT 連續區段；每組寫完 raw 後，再從 DB 查最新兩筆 raw 計算。
            foreach (var group in BuildRawGroups(writeSet))
            {
                if (group.SourceKind == QuantSourceKind.Std)
                {
                    await ProcessStdGroupAsync(connection, transaction, group.Rows, importDate, sidCounters, cancellationToken);
                    continue;
                }

                await ProcessPortGroupAsync(connection, transaction, group.Rows, rf, importDate, sidCounters, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task ProcessStdGroupAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        IReadOnlyList<QcDataRow> rawRows,
        DateTime importDate,
        IDictionary<string, decimal> sidCounters,
        CancellationToken cancellationToken)
    {
        foreach (var rawRow in rawRows)
        {
            await InsertRowIfMissingAsync(connection, transaction, _tables.StdRaw, rawRow, IncludePpb: true, IncludeRt: true, IncludeIdRefs: false, importDate, sidCounters, cancellationToken);
        }

        // STD AVG/RPD 以目前處理時間點以前，STD raw 表同組最近兩筆為準。
        var latestTwo = await GetLatestTwoRawRowsAsync(connection, transaction, _tables.StdRaw, rawRows[^1], cancellationToken);
        if (latestTwo.Count < 2)
        {
            return;
        }

        var (first, second) = (latestTwo[0], latestTwo[1]);
        var stdAverage = calculationService.CreateAverageRow($"AVG({first.Id}:{second.Id})", first, second);
        var stdRpd = calculationService.CreateRpdRow($"RPD({first.Id}:{second.Id})", first, second);

        // AVG 表是最新狀態表，只保留目前最新一筆平均值。
        await ReplaceSnapshotRowAsync(connection, transaction, _tables.StdAvg, stdAverage, IncludePpb: false, IncludeRt: false, IncludeIdRefs: true, importDate, sidCounters, cancellationToken);
        await InsertRowIfMissingAsync(connection, transaction, _tables.StdRpd, stdRpd, IncludePpb: false, IncludeRt: false, IncludeIdRefs: true, importDate, sidCounters, cancellationToken);
    }

    private async Task ProcessPortGroupAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        IReadOnlyList<QcDataRow> rawRows,
        QcDataRow rf,
        DateTime importDate,
        IDictionary<string, decimal> sidCounters,
        CancellationToken cancellationToken)
    {
        // PORT PPB 使用 PORT 時間點以前最近兩筆 STD raw 重算 active STD AVG，避免重跑時被最後快照影響。
        var activeStdAverage = await GetActiveStdAverageAsync(connection, transaction, rawRows[0], cancellationToken);
        if (activeStdAverage is null)
        {
            throw new InvalidOperationException($"PORT group '{rawRows[0].Port}' starts before any STD AVG is available.");
        }

        foreach (var rawRow in rawRows)
        {
            calculationService.ApplyPortRawPpb(rawRow, rf, activeStdAverage);
            await InsertRowIfMissingAsync(connection, transaction, _tables.PortRaw, rawRow, IncludePpb: true, IncludeRt: true, IncludeIdRefs: false, importDate, sidCounters, cancellationToken);
        }

        // PORT AVG/RPD 依 LotNo + Port + SampleType 查最新兩筆；依需求不加 SampleNo。
        var latestTwo = await GetLatestTwoRawRowsAsync(connection, transaction, _tables.PortRaw, rawRows[^1], cancellationToken);
        if (latestTwo.Count < 2)
        {
            return;
        }

        var (first, second) = (latestTwo[0], latestTwo[1]);
        var portAverage = calculationService.CreateAverageRow($"AVG({first.Id}:{second.Id})", first, second);
        var portPpb = calculationService.CreatePortPpbRow($"ppb({portAverage.Si0Id})", portAverage, rf, activeStdAverage);
        var portRpd = calculationService.CreateRpdRow($"RPD({first.Id}:{second.Id})", first, second);

        // AVG 表是最新狀態表，只保留目前最新一筆平均值。
        await ReplaceSnapshotRowAsync(connection, transaction, _tables.PortAvg, portAverage, IncludePpb: true, IncludeRt: true, IncludeIdRefs: true, importDate, sidCounters, cancellationToken);
        // PPB 的 ID 依 si0_id 命名；同一 Port/Lot 重算時需替換成最新 AVG 對應的 PPB。
        await ReplaceComputedRowAsync(connection, transaction, _tables.PortPpb, portPpb, IncludePpb: false, IncludeRt: false, IncludeIdRefs: true, importDate, sidCounters, cancellationToken);
        await InsertRowIfMissingAsync(connection, transaction, _tables.PortRpd, portRpd, IncludePpb: false, IncludeRt: false, IncludeIdRefs: true, importDate, sidCounters, cancellationToken);
    }

    private async Task<IReadOnlyList<QcDataRow>> GetLatestTwoRawRowsAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string tableName,
        QcDataRow scope,
        CancellationToken cancellationToken)
    {
        var sql = string.Format(LatestTwoRawRowsSqlFormat, Quote(tableName));
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                new { scope.LotNo, scope.Port, scope.SampleType, scope.AnlzTime },
                transaction,
                cancellationToken: cancellationToken));

        return rows
            .Select(DynamicToQcDataRow)
            .Reverse()
            .ToArray();
    }

    private async Task<QcDataRow?> GetActiveStdAverageAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        QcDataRow portRaw,
        CancellationToken cancellationToken)
    {
        var sql = string.Format(LatestTwoStdRawRowsForPortSqlFormat, Quote(_tables.StdRaw));
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                new { StdPort = "STD", portRaw.SampleType, portRaw.AnlzTime },
                transaction,
                cancellationToken: cancellationToken));

        var latestTwo = rows
            .Select(DynamicToQcDataRow)
            .Reverse()
            .ToArray();

        if (latestTwo.Length < 2)
        {
            return null;
        }

        return calculationService.CreateAverageRow($"AVG({latestTwo[0].Id}:{latestTwo[1].Id})", latestTwo[0], latestTwo[1]);
    }

    private async Task InsertRowIfMissingAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string tableName,
        QcDataRow row,
        bool IncludePpb,
        bool IncludeRt,
        bool IncludeIdRefs,
        DateTime importDate,
        IDictionary<string, decimal> sidCounters,
        CancellationToken cancellationToken)
    {
        if (await RowExistsAsync(connection, transaction, tableName, row, cancellationToken))
        {
            if (IsRawTable(tableName))
            {
                await UpdateExistingRawLotMetadataAsync(connection, transaction, tableName, row, cancellationToken);
            }

            return;
        }

        if (!sidCounters.TryGetValue(tableName, out var sid))
        {
            sid = await GetMaxSidAsync(connection, transaction, tableName, importDate, cancellationToken);
        }

        row.Sid = ++sid;
        sidCounters[tableName] = sid;
        await InsertRowAsync(connection, transaction, tableName, row, IncludePpb, IncludeRt, IncludeIdRefs, cancellationToken);
    }

    private static async Task UpdateExistingRawLotMetadataAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string tableName,
        QcDataRow row,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE dbo.{Quote(tableName)}
            SET
                EMVolts =
                    CASE
                        WHEN NULLIF(LTRIM(RTRIM(EMVolts)), N'') IS NULL THEN @EmVolts
                        ELSE EMVolts
                    END,
                RelativeEM =
                    CASE
                        WHEN NULLIF(LTRIM(RTRIM(RelativeEM)), N'') IS NULL THEN @RelativeEm
                        ELSE RelativeEM
                    END
            WHERE LotNo = @LotNo
              AND Port = @Port
              AND SampleNo = @SampleNo
              AND DataFilename = @DataFilename
              AND (
                    (NULLIF(LTRIM(RTRIM(EMVolts)), N'') IS NULL AND @EmVolts IS NOT NULL)
                 OR (NULLIF(LTRIM(RTRIM(RelativeEM)), N'') IS NULL AND @RelativeEm IS NOT NULL)
              )
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { row.EmVolts, row.RelativeEm, row.LotNo, row.Port, row.SampleNo, row.DataFilename },
                transaction,
                cancellationToken: cancellationToken));
    }

    private async Task ReplaceSnapshotRowAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string tableName,
        QcDataRow row,
        bool IncludePpb,
        bool IncludeRt,
        bool IncludeIdRefs,
        DateTime importDate,
        IDictionary<string, decimal> sidCounters,
        CancellationToken cancellationToken)
    {
        var sql = $"DELETE FROM dbo.{Quote(tableName)}";
        await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken));

        sidCounters.Remove(tableName);
        var sid = await GetMaxSidAsync(connection, transaction, tableName, importDate, cancellationToken);
        row.Sid = ++sid;
        sidCounters[tableName] = sid;

        await InsertRowAsync(connection, transaction, tableName, row, IncludePpb, IncludeRt, IncludeIdRefs, cancellationToken);
    }

    private async Task ReplaceComputedRowAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string tableName,
        QcDataRow row,
        bool IncludePpb,
        bool IncludeRt,
        bool IncludeIdRefs,
        DateTime importDate,
        IDictionary<string, decimal> sidCounters,
        CancellationToken cancellationToken)
    {
        var deleteSql = $"""
            DELETE FROM dbo.{Quote(tableName)}
            WHERE ID = @Id
              AND LotNo = @LotNo
              AND Port = @Port
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                deleteSql,
                new { row.Id, row.LotNo, row.Port },
                transaction,
                cancellationToken: cancellationToken));

        sidCounters.Remove(tableName);
        var sid = await GetMaxSidAsync(connection, transaction, tableName, importDate, cancellationToken);
        row.Sid = ++sid;
        sidCounters[tableName] = sid;

        await InsertRowAsync(connection, transaction, tableName, row, IncludePpb, IncludeRt, IncludeIdRefs, cancellationToken);
    }

    private static IReadOnlyList<RawRowGroup> BuildRawGroups(ImportWriteSet writeSet)
    {
        var rows = writeSet.StdRawRows
            .Concat(writeSet.PortRawRows)
            .OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.Port, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DataFilename, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var groups = new List<RawRowGroup>();
        var current = new List<QcDataRow>();

        foreach (var row in rows)
        {
            if (current.Count > 0 && !IsSameRawGroup(current[^1], row))
            {
                groups.Add(new RawRowGroup(GetSourceKind(current[0]), current));
                current = [];
            }

            current.Add(row);
        }

        if (current.Count > 0)
        {
            groups.Add(new RawRowGroup(GetSourceKind(current[0]), current));
        }

        return groups;
    }

    private static bool IsSameRawGroup(QcDataRow left, QcDataRow right) =>
        GetSourceKind(left) == GetSourceKind(right) &&
        string.Equals(left.Port, right.Port, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.LotNo, right.LotNo, StringComparison.OrdinalIgnoreCase);

    private static QuantSourceKind GetSourceKind(QcDataRow row) =>
        string.Equals(row.Port, "STD", StringComparison.OrdinalIgnoreCase)
            ? QuantSourceKind.Std
            : QuantSourceKind.Port;

    private static async Task<decimal> GetMaxSidAsync(SqlConnection connection, IDbTransaction transaction, string tableName, DateTime date, CancellationToken cancellationToken)
    {
        var prefix = decimal.Parse(date.ToString("yyyyMMdd") + "000");
        var upperBound = prefix + 999;
        var sql = $"SELECT ISNULL(MAX(SID), @Prefix) FROM dbo.{Quote(tableName)} WHERE SID >= @Prefix AND SID <= @UpperBound";
        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(sql, new { Prefix = prefix, UpperBound = upperBound }, transaction, cancellationToken: cancellationToken));
    }

    private async Task<bool> RowExistsAsync(SqlConnection connection, IDbTransaction transaction, string tableName, QcDataRow row, CancellationToken cancellationToken)
    {
        var sql = IsRawTable(tableName)
            ? $"""
               SELECT COUNT(1)
               FROM dbo.{Quote(tableName)}
               WHERE LotNo = @LotNo
                 AND Port = @Port
                 AND SampleNo = @SampleNo
                 AND DataFilename = @DataFilename
               """
            : $"""
               SELECT COUNT(1)
               FROM dbo.{Quote(tableName)}
               WHERE ID = @Id
                 AND LotNo = @LotNo
                 AND Port = @Port
                 AND DataFilename = @DataFilename
               """;

        var count = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                sql,
                new { row.Id, row.LotNo, row.Port, row.SampleNo, row.DataFilename },
                transaction,
                cancellationToken: cancellationToken));

        return count > 0;
    }

    private bool IsRawTable(string tableName) =>
        string.Equals(tableName, _tables.StdRaw, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tableName, _tables.PortRaw, StringComparison.OrdinalIgnoreCase);

    private static async Task InsertRowAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string tableName,
        QcDataRow row,
        bool includePpb,
        bool includeRt,
        bool includeIdRefs,
        CancellationToken cancellationToken)
    {
        var values = BuildValues(row, includePpb, includeRt, includeIdRefs);
        var columns = string.Join(", ", values.Keys.Select(Quote));
        var parameters = string.Join(", ", values.Keys.Select(key => "@" + ParameterName(key)));
        var sql = $"INSERT INTO dbo.{Quote(tableName)} ({columns}) VALUES ({parameters})";
        var parametersBag = new DynamicParameters();

        foreach (var (key, value) in values)
        {
            parametersBag.Add(ParameterName(key), value);
        }

        await connection.ExecuteAsync(new CommandDefinition(sql, parametersBag, transaction, cancellationToken: cancellationToken));
    }

    private static Dictionary<string, object?> BuildValues(QcDataRow row, bool includePpb, bool includeRt, bool includeIdRefs)
    {
        var values = new Dictionary<string, object?>
        {
            ["SID"] = row.Sid,
            ["ID"] = row.Id,
            ["AnlzTime"] = row.AnlzTime,
            ["Inst"] = row.Inst,
            ["Port"] = row.Port,
            ["si0_id"] = row.Si0Id,
            ["SampleNo"] = row.SampleNo,
            ["LotNo"] = row.LotNo,
            ["DataFilename"] = row.DataFilename,
            ["DataFilepath"] = row.DataFilepath,
            ["PCName"] = row.PcName,
            ["Container"] = row.Container,
            ["Description"] = row.Description,
            ["EMVolts"] = row.EmVolts,
            ["RelativeEM"] = row.RelativeEm,
            ["SampleName"] = row.SampleName,
            ["SampleType"] = row.SampleType
        };

        foreach (var analyte in CompoundMap.Analytes)
        {
            values[analyte.AreaColumn] = row.Areas.GetValueOrDefault(analyte.Suffix);
        }

        if (includePpb)
        {
            foreach (var analyte in CompoundMap.Analytes)
            {
                values[analyte.PpbColumn] = row.Ppbs.GetValueOrDefault(analyte.Suffix);
            }
        }

        if (includeRt)
        {
            foreach (var analyte in CompoundMap.Analytes)
            {
                values[analyte.RtColumn] = row.RetentionTimes.GetValueOrDefault(analyte.Suffix);
            }
        }

        if (includeIdRefs)
        {
            values["ID1"] = row.Id1;
            values["ID2"] = row.Id2;
        }

        values["CREATE_USER"] = row.CreateUser;
        values["CREATE_TIME"] = row.CreateTime;
        values["EDIT_USER"] = row.EditUser;
        values["EDIT_TIME"] = row.EditTime;
        return values;
    }

    private static QcDataRow DynamicToQcDataRow(dynamic row)
    {
        var dictionary = (IDictionary<string, object?>)row;
        var result = new QcDataRow
        {
            Sid = ReadDecimal(dictionary, "SID"),
            Id = ReadString(dictionary, "ID"),
            AnlzTime = ReadDateTime(dictionary, "AnlzTime"),
            Inst = ReadString(dictionary, "Inst"),
            Port = ReadString(dictionary, "Port"),
            Si0Id = ReadString(dictionary, "si0_id"),
            SampleNo = (int?)ReadDecimal(dictionary, "SampleNo"),
            LotNo = ReadString(dictionary, "LotNo"),
            DataFilename = ReadString(dictionary, "DataFilename"),
            DataFilepath = ReadString(dictionary, "DataFilepath"),
            PcName = ReadString(dictionary, "PCName"),
            Container = ReadString(dictionary, "Container"),
            Description = ReadString(dictionary, "Description"),
            EmVolts = ReadString(dictionary, "EMVolts"),
            RelativeEm = ReadString(dictionary, "RelativeEM"),
            SampleName = ReadString(dictionary, "SampleName"),
            SampleType = ReadString(dictionary, "SampleType"),
            Id1 = ReadString(dictionary, "ID1"),
            Id2 = ReadString(dictionary, "ID2"),
            CreateUser = ReadString(dictionary, "CREATE_USER"),
            CreateTime = ReadDateTime(dictionary, "CREATE_TIME"),
            EditUser = ReadString(dictionary, "EDIT_USER"),
            EditTime = ReadDateTime(dictionary, "EDIT_TIME")
        };

        foreach (var analyte in CompoundMap.Analytes)
        {
            result.Areas[analyte.Suffix] = ReadDecimal(dictionary, analyte.AreaColumn);
            result.Ppbs[analyte.Suffix] = ReadDecimal(dictionary, analyte.PpbColumn);
            result.RetentionTimes[analyte.Suffix] = ReadDecimal(dictionary, analyte.RtColumn);
        }

        return result;
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private static string ParameterName(string columnName) =>
        columnName
            .Replace(" ", "_")
            .Replace(",", "_")
            .Replace("-", "_")
            .Replace(".", "_");

    private static string? ReadString(IDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToString(value) : null;

    private static DateTime? ReadDateTime(IDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDateTime(value) : null;

    private static decimal? ReadDecimal(IDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value) : null;

    private sealed record RawRowGroup(QuantSourceKind SourceKind, IReadOnlyList<QcDataRow> Rows);
}
