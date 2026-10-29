using Fixtura.Spike.EntityFrameworkCore;
using Fixtura.Spike.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleApp;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Spike.Tests.Composition;

// Every SQL-backed host shape shares this one feature: a test server, one database per context, schemas created at start.
public sealed class SampleSqlDatabases : ITestHostFeature
{
    public void Configure(TestHostBuilder host)
    {
        SqlServerResource sql = host.UseSqlServer(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
            RequiredWhenSet = "FIXTURA_SQLSERVER_REQUIRED",

            // A stopped LocalDB instance takes several seconds to start on the first connection.
            ReadyTimeout = TimeSpan.FromSeconds(15)
        });

        host.Services
            .ReplaceDbContext<AppIdentityDbContext>(db => db.UseSqlServer(sql.Database("Identity")))
            .ReplaceDbContext<OrdersDbContext>(db => db.UseSqlServer(sql.Database("Orders")))
            .ReplaceDbContext<CatalogDbContext>(db => db.UseSqlServer(sql.Database("Catalog")))
            .ReplaceDbContext<ReportsDbContext>(db => db.UseSqlServer(sql.Database("Reports")))
            .ReplaceDbContext<AnalyticsDbContext>(db => db.UseSqlServer(sql.Database("Analytics")))
            .ReplaceDbContexts(typeof(IInventory).Assembly, db => db.UseSqlServer(sql.Database("Inventory")))
            .Configure(services => services.AddHostedService<CreateSchemas>());
    }
}

public sealed class FakeBus : ITestHostFeature
{
    public void Configure(TestHostBuilder host) => host.Services.Replace<IOrderPublisher, NullOrderPublisher>();
}

public sealed class NoReportScheduler : ITestHostFeature
{
    public void Configure(TestHostBuilder host) => host.Services.RemoveHostedService<ReportScheduler>();
}

// Builds on NoReportScheduler; adding both to one host is safe because a feature runs once.
public sealed class QuietBackground : ITestHostFeature
{
    public void Configure(TestHostBuilder host)
    {
        host.Use<NoReportScheduler>();
        host.Services.RemoveHostedService<OutboxWorker>();
    }
}

// Stands for an external client the API talks to, configured as typed options rather than index keys.
public sealed class PartnerCarrier : ITestHostFeature
{
    public void Configure(TestHostBuilder host) =>
        host.Options<ShippingOptions>(shipping => shipping.Carriers.Add(new Carrier { Name = "Partner" }));
}

public sealed class NullOrderPublisher : IOrderPublisher
{
    public Task PublishAsync(OrderPlaced message) => Task.CompletedTask;
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
