using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Inventory;

public interface IInventory
{
    Task<int> CountAsync();
}

public static class InventoryServiceCollectionExtensions
{
    // Like many libraries, this one keeps its DbContext internal, so a host can't name the type to swap it.
    public static IServiceCollection AddInventory(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IInventory, StockLedger>();
        return services;
    }
}

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> Items => Set<StockItem>();
}

internal sealed class StockItem
{
    public int Id { get; set; }

    public required string Sku { get; set; }
}

internal sealed class StockLedger(InventoryDbContext db) : IInventory
{
    public Task<int> CountAsync() => db.Items.CountAsync();
}
