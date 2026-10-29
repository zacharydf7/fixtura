using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Alba;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleApp;
using SampleApp.Auth;
using SampleApp.Data;
using SampleApp.Inventory;

namespace AlbaGate.Tests;

// Each test records one thing the mission needs and what Alba 9 does with it today.
public class AlbaGateTests(AlbaApiFixture api) : IClassFixture<AlbaApiFixture>
{
    [Fact]
    public async Task Scenario_ShouldPassTheAppsAccountCheck_WhenClaimsCarryTheSeededUser()
    {
        await api.Host.Scenario(scenario =>
        {
            scenario.WithClaim(new Claim("sub", api.Manager.Id));
            scenario.WithClaim(new Claim(SampleClaims.SecurityStamp, api.Manager.SecurityStamp!));
            scenario.WithClaim(new Claim("role", "Manager"));
            scenario.Post.Json(new PlaceOrderRequest("Lamp", 2)).ToUrl("/orders");
            scenario.StatusCodeShouldBe(HttpStatusCode.Created);
        });
    }

    [Fact]
    public async Task Scenario_ShouldBeRejectedByTheAppsAccountCheck_WhenTheStampIsStale()
    {
        await api.Host.Scenario(scenario =>
        {
            scenario.WithClaim(new Claim("sub", api.Manager.Id));
            scenario.WithClaim(new Claim(SampleClaims.SecurityStamp, "stale"));
            scenario.WithClaim(new Claim("role", "Manager"));
            scenario.Post.Json(new PlaceOrderRequest("Lamp", 2)).ToUrl("/orders");
            scenario.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    [Fact]
    public async Task PlainHttpClient_ShouldBeAnonymous_WhenTheJwtStubIsRegistered()
    {
        HttpClient client = api.Host.Server.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithConfiguration_ShouldReachAnEagerRead_WhenSetBeforeStart()
    {
        await api.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/reports");
            scenario.StatusCodeShouldBeOk();
        });
    }

    [Fact]
    public async Task PostConfigure_ShouldReplaceTheWholeArray_WhenWrittenByHand()
    {
        string[]? carriers = await api.Host.Server.CreateClient()
            .GetFromJsonAsync<string[]>("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(["Test"], carriers!);
    }

    [Fact]
    public void HandWrittenRemoval_ShouldStopBothHostedServices_WhenMatchedByTypeAndFactoryReturnType()
    {
        IEnumerable<IHostedService> hostedServices = api.Host.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, service => service is OutboxWorker or ReportScheduler);
    }

    [Fact]
    public void HandWrittenSwap_ShouldMoveEveryPublicContextToInMemory_WhenUsingTheUsualRecipe()
    {
        using IServiceScope scope = api.Host.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;

        Assert.Multiple(
            () => Assert.True(services.GetRequiredService<AppIdentityDbContext>().Database.IsInMemory(), "plain"),
            () => Assert.True(services.GetRequiredService<OrdersDbContext>().Database.IsInMemory(), "plain + interceptor"),
            () => Assert.True(services.GetRequiredService<CatalogDbContext>().Database.IsInMemory(), "pooled"),
            () => Assert.True(services.GetRequiredService<IDbContextFactory<ReportsDbContext>>().CreateDbContext()
                .Database.IsInMemory(), "factory"),
            () => Assert.True(services.GetRequiredService<IDbContextFactory<AnalyticsDbContext>>().CreateDbContext()
                .Database.IsInMemory(), "pooled factory"));
    }

    [Fact]
    public async Task InternalContext_ShouldStillUseSqlServer_WhenTheHostCannotNameItsType()
    {
        using IServiceScope scope = api.Host.Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventory>();

        Exception error = await Assert.ThrowsAnyAsync<Exception>(inventory.CountAsync);

        // On Linux the provider calls "server not found" transient and wraps the SqlException.
        Assert.IsType<SqlException>(error as SqlException ?? error.InnerException);
    }

    [Fact]
    public async Task SwappedOrdersContext_ShouldLoseTheAppsInterceptor_WhenTheOriginalLambdaIsDropped()
    {
        using IServiceScope scope = api.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var order = new Order { Product = "Desk", Quantity = 1 };

        db.Orders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Null(order.AuditedAt);
    }
}
