// <shipping-fixture>
using System.Net;
using System.Net.Http.Json;
using Fixtura;

namespace Bookshop.Tests;

public sealed class ShippingFixture : TestHost<Program>
{
    public HttpFake Shipping { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host)
    {
        host.Use<InMemoryShop>().Use<QuietBackground>();
        Shipping = host.Http.Fake<ShippingClient>().Respond(HttpMethod.Post, "/quotes", HttpStatusCode.OK, new Quote(4.99m));
        host.LogToTestOutput();
    }
}
// </shipping-fixture>

// <shipping-tests>
public class ShippingTests(ShippingFixture shop) : IClassFixture<ShippingFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await shop.ResetDataAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PlaceOrder_ShouldChargeTheQuote_WhenTheBookIsUnderTheFreeShippingPrice()
    {
        HttpClient client = await shop.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new PlaceOrder(1), TestContext.Current.CancellationToken);

        Order order = await shop.ReadAsync<Order>(response);
        Assert.Equal(4.99m, order.Shipping);
        Assert.Equal(1, shop.Shipping.Requests[^1].Json<QuoteRequest>()?.BookId);
    }

    [Fact]
    public async Task PlaceOrder_ShouldNotAskForAQuote_WhenShippingIsFree()
    {
        HttpClient client = await shop.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));

        await client.PostAsJsonAsync("/orders", new PlaceOrder(2), TestContext.Current.CancellationToken);

        Assert.Empty(shop.Shipping.Requests);
    }

    [Fact]
    public async Task PlaceOrder_ShouldFail_WhenTheShippingPartnerIsDown()
    {
        shop.Shipping.Respond(HttpMethod.Post, "/quotes", HttpStatusCode.ServiceUnavailable);
        HttpClient client = await shop.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new PlaceOrder(1), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}

// The part of the app's quote request these tests read.
public sealed record QuoteRequest(int BookId);
// </shipping-tests>

