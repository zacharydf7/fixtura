using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Bookshop;

public sealed class Book
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public decimal Price { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }

    public string Customer { get; set; } = "";

    public int BookId { get; set; }

    public decimal Shipping { get; set; }

    public DateTimeOffset PlacedAt { get; set; }
}

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Book>().Property(book => book.Price).HasPrecision(10, 2);
        model.Entity<Order>().Property(order => order.Shipping).HasPrecision(10, 2);
        model.Entity<Book>().HasData(
            new Book { Id = 1, Title = "The Quiet Harbor", Price = 18.50m },
            new Book { Id = 2, Title = "Maps of Small Towns", Price = 42.00m });
    }
}

public sealed class ShopOptions
{
    public decimal FreeShippingOver { get; set; }
}

public sealed record PlaceOrder(int BookId);

public sealed record Quote(decimal Price);

// The shipping partner's API, which tests must not call for real.
public sealed class ShippingClient(HttpClient http)
{
    public async Task<decimal> QuoteAsync(int bookId)
    {
        HttpResponseMessage response = await http.PostAsJsonAsync("/quotes", new { bookId });
        response.EnsureSuccessStatusCode();
        Quote? quote = await response.Content.ReadFromJsonAsync<Quote>();
        return quote!.Price;
    }
}

// Emails the staff about low stock every hour: nothing a test should wait for.
public sealed class RestockReminder : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
        }
    }
}
