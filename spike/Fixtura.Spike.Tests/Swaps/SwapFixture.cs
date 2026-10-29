using Fixtura.Spike.EntityFrameworkCore;
using Fixtura.Spike.Tests.Composition;
using Microsoft.EntityFrameworkCore;
using SampleApp;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Spike.Tests.Swaps;

// Every swap S2 covers, applied to the real sample app with in-memory databases.
public sealed class SwapFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services
            .ReplaceDbContext<AppIdentityDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Identity")))
            .ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")))
            .ReplaceDbContext<CatalogDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Catalog")))
            .ReplaceDbContext<ReportsDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Reports")))
            .ReplaceDbContext<AnalyticsDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Analytics")))
            .ReplaceDbContexts(typeof(IInventory).Assembly, db => db.UseInMemoryDatabase(host.UniqueName("Inventory")))
            .RemoveHostedService<OutboxWorker>()
            .RemoveHostedService<ReportScheduler>()
            .Replace<IOrderPublisher, NullOrderPublisher>()
            .ReplaceKeyed<IPaymentGateway, TestCardGateway>(PaymentMethods.Card);
    }
}

public sealed class TestCardGateway : IPaymentGateway
{
    public string Name => "Test card";
}
