using System.Net;
using System.Net.Http.Json;
using Fixtura.Tests.Integration.Users;

namespace Fixtura.Tests.Integration.Hosting;

public class KestrelTests(KestrelFixture api, KestrelJwtFixture jwt) : IClassFixture<KestrelFixture>, IClassFixture<KestrelJwtFixture>
{
    [Fact]
    public async Task BaseAddress_ShouldServeAPlainHttpClient_WhenTheFixtureRunsOnKestrel()
    {
        using var client = new HttpClient { BaseAddress = api.BaseAddress };

        HttpResponseMessage response = await client.GetAsync("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldSignInWithAToken_WhenTheFixtureRunsOnKestrel()
    {
        HttpClient client = await jwt.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }
}

public class BaseAddressTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    [Fact]
    public void BaseAddress_ShouldExplainTheFix_WhenTheFixtureRunsOnTestServer()
    {
        var error = Assert.Throws<InvalidOperationException>(() => api.BaseAddress);

        Assert.Contains("ServerKind.Kestrel", error.Message);
    }
}

public class WithOverridesTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    [Fact]
    public async Task WithOverrides_ShouldReachAnEagerRead_WhenTheParentHostDoesNotSetIt()
    {
        await using TestHost<Program> reports = api.WithOverrides(host => host.Settings(new { Features = new { Reports = true } }));
        HttpClient parentClient = await api.CreateClientAsync();
        HttpClient overrideClient = await reports.CreateClientAsync();
        CancellationToken cancel = TestContext.Current.CancellationToken;

        HttpResponseMessage parent = await parentClient.GetAsync("/reports", cancel);
        HttpResponseMessage overridden = await overrideClient.GetAsync("/reports", cancel);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.OK), (parent.StatusCode, overridden.StatusCode));
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

        HttpClient client = await api.CreateClientAsync();
        HttpResponseMessage response = await client.GetAsync("/carriers", TestContext.Current.CancellationToken);
        Assert.False(api.Resource.Disposed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

// xUnit's own assertions rethrow skips, so these tests catch the skip by hand to check it.
public class SkipGateTests(UnavailableFixture api) : IClassFixture<UnavailableFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSkipWithTheResourcesReason_WhenAResourceIsUnavailable()
    {
        TestSkippedException? skipped = await CatchSkipAsync(() => api.CreateClientAsync());

        Assert.EndsWith("The test resource is unavailable.", skipped?.Message);
    }

    [Fact]
    public async Task WithOverrides_ShouldSkip_WhenTheParentsResourcesAreUnavailable()
    {
        await using TestHost<Program> overridden = api.WithOverrides(_ => { });

        TestSkippedException? skipped = await CatchSkipAsync(() => overridden.CreateClientAsync());

        Assert.EndsWith("The test resource is unavailable.", skipped?.Message);
    }

    [Fact]
    public async Task ResetDataAsync_ShouldSkipWithTheResourcesReason_WhenAResourceIsUnavailable()
    {
        TestSkippedException? skipped = await CatchSkipAsync(() => api.ResetDataAsync());

        Assert.EndsWith("The test resource is unavailable.", skipped?.Message);
    }

    private static async Task<TestSkippedException?> CatchSkipAsync(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (TestSkippedException exception)
        {
            return exception;
        }
    }
}

public class ResourceOrderTests(OrderedResourcesFixture ordered, UnavailableFirstFixture unavailableFirst)
    : IClassFixture<OrderedResourcesFixture>, IClassFixture<UnavailableFirstFixture>
{
    [Fact]
    public void InitializeAsync_ShouldProvisionResourcesInTheOrderAdded_WhenOneBuildsOnAnother()
    {
        List<string> provisioned = ordered.Provisioned;

        Assert.Equal(["server", "migrations"], provisioned);
    }

    [Fact]
    public void InitializeAsync_ShouldNotProvisionLaterResources_WhenAnEarlierOneIsUnavailable()
    {
        List<string> provisioned = unavailableFirst.Provisioned;

        Assert.Empty(provisioned);
    }
}

public class RequiredResourceTests
{
    [Fact]
    public async Task InitializeAsync_ShouldFailTheFixture_WhenARequiredResourceIsUnavailable()
    {
        await using var fixture = new RequiredFixture();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeAsync().AsTask());

        Assert.Contains("required", error.Message);
    }
}

public class FailingShutdownTests
{
    [Fact]
    public async Task DisposeAsync_ShouldStillDisposeTheResources_WhenTheAppFailsToShutDown()
    {
        var api = new FailingShutdownFixture();
        await api.InitializeAsync();
        _ = api.Services;

        await Assert.ThrowsAnyAsync<Exception>(() => api.DisposeAsync().AsTask());

        Assert.True(api.Resource.Disposed);
    }
}
