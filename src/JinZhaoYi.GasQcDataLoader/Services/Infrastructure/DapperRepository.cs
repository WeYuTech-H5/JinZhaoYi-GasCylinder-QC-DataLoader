using System.Data;
using System.Security.Cryptography;
using System.Text;
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
            si0_id AS Si0Id,
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

    private const string PortPpbRowsSqlFormat = """
        SELECT *
        FROM dbo.{0}
        WHERE ID IN @Ids
          AND LotNo IN @LotNos
          AND Port IN @Ports
        """;

    private const string RowsByDateSqlFormat = """
        SELECT *
        FROM dbo.{0}
        WHERE CAST(AnlzTime AS date) = @BatchDate
        """;

    private const string RowsByDateRangeSqlFormat = """
        SELECT *
        FROM dbo.{0}
        WHERE CAST(AnlzTime AS date) >= @StartDate
          AND CAST(AnlzTime AS date) <= @EndDate
        """;

    private const string PortPpbGroupCountSqlFormat = """
        SELECT COUNT(1)
        FROM (
            SELECT Port, LotNo, SampleName
            FROM dbo.{0}
            WHERE CAST(AnlzTime AS date) = @BatchDate
              AND AnlzTime IS NOT NULL
              AND SampleNo IS NOT NULL
            GROUP BY Port, LotNo, SampleName
        ) grouped
        """;

    private const string PortPpbPagedGroupsSqlFormat = """
        WITH PageGroups AS (
            SELECT Port, LotNo, SampleName
            FROM dbo.{0}
            WHERE CAST(AnlzTime AS date) = @BatchDate
              AND AnlzTime IS NOT NULL
              AND SampleNo IS NOT NULL
            GROUP BY Port, LotNo, SampleName
            ORDER BY Port, LotNo, SampleName
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        )
        SELECT rows.*
        FROM dbo.{0} rows
        INNER JOIN PageGroups pageGroups
            ON ISNULL(rows.Port, '') = ISNULL(pageGroups.Port, '')
           AND ISNULL(rows.LotNo, '') = ISNULL(pageGroups.LotNo, '')
           AND ISNULL(rows.SampleName, '') = ISNULL(pageGroups.SampleName, '')
        WHERE CAST(rows.AnlzTime AS date) = @BatchDate
          AND rows.AnlzTime IS NOT NULL
          AND rows.SampleNo IS NOT NULL
        ORDER BY
            rows.Port,
            rows.LotNo,
            rows.SampleName,
            rows.AnlzTime,
            rows.SampleNo,
            rows.SourceFolderName
        """;

    private const string AllRfRowsSqlFormat = """
        SELECT *
        FROM dbo.{0}
        ORDER BY AnlzTime DESC, CREATE_TIME DESC, SID DESC
        """;

    private const string RfByIdSqlFormat = """
        SELECT TOP (1) *
        FROM dbo.{0}
        WHERE ID = @RfId
        ORDER BY AnlzTime DESC, CREATE_TIME DESC, SID DESC
        """;

    private const string StdRawForRfSqlFormat = """
        SELECT TOP (@Limit) *
        FROM dbo.{0}
        WHERE
            @Search IS NULL
            OR LotNo LIKE @SearchPattern
            OR SampleName LIKE @SearchPattern
            OR CAST(SampleNo AS NVARCHAR(32)) LIKE @SearchPattern
            OR DataFilename LIKE @SearchPattern
            OR SourceFolderName LIKE @SearchPattern
        ORDER BY AnlzTime DESC, CREATE_TIME DESC, SID DESC
        """;

    private const string StdRawForRfPagedSqlFormat = """
        SELECT *
        FROM dbo.{0}
        WHERE
            @Search IS NULL
            OR LotNo LIKE @SearchPattern
            OR SampleName LIKE @SearchPattern
            OR CAST(SampleNo AS NVARCHAR(32)) LIKE @SearchPattern
            OR DataFilename LIKE @SearchPattern
            OR SourceFolderName LIKE @SearchPattern
        ORDER BY AnlzTime DESC, CREATE_TIME DESC, SID DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    private const string StdRawForRfCountSqlFormat = """
        SELECT COUNT(1)
        FROM dbo.{0}
        WHERE
            @Search IS NULL
            OR LotNo LIKE @SearchPattern
            OR SampleName LIKE @SearchPattern
            OR CAST(SampleNo AS NVARCHAR(32)) LIKE @SearchPattern
            OR DataFilename LIKE @SearchPattern
            OR SourceFolderName LIKE @SearchPattern
        """;

    private const string AllStdRawRowsSqlFormat = """
        SELECT *
        FROM dbo.{0}
        ORDER BY AnlzTime DESC, CREATE_TIME DESC, SID DESC
        """;

    private const string RawRowsByIdentitiesSqlFormat = """
        SELECT *
        FROM dbo.{0}
        WHERE LotNo IN @LotNos
          AND Port IN @Ports
          AND SampleNo IN @SampleNos
        """;

    private readonly SchedulerOptions _options = options.Value;
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
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                new { LotNos = distinctLotNos },
                cancellationToken: cancellationToken));

        return rows
            .Select(DynamicToMfgLot)
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

    public async Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsAsync(
        IReadOnlyCollection<PpbRowSelector> selectors,
        CancellationToken cancellationToken)
    {
        var normalizedSelectors = selectors
            .Where(selector =>
                !string.IsNullOrWhiteSpace(selector.Id) &&
                !string.IsNullOrWhiteSpace(selector.LotNo) &&
                !string.IsNullOrWhiteSpace(selector.Port))
            .Distinct()
            .ToArray();

        if (normalizedSelectors.Length == 0)
        {
            return [];
        }

        var ids = normalizedSelectors.Select(selector => selector.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var lotNos = normalizedSelectors.Select(selector => selector.LotNo).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var ports = normalizedSelectors.Select(selector => selector.Port).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(PortPpbRowsSqlFormat, Quote(_tables.PortPpb));
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                new { Ids = ids, LotNos = lotNos, Ports = ports },
                cancellationToken: cancellationToken));

        var selectorSet = normalizedSelectors.ToHashSet();
        return rows
            .Select(DynamicToQcDataRow)
            .Where(row => selectorSet.Contains(PpbRowSelector.FromRow(row)))
            .OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.SampleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlySet<string>> GetExistingRawIdentityIdsAsync(
        IReadOnlyCollection<RawDataIdentity> identities,
        CancellationToken cancellationToken)
    {
        var normalizedIdentities = identities
            .Where(identity =>
                !string.IsNullOrWhiteSpace(identity.LotNo) &&
                !string.IsNullOrWhiteSpace(identity.Port) &&
                identity.SampleNo > 0 &&
                identity.AnlzTime > DateTime.MinValue)
            .Distinct()
            .ToArray();

        if (normalizedIdentities.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var lotNos = normalizedIdentities.Select(identity => identity.LotNo).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var ports = normalizedIdentities.Select(identity => identity.Port).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sampleNos = normalizedIdentities.Select(identity => identity.SampleNo).Distinct().ToArray();
        var expectedIds = normalizedIdentities.Select(identity => identity.ToStableId()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var rows = new List<QcDataRow>();
        foreach (var tableName in new[] { _tables.StdRaw, _tables.PortRaw })
        {
            var sql = string.Format(RawRowsByIdentitiesSqlFormat, Quote(tableName));
            var tableRows = await connection.QueryAsync(
                new CommandDefinition(
                    sql,
                    new { LotNos = lotNos, Ports = ports, SampleNos = sampleNos },
                    cancellationToken: cancellationToken));

            rows.AddRange(tableRows.Select(DynamicToQcDataRow));
        }

        return rows
            .Select(RawDataIdentity.FromRow)
            .Select(identity => identity.ToStableId())
            .Where(expectedIds.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(
        DateTime batchDate,
        CancellationToken cancellationToken)
    {
        return await GetExportOptionsAsync(batchDate.Date, batchDate.Date, cancellationToken);
    }

    public async Task<IReadOnlyList<ExportOption>> GetExportOptionsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var rawRows = await GetRawRowsByDateRangeAsync(startDate.Date, endDate.Date, cancellationToken);

        return rawRows
            .Where(row => row.AnlzTime.HasValue && row.SampleNo.HasValue)
            .GroupBy(row => RawDataIdentity.FromRow(row).ToStableId(), StringComparer.OrdinalIgnoreCase)
            .Select(group => ToExportOption(group.First()))
            .OrderBy(option => option.AnlzTime)
            .ThenBy(option => option.SourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Port, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ExportOption>> GetPortPpbExportOptionsAsync(
        DateTime batchDate,
        CancellationToken cancellationToken)
    {
        var rows = await GetRowsByDateAsync(_tables.PortPpb, batchDate.Date, cancellationToken);
        return rows
            .Where(row => row.AnlzTime.HasValue && row.SampleNo.HasValue)
            .GroupBy(row => RawDataIdentity.FromRow(row).ToStableId(), StringComparer.OrdinalIgnoreCase)
            .Select(group => ToExportOption(group.First()))
            .OrderBy(option => option.AnlzTime)
            .ThenBy(option => option.Port, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PagedResponse<ExportOption>> GetPortPpbExportOptionsAsync(
        DateTime batchDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 500);
        var parameters = new
        {
            BatchDate = batchDate.Date,
            Offset = (normalizedPage - 1) * normalizedPageSize,
            PageSize = normalizedPageSize
        };

        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var countSql = string.Format(PortPpbGroupCountSqlFormat, Quote(_tables.PortPpb));
        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        var pageSql = string.Format(PortPpbPagedGroupsSqlFormat, Quote(_tables.PortPpb));
        var rows = await connection.QueryAsync(
            new CommandDefinition(pageSql, parameters, cancellationToken: cancellationToken));

        var items = rows
            .Select(DynamicToQcDataRow)
            .GroupBy(row => RawDataIdentity.FromRow(row).ToStableId(), StringComparer.OrdinalIgnoreCase)
            .Select(group => ToExportOption(group.First()))
            .OrderBy(option => option.AnlzTime)
            .ThenBy(option => option.Port, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PagedResponse<ExportOption>(normalizedPage, normalizedPageSize, totalCount, items);
    }

    public async Task<IReadOnlyList<RfOption>> GetRfOptionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(AllRfRowsSqlFormat, Quote(_tables.Rf));
        var rows = await connection.QueryAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows
            .Select(DynamicToQcDataRow)
            .Select(row => new RfOption(row.Id, row.AnlzTime, row.Si0Id, row.SampleName, row.SampleNo, row.Description))
            .ToArray();
    }

    public async Task<QcDataRow?> GetRfByIdAsync(string rfId, CancellationToken cancellationToken)
    {
        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(RfByIdSqlFormat, Quote(_tables.Rf));
        var row = await connection.QueryFirstOrDefaultAsync(
            new CommandDefinition(sql, new { RfId = rfId }, cancellationToken: cancellationToken));
        return row is null ? null : DynamicToQcDataRow(row);
    }

    public async Task<IReadOnlyList<ExportOption>> GetStdRawOptionsForRfAsync(
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 500);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(StdRawForRfSqlFormat, Quote(_tables.StdRaw));
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                new
                {
                    Limit = normalizedLimit,
                    Search = normalizedSearch,
                    SearchPattern = normalizedSearch is null ? null : $"%{normalizedSearch}%"
                },
                cancellationToken: cancellationToken));

        return rows
            .Select(DynamicToQcDataRow)
            .Select(ToExportOption)
            .ToArray();
    }

    public async Task<PagedResponse<ExportOption>> GetStdRawOptionsForRfAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 500);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var parameters = new
        {
            Offset = (normalizedPage - 1) * normalizedPageSize,
            PageSize = normalizedPageSize,
            Search = normalizedSearch,
            SearchPattern = normalizedSearch is null ? null : $"%{normalizedSearch}%"
        };

        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var countSql = string.Format(StdRawForRfCountSqlFormat, Quote(_tables.StdRaw));
        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        var pageSql = string.Format(StdRawForRfPagedSqlFormat, Quote(_tables.StdRaw));
        var rows = await connection.QueryAsync(
            new CommandDefinition(pageSql, parameters, cancellationToken: cancellationToken));

        var items = rows
            .Select(DynamicToQcDataRow)
            .Select(ToExportOption)
            .ToArray();

        return new PagedResponse<ExportOption>(normalizedPage, normalizedPageSize, totalCount, items);
    }

    public async Task<QcDataRow?> GetStdRawByStableIdAsync(string stableId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return null;
        }

        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(AllStdRawRowsSqlFormat, Quote(_tables.StdRaw));
        var rows = await connection.QueryAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows
            .Select(DynamicToQcDataRow)
            .FirstOrDefault(row => string.Equals(
                RawDataIdentity.FromRow(row).ToStableId(),
                stableId,
                StringComparison.OrdinalIgnoreCase));
    }

    public async Task<RfOption> UpsertRfAsync(
        QcDataRow row,
        DateTime importDate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.LotNo))
        {
            throw new InvalidOperationException("RF LotNo is required.");
        }

        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var values = BuildRfValues(row);
            values["EDIT_USER"] = row.EditUser;
            values["EDIT_TIME"] = row.EditTime;
            values.Remove("SID");
            values.Remove("CREATE_USER");
            values.Remove("CREATE_TIME");

            var updateSet = string.Join(
                ", ",
                values.Keys
                    .Where(key => !string.Equals(key, "LotNo", StringComparison.OrdinalIgnoreCase))
                    .Select(key => $"{Quote(key)} = @{ParameterName(key)}"));
            var updateParameters = new DynamicParameters();
            foreach (var (key, value) in values)
            {
                updateParameters.Add(ParameterName(key), value);
            }

            var updateSql = $"""
                UPDATE dbo.{Quote(_tables.Rf)}
                SET {updateSet}
                WHERE LotNo = @LotNo
                """;

            var affectedRows = await connection.ExecuteAsync(
                new CommandDefinition(
                    updateSql,
                    updateParameters,
                    transaction,
                    cancellationToken: cancellationToken));

            if (affectedRows == 0)
            {
                row.Sid = await GetMaxSidAsync(connection, transaction, _tables.Rf, importDate, cancellationToken) + 1;
                var insertValues = BuildRfValues(row);
                var columns = string.Join(", ", insertValues.Keys.Select(Quote));
                var parameters = string.Join(", ", insertValues.Keys.Select(key => "@" + ParameterName(key)));
                var insertSql = $"INSERT INTO dbo.{Quote(_tables.Rf)} ({columns}) VALUES ({parameters})";
                var insertParameters = new DynamicParameters();
                foreach (var (key, value) in insertValues)
                {
                    insertParameters.Add(ParameterName(key), value);
                }

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertSql,
                        insertParameters,
                        transaction,
                        cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new RfOption(row.Id, row.AnlzTime, row.Si0Id, row.SampleName, row.SampleNo, row.Description);
    }

    public async Task<IReadOnlyList<QcDataRow>> GetRawRowsForExportAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken)
    {
        var selectedSet = selectedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Count == 0)
        {
            return [];
        }

        var rawRows = await GetRawRowsByDateRangeAsync(startDate.Date, endDate.Date, cancellationToken);
        return FilterRowsByStableIds(rawRows, selectedSet)
            .OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.SampleNo)
            .ThenBy(row => row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DataFilename, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<Query2ExportRow>> GetQuery2ExportRowsAsync(
        DateTime batchDate,
        IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken)
    {
        var selectedSet = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Count == 0)
        {
            return [];
        }

        var rows = new List<Query2ExportRow>();
        var rawRows = await GetRawRowsByDateAsync(batchDate.Date, cancellationToken);
        var selectedRawRows = FilterRowsByStableIds(rawRows, selectedSet).ToArray();
        if (selectedRawRows.Length == 0)
        {
            return [];
        }

        var rf = await GetLatestRfAsync(selectedRawRows.Min(row => row.AnlzTime) ?? batchDate.Date, cancellationToken);
        if (rf is not null)
        {
            rows.Add(new Query2ExportRow(Query2ExportRowType.Rf, rf));
        }

        foreach (var rawRow in selectedRawRows)
        {
            rows.Add(new Query2ExportRow(Query2ExportRowType.Raw, rawRow));
        }

        foreach (var rowType in new[]
                 {
                     (Table: _tables.StdAvg, RowType: Query2ExportRowType.Avg),
                     (Table: _tables.PortAvg, RowType: Query2ExportRowType.Avg),
                     (Table: _tables.PortPpb, RowType: Query2ExportRowType.Ppb),
                     (Table: _tables.StdRpd, RowType: Query2ExportRowType.Rpd),
                     (Table: _tables.PortRpd, RowType: Query2ExportRowType.Rpd),
                     (Table: _tables.StdQc, RowType: Query2ExportRowType.Qc)
                 })
        {
            var computedRows = await GetRowsByDateAsync(rowType.Table, batchDate.Date, cancellationToken);
            rows.AddRange(FilterRowsByStableIds(computedRows, selectedSet)
                .Select(row => new Query2ExportRow(rowType.RowType, row)));
        }

        return rows
            .OrderBy(row => row.RowType == Query2ExportRowType.Rf ? 0 : 1)
            .ThenBy(row => row.Row.AnlzTime)
            .ThenBy(row => row.Row.SampleNo)
            .ThenBy(row => row.Row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Row.Port, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => RowTypeOrder(row.RowType))
            .ThenBy(row => row.Row.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<QcDataRow>> GetPortPpbRowsForExportAsync(
        DateTime batchDate,
        IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken)
    {
        var selectedSet = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Count == 0)
        {
            return [];
        }

        var rows = await GetRowsByDateAsync(_tables.PortPpb, batchDate.Date, cancellationToken);
        return FilterRowsByStableIds(rows, selectedSet)
            .OrderBy(row => row.AnlzTime)
            .ThenBy(row => row.SampleNo)
            .ThenBy(row => row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SampleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task UpsertImportErrorLogsAsync(
        IReadOnlyCollection<ImportErrorReportRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var sql = $"""
                UPDATE dbo.{Quote(_tables.ImportErrorLog)}
                SET
                    LAST_OCCURRED_AT = @LAST_OCCURRED_AT,
                    OCCURRENCE_COUNT = OCCURRENCE_COUNT + 1,
                    MESSAGE = @MESSAGE,
                    SUGGESTED_ACTION = @SUGGESTED_ACTION,
                    EDIT_USER = @EDIT_USER,
                    EDIT_TIME = @EDIT_TIME
                WHERE QUANT_PATH = @QUANT_PATH
                  AND ERROR_TYPE = @ERROR_TYPE
                  AND SOURCE_KEY_HASH = @SOURCE_KEY_HASH
                  AND ((LOT_NO = @LOT_NO) OR (LOT_NO IS NULL AND @LOT_NO IS NULL));

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.{Quote(_tables.ImportErrorLog)}
                    (
                        OCCURRED_AT,
                        LAST_OCCURRED_AT,
                        OCCURRENCE_COUNT,
                        LOGICAL_BATCH_DATE,
                        PORT,
                        TOP_FOLDER_NAME,
                        LOT_NO,
                        QUANT_PATH,
                        SOURCE_KEY_HASH,
                        DATA_FOLDER_PATH,
                        ERROR_TYPE,
                        MESSAGE,
                        SUGGESTED_ACTION,
                        CREATE_USER,
                        CREATE_TIME
                    )
                    VALUES
                    (
                        @OCCURRED_AT,
                        @LAST_OCCURRED_AT,
                        1,
                        @LOGICAL_BATCH_DATE,
                        @PORT,
                        @TOP_FOLDER_NAME,
                        @LOT_NO,
                        @QUANT_PATH,
                        @SOURCE_KEY_HASH,
                        @DATA_FOLDER_PATH,
                        @ERROR_TYPE,
                        @MESSAGE,
                        @SUGGESTED_ACTION,
                        @CREATE_USER,
                        @CREATE_TIME
                    );
                END
                """;

            foreach (var row in rows)
            {
                var occurredAt = row.OccurredAt.LocalDateTime;
                var lotNo = string.IsNullOrWhiteSpace(row.LotNo) ? null : row.LotNo;
                var parameters = new DynamicParameters();
                parameters.Add("OCCURRED_AT", occurredAt);
                parameters.Add("LAST_OCCURRED_AT", occurredAt);
                parameters.Add("LOGICAL_BATCH_DATE", row.LogicalBatchDate);
                parameters.Add("PORT", row.Port);
                parameters.Add("TOP_FOLDER_NAME", row.TopFolderName);
                parameters.Add("LOT_NO", lotNo);
                parameters.Add("QUANT_PATH", row.QuantPath);
                parameters.Add("SOURCE_KEY_HASH", ComputeImportErrorSourceHash(row.QuantPath, row.ErrorType, lotNo));
                parameters.Add("DATA_FOLDER_PATH", row.DataFolderPath);
                parameters.Add("ERROR_TYPE", row.ErrorType);
                parameters.Add("MESSAGE", row.Message);
                parameters.Add("SUGGESTED_ACTION", row.SuggestedAction);
                parameters.Add("CREATE_USER", "GasQcDataLoader");
                parameters.Add("CREATE_TIME", DateTime.Now);
                parameters.Add("EDIT_USER", "GasQcDataLoader");
                parameters.Add("EDIT_TIME", DateTime.Now);

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        parameters,
                        transaction,
                        cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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

            foreach (var stdQcRow in writeSet.StdQcRows)
            {
                await InsertRowIfMissingAsync(connection, transaction, _tables.StdQc, stdQcRow, IncludePpb: false, IncludeRt: false, IncludeIdRefs: true, importDate, sidCounters, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyList<QcDataRow>> GetRawRowsByDateAsync(DateTime batchDate, CancellationToken cancellationToken)
    {
        var rows = new List<QcDataRow>();
        rows.AddRange(await GetRowsByDateAsync(_tables.StdRaw, batchDate, cancellationToken));
        rows.AddRange(await GetRowsByDateAsync(_tables.PortRaw, batchDate, cancellationToken));
        return rows;
    }

    private async Task<IReadOnlyList<QcDataRow>> GetRawRowsByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var rows = new List<QcDataRow>();
        rows.AddRange(await GetRowsByDateRangeAsync(_tables.StdRaw, startDate, endDate, cancellationToken));
        rows.AddRange(await GetRowsByDateRangeAsync(_tables.PortRaw, startDate, endDate, cancellationToken));
        return rows;
    }

    private async Task<IReadOnlyList<QcDataRow>> GetRowsByDateAsync(
        string tableName,
        DateTime batchDate,
        CancellationToken cancellationToken)
    {
        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(RowsByDateSqlFormat, Quote(tableName));
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                new { BatchDate = batchDate.Date },
                cancellationToken: cancellationToken));

        return rows.Select(DynamicToQcDataRow).ToArray();
    }

    private async Task<IReadOnlyList<QcDataRow>> GetRowsByDateRangeAsync(
        string tableName,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        await using var connection = (SqlConnection)sqlConnectionFactory.CreateConnection();
        var sql = string.Format(RowsByDateRangeSqlFormat, Quote(tableName));
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                new { StartDate = startDate.Date, EndDate = endDate.Date },
                cancellationToken: cancellationToken));

        return rows.Select(DynamicToQcDataRow).ToArray();
    }

    private static IEnumerable<QcDataRow> FilterRowsByStableIds(
        IEnumerable<QcDataRow> rows,
        IReadOnlySet<string> selectedIds) =>
        rows.Where(row => selectedIds.Contains(RawDataIdentity.FromRow(row).ToStableId()));

    private static ExportOption ToExportOption(QcDataRow row)
    {
        var id = RawDataIdentity.FromRow(row).ToStableId();
        var displayName = !string.IsNullOrWhiteSpace(row.DataFilepath)
            ? Path.GetFileName(row.DataFilepath)
            : row.SampleName ?? id;

        return new ExportOption(
            id,
            displayName,
            row.AnlzTime?.ToString("yyyyMMdd") ?? string.Empty,
            row.SourceKind,
            row.SourceFolderName,
            row.Port ?? string.Empty,
            row.LotNo ?? string.Empty,
            row.SampleName,
            row.SampleNo,
            row.DataFilename,
            row.DataFilepath,
            row.AnlzTime);
    }

    private static int RowTypeOrder(Query2ExportRowType rowType) =>
        rowType switch
        {
            Query2ExportRowType.Rf => 0,
            Query2ExportRowType.Raw => 1,
            Query2ExportRowType.Avg => 2,
            Query2ExportRowType.Ppb => 3,
            Query2ExportRowType.Rpd => 4,
            Query2ExportRowType.Qc => 5,
            Query2ExportRowType.Crit => 6,
            _ => 99
        };

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

        await WriteAverageRowAsync(connection, transaction, _tables.StdAvg, stdAverage, IncludePpb: false, IncludeRt: false, IncludeIdRefs: true, importDate, sidCounters, cancellationToken);
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

        await WriteAverageRowAsync(connection, transaction, _tables.PortAvg, portAverage, IncludePpb: true, IncludeRt: true, IncludeIdRefs: true, importDate, sidCounters, cancellationToken);
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

    private async Task WriteAverageRowAsync(
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
        if (_options.UseAverageSnapshotTables)
        {
            await ReplaceSnapshotRowAsync(connection, transaction, tableName, row, IncludePpb, IncludeRt, IncludeIdRefs, importDate, sidCounters, cancellationToken);
            return;
        }

        await InsertRowIfMissingAsync(connection, transaction, tableName, row, IncludePpb, IncludeRt, IncludeIdRefs, importDate, sidCounters, cancellationToken);
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
            .ThenBy(row => row.SampleNo)
            .ThenBy(row => row.SourceFolderName, StringComparer.OrdinalIgnoreCase)
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
            ["SourceKind"] = row.SourceKind,
            ["SourceFolderName"] = row.SourceFolderName,
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

    private static Dictionary<string, object?> BuildRfValues(QcDataRow row)
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
            SourceKind = ReadString(dictionary, "SourceKind"),
            SourceFolderName = ReadString(dictionary, "SourceFolderName"),
            Si0Id = ReadInt(dictionary, "si0_id"),
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

    private static MfgLot DynamicToMfgLot(dynamic row)
    {
        var dictionary = (IDictionary<string, object?>)row;
        return new MfgLot
        {
            Id = ReadDecimal(dictionary, "Id") ?? 0m,
            LotNo = ReadString(dictionary, "LotNo") ?? string.Empty,
            Si0Id = ReadInt(dictionary, "Si0Id"),
            SampleName = ReadString(dictionary, "SampleName"),
            SampleNo = ReadString(dictionary, "SampleNo"),
            SampleType = ReadString(dictionary, "SampleType"),
            Container = ReadString(dictionary, "Container"),
            EMVolts = ReadString(dictionary, "EMVolts"),
            RelativeEM = ReadString(dictionary, "RelativeEM")
        };
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private static byte[] ComputeImportErrorSourceHash(string quantPath, string errorType, string? lotNo)
    {
        var sourceKey = string.Join(
            '\u001F',
            quantPath,
            errorType,
            lotNo ?? "<NULL>");
        return SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey));
    }

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

    private static int? ReadInt(IDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt32(value) : null;

    private sealed record RawRowGroup(QuantSourceKind SourceKind, IReadOnlyList<QcDataRow> Rows);
}
