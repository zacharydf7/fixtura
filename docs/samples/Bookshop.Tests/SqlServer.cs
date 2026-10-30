using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Bookshop.Tests;

// <sql-fixture>
public sealed class SqlShopFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        SqlServerResource sql = host.UseSqlServer(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
            RequiredWhenSet = "FIXTURA_SQLSERVER_REQUIRED",
            KeepOnReset = ["Books"]
        });
        host.Services.ReplaceDbContext<ShopDb>(db => db.UseSqlServer(sql.Database("Shop")));
        host.Use<QuietBackground>();
        host.Settings(new { Shop = new { FreeShippingOver = 0 } });
    }
}
// </sql-fixture>

// <container-fixture>
public sealed class ContainerShopFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        SqlServerResource sql = host.UseSqlServer(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrContainer("FIXTURA_SQLSERVER"),
            RequiredWhenSet = "FIXTURA_SQLSERVER_REQUIRED",
            KeepOnReset = ["Books"]
        });
        host.Services.ReplaceDbContext<ShopDb>(db => db.UseSqlServer(sql.Database("Shop")));
        host.Use<QuietBackground>();
        host.Settings(new { Shop = new { FreeShippingOver = 0 } });
    }
}
// </container-fixture>

public class ContainerOrderTests(ContainerShopFixture shop) : IClassFixture<ContainerShopFixture>
{
    [Fact]
    public async Task PlaceOrder_ShouldBeCreated_WhenTheServerRunsInAContainer()
    {
        HttpClient ann = await shop.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));

        HttpResponseMessage response = await ann.PostAsJsonAsync("/orders", new PlaceOrder(1), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}

// <reset>
public class SqlOrderTests(SqlShopFixture shop) : IClassFixture<SqlShopFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await shop.ResetDataAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    // </reset>

    // <mine>
    [Fact]
    public async Task MyOrders_ShouldListOnlyTheCustomersOwn_WhenOthersOrderedToo()
    {
        HttpClient ann = await shop.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));
        HttpClient bob = await shop.CreateClientAsync(TestUser.Named("bob").WithRoles("Customer"));
        await ann.PostAsJsonAsync("/orders", new PlaceOrder(1), TestContext.Current.CancellationToken);
        await bob.PostAsJsonAsync("/orders", new PlaceOrder(2), TestContext.Current.CancellationToken);

        List<Order>? orders = await ann.GetFromJsonAsync<List<Order>>("/orders/mine", TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(orders!).BookId);
    }
    // </mine>

    // <numbering>
    [Fact]
    public async Task PlaceOrder_ShouldNumberTheFirstOrderOne_WhenEachTestStartsFromAReset()
    {
        HttpClient ann = await shop.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));

        HttpResponseMessage response = await ann.PostAsJsonAsync("/orders", new PlaceOrder(1), TestContext.Current.CancellationToken);

        Assert.Equal("/orders/1", response.Headers.Location?.OriginalString);
    }
    // </numbering>

    // <kept>
    [Fact]
    public async Task GetBooks_ShouldStillListTheCatalog_WhenTheBooksTableIsKept()
    {
        HttpClient client = await shop.CreateClientAsync();

        List<Book>? books = await client.GetFromJsonAsync<List<Book>>("/books", TestContext.Current.CancellationToken);

        Assert.Equal(2, books!.Count);
    }
    // </kept>
}
