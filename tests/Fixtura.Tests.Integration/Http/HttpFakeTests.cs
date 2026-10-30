using System.Net;
using System.Net.Http.Json;
using SampleApp;

namespace Fixtura.Tests.Integration.Http;

public sealed class PartnersFixture : TestHost<Program>
{
    public HttpFake Rates { get; private set; } = null!;

    public HttpFake Tracking { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host)
    {
        Rates = host.Http.Fake("rates").Respond(HttpMethod.Get, "/today/EUR", HttpStatusCode.OK, new Rate("EUR", 1.1m));
        Tracking = host.Http.Fake<TrackingClient>().Respond(HttpMethod.Post, "/track", HttpStatusCode.OK, new TrackResponse("Delivered"));
    }
}

public sealed class BlockedPartnersFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Http.Unfaked = UnfakedHttpClients.Block;
}

public sealed class UnregisteredClientFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Http.Fake<UnregisteredClient>();
}

public sealed class UnregisteredClient;

public class HttpFakeTests(PartnersFixture api) : IClassFixture<PartnersFixture>
{
    [Fact]
    public async Task Fake_ShouldAnswerTheAppsNamedClient_WhenTheAppCallsIt()
    {
        HttpClient client = await api.CreateClientAsync();

        Rate? rate = await client.GetFromJsonAsync<Rate>("/rates/EUR", TestContext.Current.CancellationToken);

        Assert.Equal(1.1m, rate?.Value);
    }

    [Fact]
    public async Task Fake_ShouldKeepTheAppsOwnHandlers_WhenItReplacesThePrimaryHandler()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/rates/EUR", TestContext.Current.CancellationToken);

        Assert.Equal("sample-key", api.Rates.Requests[^1].Headers["X-Api-Key"]);
    }

    [Fact]
    public async Task Fake_ShouldAnswerATypedClientAndRecordItsBody_WhenTheAppPostsJson()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.PostAsync("/parcels/123/track", null, TestContext.Current.CancellationToken);

        string status = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("Delivered", "123"), (status, api.Tracking.Requests[^1].Json<TrackRequest>()?.Number));
    }

    [Fact]
    public async Task WithOverrides_ShouldUseItsOwnFake_WhenTheOverrideFakesTheClientAgain()
    {
        await using TestHost<Program> overridden = api.WithOverrides(host =>
            host.Http.Fake("rates").Respond(HttpStatusCode.OK, new Rate("EUR", 2.2m)));
        HttpClient overriddenClient = await overridden.CreateClientAsync();
        HttpClient client = await api.CreateClientAsync();

        Rate? overriddenRate = await overriddenClient.GetFromJsonAsync<Rate>("/rates/EUR", TestContext.Current.CancellationToken);
        Rate? rate = await client.GetFromJsonAsync<Rate>("/rates/EUR", TestContext.Current.CancellationToken);

        Assert.Equal((2.2m, 1.1m), (overriddenRate?.Value, rate?.Value));
    }
}

// A fixture of its own, because the tests change the fixture's fake.
public class HttpFakeResetTests(PartnersFixture api) : IClassFixture<PartnersFixture>
{
    [Fact]
    public async Task ResetDataAsync_ShouldBringBackTheConfiguredResponse_WhenATestChangedIt()
    {
        HttpClient client = await api.CreateClientAsync();
        api.Rates.Respond(HttpMethod.Get, "/today/EUR", HttpStatusCode.ServiceUnavailable);
        await client.GetAsync("/rates/EUR", TestContext.Current.CancellationToken);

        await api.ResetDataAsync();

        HttpResponseMessage response = await client.GetAsync("/rates/EUR", TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode.OK, 1), (response.StatusCode, api.Rates.Requests.Count));
    }
}

public class SharedHttpFakeTests(PartnersFixture api) : IClassFixture<PartnersFixture>
{
    // Block stops only clients without a fake, so the fixture's rates fake still answers through the override.
    [Fact]
    public async Task WithOverrides_ShouldShareTheFixturesFake_WhenTheOverrideDoesNotFakeTheClientAgain()
    {
        await using TestHost<Program> overridden = api.WithOverrides(host => host.Http.Unfaked = UnfakedHttpClients.Block);
        HttpClient client = await overridden.CreateClientAsync();

        HttpResponseMessage response = await client.GetAsync("/rates/EUR", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.OK, 1), (response.StatusCode, api.Rates.Requests.Count));
    }
}

public class BlockedHttpTests(BlockedPartnersFixture api) : IClassFixture<BlockedPartnersFixture>
{
    // The app runs in Development, so its exception page answers with the error.
    [Fact]
    public async Task Unfaked_ShouldStopTheRequestAndNameTheFix_WhenSetToBlock()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.GetAsync("/rates/EUR", TestContext.Current.CancellationToken);

        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Fake it with host.Http.Fake(\"rates\")", page);
    }
}

public class UnregisteredHttpClientTests(UnregisteredClientFixture api) : IClassFixture<UnregisteredClientFixture>
{
    [Fact]
    public void Fake_ShouldThrowNothingMatched_WhenTheAppHasNoSuchTypedClient()
    {
        var error = Assert.ThrowsAny<Exception>(() => api.Services);

        Assert.IsType<NothingMatchedException>(error.GetBaseException());
    }
}
