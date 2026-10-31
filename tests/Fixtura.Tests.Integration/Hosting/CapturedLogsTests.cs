using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Fixtura.Tests.Integration.Hosting;

public sealed class CapturedLogsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.CaptureLogs();
}

public sealed class CapturedBackgroundLogsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.CaptureLogs();
        host.Services.Configure(services => services.AddSingleton<LateLog>().AddHostedService<LateLoggingService>());
    }
}

// Stands in for Serilog without writeToProviders: a logger factory of the app's own that ignores the test's provider.
public sealed class CapturedOwnLoggerFactoryFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.CaptureLogs();
        host.Services.Configure(services => services.AddSingleton<ILoggerFactory>(new AppLoggerFactory([])));
    }
}

public sealed class KestrelAppErrorsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Server = ServerKind.Kestrel;
        host.FailOnAppErrors();
    }
}

public sealed class AppErrorsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.FailOnAppErrors();
}

public sealed class ExpectedAppErrorsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.FailOnAppErrors(except: error => error.Message == "Swallowed an error.");
}

public class CapturedLogsTests(CapturedLogsFixture api) : IClassFixture<CapturedLogsFixture>
{
    [Fact]
    public async Task Logs_ShouldHoldTheRequestsLogs_WhenTheTestSentIt()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        FakeLogRecord pinged = Assert.Single(api.Logs.Current, log => log.Message == "Pinged.");
        Assert.Equal(("Program", LogLevel.Information), (pinged.Category, pinged.Level));
    }

    [Fact]
    public async Task Logs_ShouldKeepTheRequestsScope_WhenTheAppLogsWithinIt()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        FakeLogRecord pinged = Assert.Single(api.Logs.Current, log => log.Message == "Pinged.");
        Assert.Contains(pinged.Scopes, scope => scope?.ToString()?.Contains("RequestPath:/ping") == true);
    }

    // The override has a logger factory, and so a scope provider, of its own, but shares the fixture's logs.
    [Fact]
    public async Task Logs_ShouldKeepEachHostsScopes_WhenATestUsesTheFixtureAndAnOverride()
    {
        HttpClient client = await api.CreateClientAsync();
        await using TestHost<Program> overridden = api.WithOverrides(_ => { });
        HttpClient overriddenClient = await overridden.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);
        await overriddenClient.GetAsync("/ping", TestContext.Current.CancellationToken);

        List<FakeLogRecord> pinged = [.. api.Logs.Current.Where(log => log.Message == "Pinged.")];
        Assert.Equal(2, pinged.Count);
        Assert.All(pinged, log => Assert.Contains(log.Scopes, scope => scope?.ToString()?.Contains("RequestPath:/ping") == true));
    }

    // Whichever row runs second would see the first row's marker if the tests shared their logs.
    [Theory]
    [InlineData("first", "second")]
    [InlineData("second", "first")]
    public void Logs_ShouldHoldOnlyTheRunningTestsLogs_WhenEachTestLogs(string mine, string theirs)
    {
        var logger = api.Services.GetRequiredService<ILogger<CapturedLogsTests>>();

        logger.LogWarning("Marker {Name}.", mine);

        Assert.Contains(api.Logs.Current, log => log.Message == $"Marker {mine}.");
        Assert.DoesNotContain(api.Logs.Current, log => log.Message == $"Marker {theirs}.");
    }

    [Fact]
    public void Logs_ShouldHoldTheTestsOwnWork_WhenTheTestCallsTheAppsServices()
    {
        var logger = api.Services.GetRequiredService<ILogger<CapturedLogsTests>>();

        logger.LogWarning("Called by the test.");

        Assert.Contains(api.Logs.Current, log => log.Message == "Called by the test.");
    }

    // The sample's appsettings.json sets Microsoft.AspNetCore to Warning.
    [Fact]
    public async Task Logs_ShouldFollowTheAppsLoggingSettings_WhenTheyRaiseACategorysLevel()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(api.Logs.Current,
            log => log.Category?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Logs_ShouldLeaveOutWorkForNoTest_WhenItRunsWithoutATestsContext()
    {
        var logger = api.Services.GetRequiredService<ILogger<CapturedLogsTests>>();
        Task work;
        using (ExecutionContext.SuppressFlow())
        {
            work = Task.Run(() => logger.LogWarning("Logged by no test."), TestContext.Current.CancellationToken);
        }

        await work;

        Assert.DoesNotContain(api.Logs.Current, log => log.Message == "Logged by no test.");
    }
}

