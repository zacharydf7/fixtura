namespace Fixtura;

public static class PostgreSqlTestHostExtensions
{
    /// <summary>
    /// Adds a PostgreSQL server for this fixture, provisioned before the first test. When it can't be reached, the fixture's
    /// tests skip, or fail when <see cref="PostgreSqlOptions.RequiredWhenVariableIsTrue"/> says it's required.
    /// </summary>
    public static PostgreSqlResource AddPostgreSql(this TestHostBuilder host, PostgreSqlOptions options) =>
        host.AddResource(new PostgreSqlResource(options));
}
