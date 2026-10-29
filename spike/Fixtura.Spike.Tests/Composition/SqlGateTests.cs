using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Spike.Tests.Composition;

// Spike S5: no test calls a skip helper, yet without a server they skip, and with FIXTURA_SQLSERVER_REQUIRED=true they fail.
public class SqlGateTests(ApiSqlFixture api) : IClassFixture<ApiSqlFixture>
{
    [Fact]
    public async Task Orders_ShouldBeEmpty_WhenTheDatabaseIsNew()
    {
        using IServiceScope scope = api.CreateScope();

        int count = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Catalog_ShouldSaveAProduct_WhenThePooledContextIsOnSqlServer()
    {
        using IServiceScope scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.Products.Add(new Product { Name = "Lamp" });
        int saved = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task Reports_ShouldSaveARun_WhenTheFactoryContextIsOnSqlServer()
    {
        await using ReportsDbContext db = await api.Services.GetRequiredService<IDbContextFactory<ReportsDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Runs.Add(new ReportRun { RanAt = DateTimeOffset.UtcNow });
        int saved = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task Analytics_ShouldSaveAPageView_WhenThePooledFactoryContextIsOnSqlServer()
    {
        await using AnalyticsDbContext db = await api.Services.GetRequiredService<IDbContextFactory<AnalyticsDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.PageViews.Add(new PageView { Path = "/" });
        int saved = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task Inventory_ShouldBeEmpty_WhenTheInternalContextIsOnSqlServer()
    {
        using IServiceScope scope = api.CreateScope();

        int count = await scope.ServiceProvider.GetRequiredService<IInventory>().CountAsync();

        Assert.Equal(0, count);
    }
}