// The test builds the host, so without care start-up and the background service would log in this test's context.
public class CapturedBackgroundLogsTests(CapturedBackgroundLogsFixture api) : IClassFixture<CapturedBackgroundLogsFixture>
{
    [Fact]
    public async Task Logs_ShouldLeaveOutStartupAndBackgroundLogs_WhenTheTestBuiltTheHost()
    {
        var logged = api.Services.GetRequiredService<LateLog>();

        await logged.Done.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(api.Logs.Current, log => log.Message == "Background work ran.");
        Assert.DoesNotContain(api.Logs.Current, log => log.Category == "Microsoft.Hosting.Lifetime");
    }
}

public class CapturedOwnLoggerFactoryTests(CapturedOwnLoggerFactoryFixture api) : IClassFixture<CapturedOwnLoggerFactoryFixture>
{
    [Fact]
    public void CaptureLogs_ShouldExplainTheFix_WhenTheAppsLoggerFactoryIgnoresTheTestsProvider()
    {
        var error = Assert.Throws<InvalidOperationException>(() => api.Services);

        Assert.StartsWith("CaptureLogs can't see the app's logs", error.Message);
        Assert.Contains("writeToProviders: true", error.Message);
    }
}

public class KestrelAppErrorsTests(KestrelAppErrorsFixture api) : IClassFixture<KestrelAppErrorsFixture>
{
    [Fact]
    public async Task FailOnAppErrors_ShouldFailTheRequest_WhenTheAppRunsOnKestrel()
    {
        HttpClient client = await api.CreateClientAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetAsync("/swallowed", TestContext.Current.CancellationToken));

        Assert.Contains("Error Program: Swallowed an error.", error.Message);
    }
}

public class UncapturedLogsTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    [Fact]
    public void Logs_ShouldNameTheSetting_WhenTheFixtureCapturesNoLogs()
    {
        var error = Assert.Throws<InvalidOperationException>(() => api.Logs);

        Assert.Contains("host.CaptureLogs()", error.Message);
    }
}

public class AppErrorsTests(AppErrorsFixture api) : IClassFixture<AppErrorsFixture>
{
    [Fact]
    public async Task FailOnAppErrors_ShouldFailTheRequest_WhenTheAppLogsAnErrorWhileHandlingIt()
    {
        HttpClient client = await api.CreateClientAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetAsync("/swallowed", TestContext.Current.CancellationToken));

        Assert.Contains("Error Program: Swallowed an error.", error.Message);
        Assert.Contains("The cache is down.", error.Message);
    }

    [Fact]
    public async Task FailOnAppErrors_ShouldFailTheRequest_WhenTheTestSendsThroughAHandler()
    {
        using HttpMessageHandler handler = await api.CreateHandlerAsync();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetAsync("/swallowed", TestContext.Current.CancellationToken));

        Assert.Contains("Error Program: Swallowed an error.", error.Message);
    }

    [Fact]
    public async Task FailOnAppErrors_ShouldLetTheRequestThrough_WhenTheAppLogsNoError()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task FailOnAppErrors_ShouldCaptureLogs_WhenItsTheOnlySetting()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        Assert.Contains(api.Logs.Current, log => log.Message == "Pinged.");
    }
}

public class ExpectedAppErrorsTests(ExpectedAppErrorsFixture api) : IClassFixture<ExpectedAppErrorsFixture>
{
    [Fact]
    public async Task FailOnAppErrors_ShouldLetTheRequestThrough_WhenExceptAcceptsTheError()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.GetAsync("/swallowed", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains(api.Logs.Current, log => log.Level == LogLevel.Error);
    }
}
