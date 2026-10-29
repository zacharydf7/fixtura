namespace Fixtura;

public static class SqlServerTestHostExtensions
{
    /// <summary>
    /// Adds a SQL Server for this fixture, provisioned before the first test. When it can't be reached, the fixture's tests
    /// skip, or fail when <see cref="SqlServerOptions.RequiredWhenSet"/> says it's required.
    /// </summary>
    public static SqlServerResource UseSqlServer(this TestHostBuilder host, SqlServerOptions options) =>
        host.AddResource(new SqlServerResource(options));
}
