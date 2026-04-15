using System.Data;
using JinZhaoYi.GasQcDataLoader.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Infrastructure;

public sealed class SqlConnectionFactory(
    IConfiguration configuration,
    IOptions<SchedulerOptions> options) : ISqlConnectionFactory
{
    private readonly SchedulerOptions _options = options.Value;

    public IDbConnection CreateConnection()
    {
        var connectionString = configuration.GetConnectionString(_options.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Missing ConnectionStrings:{_options.ConnectionStringName}.");
        }

        return new SqlConnection(connectionString);
    }
}
