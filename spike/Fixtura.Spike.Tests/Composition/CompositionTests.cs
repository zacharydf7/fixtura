using System.Net.Http.Json;
using Fixtura.Spike.Tests.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleApp;
using SampleApp.Data;
using SampleApp.Inventory;

[assembly: AssemblyFixture(typeof(ApiWorkerPartnerSqlFixture))]

namespace Fixtura.Spike.Tests.Composition;

// No test here calls a skip helper: without a SQL Server, the fixture's first use skips the test.
public class ClassFixtureTests(ApiSqlFixture api) : IClassFixture<ApiSqlFixture>
{
    [Fact]
    public async Task Orders_ShouldRoundTripThroughTheTestDatabase_WhenSharedAsAClassFixture()
    {
        using (IServiceScope scope = api.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Orders.Add(new Order { Product = "Lamp", Quantity = 2 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using IServiceScope check = api.CreateScope();
        var orders = check.ServiceProvider.GetRequiredService<OrdersDbContext>();

        Assert.StartsWith("fixtura_", orders.Database.GetDbConnection().Database);
        Assert.True(await orders.Orders.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void HostedServices_ShouldAllBeRemoved_WhenTheShapeIsApiOnly()
    {
        IEnumerable<IHostedService> hostedServices = api.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, service => service is OutboxWorker or ReportScheduler);
    }
}

[CollectionDefinition(Name)]
public class ApiWorkerCollection : ICollectionFixture<ApiWorkerSqlFixture>
{
    public const string Name = "api-worker";
}

[Collection(ApiWorkerCollection.Name)]
public class CollectionFixtureTests(ApiWorkerSqlFixture api)
{
    [Fact]
    public void Worker_ShouldStillRun_WhenTheShapeIncludesIt()
    {
        IEnumerable<IHostedService> hostedServices = api.Services.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service => service is OutboxWorker);
        Assert.DoesNotContain(hostedServices, service => service is ReportScheduler);
    }

    [Fact]
    public async Task InternalContext_ShouldUseTheTestDatabase_WhenSwappedByAssembly()
    {
        using IServiceScope scope = api.CreateScope();

        int count = await scope.ServiceProvider.GetRequiredService<IInventory>().CountAsync();

        Assert.Equal(0, count);
    }
}

public class AssemblyFixtureTests(ApiWorkerPartnerSqlFixture api)
{
    [Fact]
    public async Task Carriers_ShouldIncludeThePartner_WhenSharedAsAnAssemblyFixture()
    {
        string[]? carriers = await api.CreateClient()
            .GetFromJsonAsync<string[]>("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(["Ground", "Air", "Freight", "Partner"], carriers!);
    }
}
