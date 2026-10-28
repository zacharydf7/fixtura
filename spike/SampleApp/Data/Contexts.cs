using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SampleApp.Data;

public sealed class AppUser : IdentityUser;

public sealed class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<AppUser>(options);

// Registered with AddDbContext in a lambda that sets the provider, adds an interceptor and throws without a connection
// string.
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

// Registered with AddDbContextPool.
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

// Registered with AddDbContextFactory.
public sealed class ReportsDbContext(DbContextOptions<ReportsDbContext> options) : DbContext(options)
{
    public DbSet<ReportRun> Runs => Set<ReportRun>();
}

// Registered with AddPooledDbContextFactory.
public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<PageView> PageViews => Set<PageView>();
}

public sealed class Order
{
    public int Id { get; set; }

    public required string Product { get; set; }

    public int Quantity { get; set; }

    public DateTimeOffset? AuditedAt { get; set; }
}

public sealed class Product
{
    public int Id { get; set; }

    public required string Name { get; set; }
}

public sealed class ReportRun
{
    public int Id { get; set; }

    public DateTimeOffset RanAt { get; set; }
}

public sealed class PageView
{
    public int Id { get; set; }

    public required string Path { get; set; }
}
