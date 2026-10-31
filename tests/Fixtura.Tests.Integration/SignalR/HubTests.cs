using System.Net;
using Fixtura.Tests.Integration.Hosting;
using Fixtura.Tests.Integration.Users;
using Microsoft.AspNetCore.SignalR.Client;

namespace Fixtura.Tests.Integration.SignalR;

public sealed class CapturedHubLogsFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.CaptureLogs();
}

public class HubTests(SymmetricJwtFixture api) : IClassFixture<SymmetricJwtFixture>
{
    [Fact]
    public async Task CreateHubConnectionAsync_ShouldReachTheHub_WhenTheHubIsOpenToAnyone()
    {
        await using HubConnection hub = await api.CreateHubConnectionAsync("/hubs/echo");
        HubMessages<string> echoed = hub.Record<string>("Echoed");
        await hub.StartAsync(TestContext.Current.CancellationToken);

        await hub.InvokeAsync("Echo", "hello", TestContext.Current.CancellationToken);

        Assert.Equal("hello", await echoed.WaitForAsync());
    }

    // Recorded before StartAsync, so the message the hub sends as the client connects is kept too.
    [Fact]
    public async Task CreateHubConnectionAsync_ShouldSignTheUserIn_WhenTheHubNeedsAUser()
    {
        await using HubConnection hub = await api.CreateHubConnectionAsync("/hubs/greeting", TestUser.Named("ann"));
        HubMessages<string> greeted = hub.Record<string>("Greeted");

        await hub.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ann", await greeted.WaitForAsync());
    }

    [Fact]
    public async Task CreateHubConnectionAsync_ShouldBeTurnedAway_WhenTheHubNeedsAUserAndNoneIsGiven()
    {
        await using HubConnection hub = await api.CreateHubConnectionAsync("/hubs/greeting");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            hub.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    // SignalR disposes the handler it gets for each start, so a restart needs one the first start didn't dispose.
    [Fact]
    public async Task CreateHubConnectionAsync_ShouldReachTheHubAgain_WhenTheConnectionRestarts()
    {
        await using HubConnection hub = await api.CreateHubConnectionAsync("/hubs/echo");
        HubMessages<string> echoed = hub.Record<string>("Echoed");
        await hub.StartAsync(TestContext.Current.CancellationToken);
        await hub.StopAsync(TestContext.Current.CancellationToken);

        await hub.StartAsync(TestContext.Current.CancellationToken);
        await hub.InvokeAsync("Echo", "again", TestContext.Current.CancellationToken);

        Assert.Equal("again", await echoed.WaitForAsync());
    }

    [Fact]
    public async Task WaitForAsync_ShouldNameTheMethod_WhenNoMatchingMessageArrives()
    {
        await using HubConnection hub = await api.CreateHubConnectionAsync("/hubs/echo");
        HubMessages<string> echoed = hub.Record<string>("Echoed");
        await hub.StartAsync(TestContext.Current.CancellationToken);
        await hub.InvokeAsync("Echo", "hello", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            echoed.WaitForAsync(text => text == "goodbye", TimeSpan.FromMilliseconds(200)));

        Assert.Equal("The hub sent no matching Echoed message.", error.Message);
    }
}

public class HubLogsTests(CapturedHubLogsFixture api) : IClassFixture<CapturedHubLogsFixture>
{
    [Fact]
    public async Task CreateHubConnectionAsync_ShouldTieTheHubsLogsToTheTest_WhenTheFixtureCapturesLogs()
    {
        await using HubConnection hub = await api.CreateHubConnectionAsync("/hubs/echo");
        HubMessages<string> echoed = hub.Record<string>("Echoed");
        await hub.StartAsync(TestContext.Current.CancellationToken);

        await hub.InvokeAsync("Echo", "hello", TestContext.Current.CancellationToken);
        await echoed.WaitForAsync();

        Assert.Contains(api.Logs.Current, log => log.Message == "Echoing hello.");
    }
}

public class KestrelHubTests(KestrelJwtFixture api) : IClassFixture<KestrelJwtFixture>
{
    [Fact]
    public async Task CreateHubConnectionAsync_ShouldSignTheUserIn_WhenTheAppRunsOnKestrel()
    {
        await using HubConnection hub = await api.CreateHubConnectionAsync("/hubs/greeting", TestUser.Named("ann"));
        HubMessages<string> greeted = hub.Record<string>("Greeted");

        await hub.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ann", await greeted.WaitForAsync());
    }
}
