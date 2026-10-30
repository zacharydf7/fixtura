using System.Net;
using System.Net.Http.Json;

namespace Bookshop.Tests;

// The landing page's example, kept narrow for its code window.
// <landing>
public sealed class ShopApi : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Use<InMemoryShop>().Use<QuietBackground>();
        host.Settings(new { Shop = new { FreeShippingOver = 0 } });
    }
}

public class CheckoutTests(ShopApi shop) : IClassFixture<ShopApi>
{
    [Fact]
    public async Task PlaceOrder_ShouldBeCreated_WhenACustomerOrders()
    {
        var ann = TestUser.Named("ann").WithRoles("Customer");
        HttpClient client = await shop.CreateClientAsync(ann);

        var response = await client.PostAsJsonAsync("/orders",
            new PlaceOrder(1), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
// </landing>
