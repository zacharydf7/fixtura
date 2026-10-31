// <feed-fixture>
using System.Net;
using System.Net.Http.Json;
using Fixtura;
using Microsoft.AspNetCore.SignalR.Client;

namespace Bookshop.Tests;

public sealed class OrderFeedFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Use<InMemoryShop>().Use<QuietBackground>();
        host.Settings(new { Shop = new { FreeShippingOver = 0 } });
        host.LogToTestOutput();
    }
}
// </feed-fixture>

// <feed-tests>
public class OrderFeedTests(OrderFeedFixture shop) : IClassFixture<OrderFeedFixture>
{
    [Fact]
    public async Task OrderFeed_ShouldShowAClerkTheOrder_WhenACustomerPlacesOne()
    {
        // <hub-connection>
        await using HubConnection feed = await shop.CreateHubConnectionAsync("/hubs/orders", TestUser.Named("sam").WithRoles("Clerk"));
        HubMessages<Order> placed = feed.Record<Order>("OrderPlaced");
        await feed.StartAsync(TestContext.Current.CancellationToken);
        HttpClient ann = await shop.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));

        await ann.PostAsJsonAsync("/orders", new PlaceOrder(2), TestContext.Current.CancellationToken);

        Order order = await placed.WaitForAsync(order => order.Customer == "ann");
        Assert.Equal(2, order.BookId);
        // </hub-connection>
    }

    [Fact]
    public async Task OrderFeed_ShouldTurnTheConnectionAway_WhenTheUserIsNoClerk()
    {
        await using HubConnection feed = await shop.CreateHubConnectionAsync("/hubs/orders", TestUser.Named("ann").WithRoles("Customer"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => feed.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }
}
// </feed-tests>
