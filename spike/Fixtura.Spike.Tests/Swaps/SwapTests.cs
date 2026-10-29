using Fixtura.Spike.Tests.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleApp;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Spike.Tests.Swaps;

public class SwapTests(SwapFixture api) : IClassFixture<SwapFixture>
{
    [Fact]
    public void EveryContext_ShouldHaveOnlyTheReplacementProvider_WhenSwapped()
    {
        using IServiceScope scope = api.CreateScope();

        List<DbContextOptions> allOptions = scope.ServiceProvider.GetServices<DbContextOptions>().ToList();

        Assert.Equal(6, allOptions.Count);
        Assert.All(allOptions, options => Assert.Equal(["InMemoryOptionsExtension"], ProviderNames(options)));
    }

    [Fact]
    public async Task EveryShape_ShouldResolveAWorkingReplacement_WhenSwapped()
    {
        using IServiceScope scope = api.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        CancellationToken cancel = TestContext.Current.CancellationToken;

        Assert.Multiple(
            () => Assert.True(services.GetRequiredService<AppIdentityDbContext>().Database.IsInMemory(), "plain"),
            () => Assert.True(services.GetRequiredService<OrdersDbContext>().Database.IsInMemory(), "plain, with interceptor"),
            () => Assert.True(services.GetRequiredService<CatalogDbContext>().Database.IsInMemory(), "pooled"),
            () => Assert.True(Create<ReportsDbContext>(services).Database.IsInMemory(), "factory"),
            () => Assert.True(Create<AnalyticsDbContext>(services).Database.IsInMemory(), "pooled factory"));
        Assert.Equal(0, await services.GetRequiredService<IInventory>().CountAsync());
        Assert.Equal(0, await services.GetRequiredService<CatalogDbContext>().Products.CountAsync(cancel));
    }

    [Fact]
    public void HostedServices_ShouldNotBeRegistered_WhenRemovedInBothRegistrationForms()
    {
        IEnumerable<IHostedService> hostedServices = api.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, service => service is OutboxWorker or ReportScheduler);
    }

    [Fact]
    public void Replace_ShouldKeepTheAppsScopedLifetime_WhenGivenOnlyTheImplementation()
    {
        using IServiceScope first = api.CreateScope();
        using IServiceScope second = api.CreateScope();

        var publisher = first.ServiceProvider.GetRequiredService<IOrderPublisher>();

        Assert.IsType<NullOrderPublisher>(publisher);
        Assert.Same(publisher, first.ServiceProvider.GetRequiredService<IOrderPublisher>());
        Assert.NotSame(publisher, second.ServiceProvider.GetRequiredService<IOrderPublisher>());
    }

    [Fact]
    public async Task ReplaceKeyed_ShouldSwapOnlyThatKey_WhenOtherKeysShareTheService()
    {
        HttpClient client = api.CreateClient();
        CancellationToken cancel = TestContext.Current.CancellationToken;

        string card = await client.GetStringAsync("/payments/card", cancel);
        string invoice = await client.GetStringAsync("/payments/invoice", cancel);

        Assert.Equal(["Test card", "Invoicing"], [card, invoice]);
    }

    private static TContext Create<TContext>(IServiceProvider services) where TContext : DbContext =>
        services.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext();

    private static IEnumerable<string> ProviderNames(DbContextOptions options) =>
        options.Extensions.Where(extension => extension.Info.IsDatabaseProvider).Select(extension => extension.GetType().Name);
}
