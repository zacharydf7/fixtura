using Alba;
using Alba.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SampleApp;
using SampleApp.Auth;
using SampleApp.Data;

namespace AlbaGate.Tests;

// The sample's API fixture written the way Alba 9 supports today: plain service surgery plus Alba's JWT stub.
public class AlbaApiFixture : IAsyncLifetime
{
    private readonly string _database = Guid.NewGuid().ToString("N");

    public IAlbaHost Host { get; private set; } = null!;

    public JwtSecurityStub Jwt { get; } = new();

    public AppUser Manager { get; private set; } = null!;

    protected virtual IAlbaExtension[] MoreExtensions => [];

    public async ValueTask InitializeAsync()
    {
        Host = await AlbaHost.For<Program>(web => web.ConfigureTestServices(services =>
            {
                // AddDbContext alone breaks pooled and factory shapes, so each swap must repeat the app's shape.
                SwapToInMemory<AppIdentityDbContext>(services, (s, o) => s.AddDbContext<AppIdentityDbContext>(o));
                SwapToInMemory<OrdersDbContext>(services, (s, o) => s.AddDbContext<OrdersDbContext>(o));
                SwapToInMemory<CatalogDbContext>(services, (s, o) => s.AddDbContextPool<CatalogDbContext>(o));
                SwapToInMemory<ReportsDbContext>(services, (s, o) => s.AddDbContextFactory<ReportsDbContext>(o));
                SwapToInMemory<AnalyticsDbContext>(services, (s, o) => s.AddPooledDbContextFactory<AnalyticsDbContext>(o));

                services.RemoveAll<IOrderPublisher>();
                services.AddScoped<IOrderPublisher, NullOrderPublisher>();
                services.PostConfigure<ShippingOptions>(shipping => shipping.Carriers = [new Carrier { Name = "Test" }]);
                RemoveHostedService<OutboxWorker>(services);
                RemoveHostedService<ReportScheduler>(services);
            }), [Jwt, .. MoreExtensions])
            .WithConfiguration("Features:Reports", "true");

        using IServiceScope scope = Host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Manager = new AppUser { UserName = "manager" };
        EnsureSucceeded(await roles.CreateAsync(new IdentityRole("Manager")));
        EnsureSucceeded(await users.CreateAsync(Manager));
        EnsureSucceeded(await users.AddToRoleAsync(Manager, "Manager"));
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();

    // The usual hand-written recipe: drop the options and their configurations, then register the context again.
    private void SwapToInMemory<TContext>(IServiceCollection services,
        Action<IServiceCollection, Action<DbContextOptionsBuilder>> register) where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        register(services, options => options.UseInMemoryDatabase(_database + typeof(TContext).Name));
    }

    private static void RemoveHostedService<TService>(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
            && (descriptor.ImplementationType == typeof(TService)
                || descriptor.ImplementationFactory?.Method.ReturnType == typeof(TService))).ToList())
        {
            services.Remove(descriptor);
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }
}

public sealed class NullOrderPublisher : IOrderPublisher
{
    public Task PublishAsync(OrderPlaced message) => Task.CompletedTask;
}
