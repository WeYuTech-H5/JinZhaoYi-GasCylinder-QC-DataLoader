using System.Data;

namespace JinZhaoYi.GasQcDataLoader.Services.Infrastructure;

public interface ISqlConnectionFactory
{
    IDbConnection CreateConnection();
}
