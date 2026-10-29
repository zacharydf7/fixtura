using Fixtura.Spike.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Inventory;

namespace Fixtura.Spike.Tests.Swaps;

// The five registration shapes on a plain service collection, so each swap is checked without a host.
public class DbContextSwapsTests
{
    public static TheoryData<Shape> Shapes => [Shape.Plain, Shape.Pooled, Shape.Factory, Shape.PooledFactory];

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ReplaceDbContext_ShouldLeaveOnlyTheReplacement_WhenGivenAnyShape(Shape shape)
    {
        var services = new ServiceCollection();
        Register(services, shape, (_, db) => db.UseSqlServer("Server=unused"));

        services.ReplaceDbContext<ShopContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<ShopContext>));
        Assert.True(Resolve(provider, shape).Database.IsInMemory());
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ReplaceDbContext_ShouldKeepTheAppsInterceptor_WhenAskedToKeepAppOptions(Shape shape)
    {
        var services = new ServiceCollection();
        var interceptor = new CountingInterceptor();
        Register(services, shape, (_, db) => db.UseSqlServer("Server=unused").AddInterceptors(interceptor));

        services.ReplaceDbContext<ShopContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString()), AppDbOptions.Keep);

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        ShopContext db = Resolve(provider, shape);
        db.Items.Add(new ShopItem());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.True(db.Database.IsInMemory());
        Assert.Equal(1, interceptor.Saves);
        Assert.NotNull(db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.ApplicationServiceProvider);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ReplaceDbContext_ShouldDropTheAppsInterceptor_WhenAppOptionsAreDropped(Shape shape)
    {
        var services = new ServiceCollection();
        var interceptor = new CountingInterceptor();
        Register(services, shape, (_, db) => db.UseSqlServer("Server=unused").AddInterceptors(interceptor));

        services.ReplaceDbContext<ShopContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        ShopContext db = Resolve(provider, shape);
        db.Items.Add(new ShopItem());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, interceptor.Saves);
    }

    [Fact]
    public async Task ReplaceDbContext_ShouldExplainWhatToDo_WhenKeptAppOptionsThrow()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ShopContext>((_, _) => throw new InvalidOperationException("No connection string."));

        services.ReplaceDbContext<ShopContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString()), AppDbOptions.Keep);

        await using ServiceProvider provider = services.BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(() => provider.CreateScope().ServiceProvider.GetRequiredService<ShopContext>());
        Assert.Contains("AppDbOptions.Drop", error.Message);
    }

    [Fact]
    public async Task ReplaceDbContext_ShouldLeaveOtherContextsAlone_WhenSwappingOne()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ShopContext>(db => db.UseSqlServer("Server=unused"));
        services.AddDbContext<OtherContext>(db => db.UseSqlServer("Server=unused"));

        services.ReplaceDbContext<ShopContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<OtherContext>().Database.IsSqlServer());
    }

    [Fact]
    public async Task ReplaceDbContexts_ShouldSwapAnInternalContext_WhenGivenItsAssembly()
    {
        var services = new ServiceCollection();
        services.AddInventory("Server=unused");

        services.ReplaceDbContexts(typeof(IInventory).Assembly, db => db.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IInventory>().CountAsync());
    }

    [Fact]
    public void ReplaceDbContexts_ShouldThrow_WhenTheAssemblyHasNoContexts()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ShopContext>(db => db.UseSqlServer("Server=unused"));

        Assert.Throws<NothingMatchedException>(() =>
            services.ReplaceDbContexts(typeof(IInventory).Assembly, db => db.UseInMemoryDatabase("unused")));
    }

    [Fact]
    public void ReplaceDbContext_ShouldThrow_WhenTheContextIsNotRegistered()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<NothingMatchedException>(() =>
            services.ReplaceDbContext<ShopContext>(db => db.UseInMemoryDatabase("unused")));

        Assert.Contains("ReplaceDbContext<ShopContext>", error.Message);
    }

    private static void Register(IServiceCollection services, Shape shape, Action<IServiceProvider, DbContextOptionsBuilder> configure)
    {
        _ = shape switch
        {
            Shape.Plain => services.AddDbContext<ShopContext>(configure),
            Shape.Pooled => services.AddDbContextPool<ShopContext>(configure),
            Shape.Factory => services.AddDbContextFactory<ShopContext>(configure),
            _ => services.AddPooledDbContextFactory<ShopContext>(configure)
        };
    }

    private static ShopContext Resolve(IServiceProvider provider, Shape shape)
    {
        IServiceProvider scoped = provider.CreateScope().ServiceProvider;
        return shape is Shape.Plain or Shape.Pooled
            ? scoped.GetRequiredService<ShopContext>()
            : scoped.GetRequiredService<IDbContextFactory<ShopContext>>().CreateDbContext();
    }
}

public enum Shape
{
    Plain,
    Pooled,
    Factory,
    PooledFactory
}

public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<ShopItem> Items => Set<ShopItem>();
}

public sealed class OtherContext(DbContextOptions<OtherContext> options) : DbContext(options);

public sealed class ShopItem
{
    public int Id { get; set; }
}

public sealed class CountingInterceptor : SaveChangesInterceptor
{
    public int Saves { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Saves++;
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
