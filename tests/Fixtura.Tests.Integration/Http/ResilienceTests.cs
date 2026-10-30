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
