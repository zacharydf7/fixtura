using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Http;
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

// No HTTP setup at all, so the default applies to every client.
public sealed class UnfakedPartnersFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
    }
}

// The rates client gets a primary handler of its own, standing in for the network, so Allow keeps it in place.
public sealed class AllowedPartnersFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Http.Unfaked = UnfakedHttpClients.Allow;
        host.Options<HttpClientFactoryOptions>("rates", options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = new NetworkStandIn()));
    }
}

public sealed class NetworkStandIn : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new Rate("EUR", 9.9m)) });
}

// A header added through all of the HTTP clients' options, and one through the rates client's own.
public sealed class ClientOptionsFixture : TestHost<Program>
{
    public HttpFake Rates { get; private set; } = null!;

    public HttpFake Tracking { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host)
    {
        Rates = host.Http.Fake("rates").Respond(HttpStatusCode.OK, new Rate("EUR", 1.1m));
        Tracking = host.Http.Fake<TrackingClient>().Respond(HttpStatusCode.OK, new TrackResponse("Delivered"));
        host.Options<HttpClientFactoryOptions>(options =>
            options.HttpClientActions.Add(client => client.DefaultRequestHeaders.Add("X-All", "yes")));
        host.Options<HttpClientFactoryOptions>("rates", options =>
            options.HttpClientActions.Add(client => client.DefaultRequestHeaders.Add("X-Rates", "yes")));
    }
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

// A fixture of its own, so a request another class sent can't satisfy the wait.
public class WaitForRequestTests(PartnersFixture api) : IClassFixture<PartnersFixture>
{
    [Fact]
    public async Task WaitForRequestAsync_ShouldReturnTheRequest_WhenTheAppSendsItAfterAWhile()
    {
        HttpClient client = await api.CreateClientAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            await client.GetAsync("/rates/EUR");
        }, TestContext.Current.CancellationToken);

        FakeHttpRequest request = await api.Rates.WaitForRequestAsync(sent => sent.Uri.AbsolutePath.EndsWith("/EUR"));

        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task WaitForRequestAsync_ShouldNameTheClient_WhenNoMatchingRequestArrives()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.Rates.WaitForRequestAsync(sent => sent.Uri.AbsolutePath.EndsWith("/JPY"), TimeSpan.FromMilliseconds(200)));

        Assert.Contains("'rates' client", error.Message);
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
    // Clients without a fake are blocked, so only the fixture's shared rates fake can answer through the override.
    [Fact]
    public async Task WithOverrides_ShouldShareTheFixturesFake_WhenTheOverrideDoesNotFakeTheClientAgain()
    {
        await using TestHost<Program> overridden = api.WithOverrides(_ => { });
        HttpClient client = await overridden.CreateClientAsync();

        HttpResponseMessage response = await client.GetAsync("/rates/EUR", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.OK, 1), (response.StatusCode, api.Rates.Requests.Count));
    }
}

public class UnfakedHttpTests(UnfakedPartnersFixture api) : IClassFixture<UnfakedPartnersFixture>
{
    // The app runs in Development, so its exception page answers with the error.
    [Fact]
    public async Task Unfaked_ShouldStopTheRequestAndNameTheFix_WhenNothingIsSet()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.GetAsync("/rates/EUR", TestContext.Current.CancellationToken);

        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Fake it with host.Http.Fake(\"rates\")", page);
    }
}

public class AllowedHttpTests(AllowedPartnersFixture api) : IClassFixture<AllowedPartnersFixture>
{
    [Fact]
    public async Task Unfaked_ShouldLetTheRequestThrough_WhenSetToAllow()
    {
        HttpClient client = await api.CreateClientAsync();

        Rate? rate = await client.GetFromJsonAsync<Rate>("/rates/EUR", TestContext.Current.CancellationToken);

        Assert.Equal(9.9m, rate?.Value);
    }
}

public class ClientOptionsTests(ClientOptionsFixture api) : IClassFixture<ClientOptionsFixture>
{
    [Fact]
    public async Task Options_ShouldReachNamedOptions_WhenGivenForAllNamesOrForOne()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/rates/EUR", TestContext.Current.CancellationToken);
        await client.PostAsync("/parcels/123/track", null, TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> rates = api.Rates.Requests[^1].Headers;
        IReadOnlyDictionary<string, string> tracking = api.Tracking.Requests[^1].Headers;
        Assert.Equal(("yes", "yes"), (rates["X-All"], rates["X-Rates"]));
        Assert.Equal(("yes", false), (tracking["X-All"], tracking.ContainsKey("X-Rates")));
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
