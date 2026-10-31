using Fixtura.Tests.Integration.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Tests.Integration.Sql;

// Every SQL-backed host shape shares this one feature: a test server, one database per context, schemas created at start.
public sealed class SampleSqlDatabases(bool reuseDatabases) : ITestHostFeature
{
    public SampleSqlDatabases() : this(false)
    {
    }

    public void Configure(TestHostBuilder host)
    {
        SqlServerResource sql = host.AddSqlServer(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
            RequiredWhenVariableIsTrue = "FIXTURA_SQLSERVER_REQUIRED",
            ReuseDatabases = reuseDatabases,

            // A stopped LocalDB instance takes several seconds to start on the first connection.
            ReadyTimeout = TimeSpan.FromSeconds(15)
        });

        host.Services
            .ReplaceDbContext<AppIdentityDbContext>(db => db.UseSqlServer(sql.ConnectionString("Identity")))
            .ReplaceDbContext<OrdersDbContext>(db => db.UseSqlServer(sql.ConnectionString("Orders")))
            .ReplaceDbContext<CatalogDbContext>(db => db.UseSqlServer(sql.ConnectionString("Catalog")))
            .ReplaceDbContext<ReportsDbContext>(db => db.UseSqlServer(sql.ConnectionString("Reports")))
            .ReplaceDbContext<AnalyticsDbContext>(db => db.UseSqlServer(sql.ConnectionString("Analytics")))
            .ReplaceDbContexts(typeof(IInventory).Assembly, db => db.UseSqlServer(sql.ConnectionString("Inventory")))
            .Configure(services => services.AddHostedService<CreateSchemas>());
    }
}

// The sample has no migrations, so each test database gets its schema from the model before the app serves requests.
public sealed class CreateSchemas(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();
        foreach (DbContextOptions options in scope.ServiceProvider.GetServices<DbContextOptions>())
        {
            await using var db = (DbContext)ActivatorUtilities.CreateInstance(scope.ServiceProvider, options.ContextType, options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// The three SQL host shapes: the same shared features as the in-memory shapes, plus one SQL feature.
public sealed class ApiSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use<SampleSqlDatabases>().Use<FakeBus>().Use<QuietBackground>();
}

public sealed class ApiWorkerSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use<SampleSqlDatabases>().Use<FakeBus>().Use<NoReportScheduler>();
}

public sealed class ApiWorkerPartnerSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use<SampleSqlDatabases>().Use<FakeBus>().Use<NoReportScheduler>().Use<PartnerCarrier>();
}
