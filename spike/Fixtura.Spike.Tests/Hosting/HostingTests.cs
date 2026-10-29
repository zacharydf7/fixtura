using System.Net;
using System.Net.Http.Json;
using Fixtura.Spike.EntityFrameworkCore;
using Fixtura.Spike.Tests.Users;
using Fixtura.Spike.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;

namespace Fixtura.Spike.Tests.Hosting;

public class KestrelTests(KestrelFixture api) : IClassFixture<KestrelFixture>
{
    [Fact]
    public async Task BaseAddress_ShouldServeAPlainHttpClient_WhenTheFixtureRunsOnKestrel()
    {
        using var client = new HttpClient { BaseAddress = api.BaseAddress };

        HttpResponseMessage response = await client.GetAsync("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class KestrelSignInTests(KestrelJwtFixture jwt, KestrelCookieFixture cookie)
    : IClassFixture<KestrelJwtFixture>, IClassFixture<KestrelCookieFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignInWithAToken_WhenTheFixtureRunsOnKestrel()
    {
        HttpClient client = await jwt.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldSignInWithACookie_WhenTheFixtureRunsOnKestrel()
    {
        HttpClient client = await cookie.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }
}

public class WithOverridesTests(OverridableFixture api) : IClassFixture<OverridableFixture>
{
    [Fact]
    public async Task WithOverrides_ShouldReachAnEagerRead_WhenTheParentHostDoesNotSetIt()
    {
        await using TestHost<Program> reports = api.WithOverrides(host => host.Settings(new { Features = new { Reports = true } }));
        CancellationToken cancel = TestContext.Current.CancellationToken;

        HttpResponseMessage parent = await (await api.CreateClientAsync()).GetAsync("/reports", cancel);
        HttpResponseMessage overridden = await (await reports.CreateClientAsync()).GetAsync("/reports", cancel);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.OK), (parent.StatusCode, overridden.StatusCode));
    }

    [Fact]
    public async Task WithOverrides_ShouldShareTheParentsDatabases_WhenTheyAreNamedWithUniqueName()
    {
        await using TestHost<Program> overridden = api.WithOverrides(host =>
            host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders"))));
        using (IServiceScope scope = api.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Orders.Add(new Order { Product = "Shared", Quantity = 1 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using IServiceScope other = overridden.CreateScope();
        bool found = await other.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders
            .AnyAsync(order => order.Product == "Shared", TestContext.Current.CancellationToken);

        Assert.True(found);
    }

    [Fact]
    public void BaseAddress_ShouldExplainTheFix_WhenTheFixtureRunsOnTestServer()
    {
        var error = Assert.Throws<InvalidOperationException>(() => api.BaseAddress);

        Assert.Contains("ServerKind.Kestrel", error.Message);
    }

    [Fact]
    public void WithOverrides_ShouldRefuseNewResources_WhenTheParentOwnsThem()
    {
        Assert.Throws<InvalidOperationException>(() => api.WithOverrides(host => host.AddResource(new UnavailableResource())));
    }
}

public class OverrideDisposalTests(RecordingResourceFixture api) : IClassFixture<RecordingResourceFixture>
{
    [Fact]
    public async Task WithOverrides_ShouldLeaveTheParentsResources_WhenTheOverrideIsDisposed()
    {
        TestHost<Program> overridden = api.WithOverrides(_ => { });
        _ = overridden.Services;

        await overridden.DisposeAsync();

        HttpResponseMessage response = await (await api.CreateClientAsync()).GetAsync("/carriers", TestContext.Current.CancellationToken);
        Assert.False(api.Resource.Disposed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class UnavailableOverridesTests(UnavailableFixture api) : IClassFixture<UnavailableFixture>
{
    [Fact]
    public async Task WithOverrides_ShouldSkip_WhenTheParentsResourcesAreUnavailable()
    {
        await using TestHost<Program> overridden = api.WithOverrides(_ => { });
        TestSkippedException? skipped = null;

        // xUnit's own assertions rethrow skips, so the skip is caught by hand.
        try
        {
            await overridden.CreateClientAsync();
        }
        catch (TestSkippedException exception)
        {
            skipped = exception;
        }

        Assert.EndsWith("The test resource is unavailable.", skipped?.Message);
    }
}
