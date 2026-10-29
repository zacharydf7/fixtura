namespace Fixtura.Spike.SqlServer;

public static class SqlServerTestHostExtensions
{
    public static SqlServerResource UseSqlServer(this TestHostBuilder host, SqlServerOptions options) =>
        host.AddResource(new SqlServerResource(options));
}
