using System.Net;
using System.Net.Http.Json;
using Fixtura.Tests.Integration.Hosting;
using Microsoft.EntityFrameworkCore;
using SampleApp;
using SampleApp.Auth;
using SampleApp.Data;

namespace Fixtura.Tests.Integration.Data;

// The order publisher reaches a message broker in production; here it records what the app published.
public sealed class RecordedPublisherFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services
            .ReplaceDbContext<AppIdentityDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Identity")))
            .ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")))
            .Record<IOrderPublisher>();
        host.Users.SeedWith<SampleAppUserSeeder>();
    }
}

public class RecordedServiceTests(RecordedPublisherFixture api) : IClassFixture<RecordedPublisherFixture>
{
    [Fact]
    public async Task Record_ShouldKeepWhatTheAppPublished_WhenARequestCallsTheService()
    {
        await api.ResetDataAsync();
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Manager"));

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new PlaceOrderRequest("Tea", 3),
            TestContext.Current.CancellationToken);

        RecordedCall published = Assert.Single(api.Recorder<IOrderPublisher>().Calls);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("PublishAsync", "Tea", 3),
            (published.Method, published.Arg<OrderPlaced>("message").Product, published.Arg<OrderPlaced>("message").Quantity));
    }

    [Fact]
    public async Task ResetDataAsync_ShouldClearTheRecordedCalls_WhenATestMadeSome()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Manager"));
        await client.PostAsJsonAsync("/orders", new PlaceOrderRequest("Tea", 1), TestContext.Current.CancellationToken);

        await api.ResetDataAsync();

        Assert.Empty(api.Recorder<IOrderPublisher>().Calls);
    }
}

public class OverriddenRecorderTests(RecordedPublisherFixture api) : IClassFixture<RecordedPublisherFixture>
{
    [Fact]
    public async Task Recorder_ShouldBeTheFixtures_WhenAnOverrideDoesNotRecordTheServiceAgain()
    {
        await api.ResetDataAsync();
        await using TestHost<Program> overridden = api.WithOverrides(_ => { });
        HttpClient client = await overridden.CreateClientAsync(TestUser.Named("ann").WithRoles("Manager"));

        await client.PostAsJsonAsync("/orders", new PlaceOrderRequest("Tea", 1), TestContext.Current.CancellationToken);

        Assert.Same(api.Recorder<IOrderPublisher>(), overridden.Recorder<IOrderPublisher>());
        Assert.Single(api.Recorder<IOrderPublisher>().Calls);
    }

    [Fact]
    public async Task Recorder_ShouldBeTheOverridesOwn_WhenTheOverrideRecordsTheServiceAgain()
    {
        await api.ResetDataAsync();
        await using TestHost<Program> overridden = api.WithOverrides(host => host.Services.Record<IOrderPublisher>());
        HttpClient client = await overridden.CreateClientAsync(TestUser.Named("ann").WithRoles("Manager"));

        await client.PostAsJsonAsync("/orders", new PlaceOrderRequest("Tea", 1), TestContext.Current.CancellationToken);

        Assert.NotSame(api.Recorder<IOrderPublisher>(), overridden.Recorder<IOrderPublisher>());
        Assert.Equal((1, 0), (overridden.Recorder<IOrderPublisher>().Calls.Count, api.Recorder<IOrderPublisher>().Calls.Count));
    }
}

public class UnrecordedServiceTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    [Fact]
    public void Recorder_ShouldNameTheSetup_WhenTheFixtureRecordsNoSuchService()
    {
        var error = Assert.Throws<InvalidOperationException>(() => api.Recorder<IOrderPublisher>());

        Assert.Contains("host.Services.Record<IOrderPublisher>()", error.Message);
    }
}
