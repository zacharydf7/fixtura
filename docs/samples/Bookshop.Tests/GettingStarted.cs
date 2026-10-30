using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Bookshop.Tests;

// <fixture>
public sealed class ShopFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services
            .ReplaceDbContext<ShopDb>(db => db.UseInMemoryDatabase(host.UniqueName("Shop")))
            .RemoveHostedService<RestockReminder>();
    }
}
// </fixture>

// <first-test>
public class BookTests(ShopFixture shop) : IClassFixture<ShopFixture>
{
    [Fact]
    public async Task GetBooks_ShouldListTheCatalog_WhenCalled()
    {
        HttpClient client = await shop.CreateClientAsync();

        List<Book>? books = await client.GetFromJsonAsync<List<Book>>("/books", TestContext.Current.CancellationToken);

        Assert.Equal(["The Quiet Harbor", "Maps of Small Towns"], books!.Select(book => book.Title));
    }
}
// </first-test>
