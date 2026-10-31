using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using SampleApp;

namespace Fixtura.Tests.Integration.Http;

// The standard resilience handler, as service defaults put on every client, would retry the 503 with seconds of delay.
public sealed class RemovedResilienceFixture : TestHost<Program>
{
    public HttpFake Flaky { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services =>
        {
            services.AddHttpClient("flaky");
            services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler());
        });
        host.Http.Resilience = FakedClientResilience.Remove;
        Flaky = host.Http.Fake("flaky").Respond(HttpStatusCode.ServiceUnavailable);
    }
}

// Retries without delay, so keeping them costs the test nothing.
public sealed class KeptResilienceFixture : TestHost<Program>
{
    public HttpFake Flaky { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services => services.AddHttpClient("flaky").AddResilienceHandler("retry", pipeline =>
            pipeline.AddRetry(new HttpRetryStrategyOptions { Delay = TimeSpan.Zero, MaxRetryAttempts = 2 })));
        Flaky = host.Http.Fake("flaky").Respond(HttpStatusCode.ServiceUnavailable);
    }
}

// Retries without delay and with no responses of its own, so each test sets its own sequence.
public sealed class SequencedResilienceFixture : TestHost<Program>
{
    public HttpFake Flaky { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services => services.AddHttpClient("flaky").AddResilienceHandler("retry", pipeline =>
            pipeline.AddRetry(new HttpRetryStrategyOptions { Delay = TimeSpan.Zero, MaxRetryAttempts = 2 })));
        Flaky = host.Http.Fake("flaky");
    }
}

public class SequencedResilienceTests(SequencedResilienceFixture api) : IClassFixture<SequencedResilienceFixture>
{
    [Fact]
    public async Task Respond_ShouldLetTheAppsRetriesRecover_WhenGivenFailuresThenASuccess()
    {
        await api.ResetDataAsync();
        api.Flaky.Respond(HttpMethod.Get, "/status", FakeResponse.Status(HttpStatusCode.ServiceUnavailable),
            FakeResponse.ConnectionFailure(), FakeResponse.Status(HttpStatusCode.OK));
        HttpClient client = api.Services.GetRequiredService<IHttpClientFactory>().CreateClient("flaky");

        HttpResponseMessage response = await client.GetAsync("https://flaky.test/status", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.OK, 3), (response.StatusCode, api.Flaky.Requests.Count));
        api.Flaky.VerifyAllResponsesUsed();
    }

    // Microsoft's default retry predicate leaves HttpClient's own timeout alone, so it reaches the app.
    [Fact]
    public async Task Respond_ShouldReachTheAppWithoutARetry_WhenTheResponseIsATimeout()
    {
        await api.ResetDataAsync();
        api.Flaky.Respond(HttpMethod.Get, "/slow", FakeResponse.Timeout(), FakeResponse.Status(HttpStatusCode.OK));
        HttpClient client = api.Services.GetRequiredService<IHttpClientFactory>().CreateClient("flaky");

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            client.GetAsync("https://flaky.test/slow", TestContext.Current.CancellationToken));

        Assert.Single(api.Flaky.Requests);
    }
}

public class ResilienceTests(RemovedResilienceFixture removed, KeptResilienceFixture kept)
    : IClassFixture<RemovedResilienceFixture>, IClassFixture<KeptResilienceFixture>
{
    [Fact]
    public async Task Resilience_ShouldSendEachRequestOnce_WhenSetToRemove()
    {
        HttpClient client = removed.Services.GetRequiredService<IHttpClientFactory>().CreateClient("flaky");

        HttpResponseMessage response = await client.GetAsync("https://flaky.test/status", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.ServiceUnavailable, 1), (response.StatusCode, removed.Flaky.Requests.Count));
    }

    [Fact]
    public async Task Resilience_ShouldRetry_WhenKeptAsByDefault()
    {
        HttpClient client = kept.Services.GetRequiredService<IHttpClientFactory>().CreateClient("flaky");

        HttpResponseMessage response = await client.GetAsync("https://flaky.test/status", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.ServiceUnavailable, 3), (response.StatusCode, kept.Flaky.Requests.Count));
    }
}
