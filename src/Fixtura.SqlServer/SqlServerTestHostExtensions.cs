namespace Fixtura;

public static class SqlServerTestHostExtensions
{
    /// <summary>
    /// Adds a SQL Server for this fixture, provisioned before the first test, on LocalDB unless
    /// <paramref name="options"/> say otherwise. When it can't be reached, the fixture's tests skip, or fail when
    /// <see cref="SqlServerOptions.RequiredWhenVariableIsTrue"/> says it's required.
    /// </summary>
    public static SqlServerResource AddSqlServer(this TestHostBuilder host, SqlServerOptions? options = null) =>
        host.AddResource(new SqlServerResource(options ?? new SqlServerOptions()));
}
