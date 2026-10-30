// <features>
using System.Net;
using System.Net.Http.Json;
using Fixtura;
using Microsoft.EntityFrameworkCore;

namespace Bookshop.Tests;

public sealed class InMemoryShop : ITestHostFeature
{
    public void Configure(TestHostBuilder host)
    {
        host.Services.ReplaceDbContext<ShopDb>(db => db.UseInMemoryDatabase(host.UniqueName("Shop")));
        host.RecreateInMemoryDatabasesOnReset();
    }
}

public sealed class QuietBackground : ITestHostFeature
{
    public void Configure(TestHostBuilder host) => host.Services.RemoveHostedService<RestockReminder>();
}
// </features>

// <customer-fixture>
public sealed class CustomerFixture : TestHost<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    protected override void Configure(TestHostBuilder host)
    {
        host.Use<InMemoryShop>().Use<QuietBackground>();

        // Every order ships free, so these tests never reach the shipping partner.
        host.Settings(new { Shop = new { FreeShippingOver = 0 } });
        host.UseFakeClock(Now);
    }
}
// </customer-fixture>

// <order-tests>
public class OrderTests(CustomerFixture shop) : IClassFixture<CustomerFixture>
{
    [Fact]
    public async Task PlaceOrder_ShouldBeUnauthorized_WhenNobodySignedIn()
    {
        HttpClient client = await shop.CreateClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new PlaceOrder(1), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_ShouldBeForbidden_WhenTheUserIsNoCustomer()
    {
        HttpClient client = await shop.CreateClientAsync(TestUser.Named("sam").WithRoles("Clerk"));

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new PlaceOrder(1), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_ShouldRecordTheCustomerAndTime_WhenACustomerOrders()
    {
        HttpClient client = await shop.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new PlaceOrder(1), TestContext.Current.CancellationToken);

        Order? order = await response.Content.ReadFromJsonAsync<Order>(TestContext.Current.CancellationToken);
        Assert.Equal(("ann", CustomerFixture.Now), (order!.Customer, order.PlacedAt));
    }
}
// </order-tests>
