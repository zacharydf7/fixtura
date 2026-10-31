using Microsoft.Data.SqlClient;

namespace Fixtura.Tests.Integration.Sql;

internal static class SqlProbe
{
    // Runs a statement on the server's master database with @name set to the database, returning its first value.
    public static async Task<int> ExecuteAsync(SqlConnectionStringBuilder server, string sql, string database)
    {
        var master = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", database);
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
