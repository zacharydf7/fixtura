using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fixtura.Tests.Integration.Hosting;

public sealed class LoggingFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.LogToTestOutput();
}

public sealed class WarningLoggingFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.LogToTestOutput(LogLevel.Warning);
}

public sealed class BackgroundLoggingFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.LogToTestOutput();
        host.Services.Configure(services => services.AddSingleton<LateLog>().AddHostedService<LateLoggingService>());
    }
}

// Stands in for Serilog without writeToProviders: a logger factory of the app's own that ignores the test's provider.
public sealed class OwnLoggerFactoryFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.LogToTestOutput();
        host.Services.Configure(services => services.AddSingleton<ILoggerFactory>(new AppLoggerFactory([])));
    }
}

// Stands in for Serilog with writeToProviders: true: a logger factory of the app's own that passes logs to every provider.
public sealed class ForwardingLoggerFactoryFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.LogToTestOutput();
        host.Services.Configure(services =>
            services.AddSingleton<ILoggerFactory>(provider => new AppLoggerFactory(provider.GetServices<ILoggerProvider>())));
    }
}

// A logger factory of the app's own, as Serilog sets up, passing logs to the providers it's given.
public sealed class AppLoggerFactory(IEnumerable<ILoggerProvider> providers) : LoggerFactory(providers);

// Logs once, after the host has started, as background work does while some test is running, then says it has.
public sealed class LateLoggingService(ILogger<LateLoggingService> logger, LateLog logged) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        logger.LogInformation("Background work ran.");
        logged.Done.SetResult();
    }
}

public sealed class LateLog
{
    public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public class LoggingTests(LoggingFixture api) : IClassFixture<LoggingFixture>
{
    [Fact]
    public async Task LogToTestOutput_ShouldWriteTheAppsLogToTheTestsOutput_WhenTheTestsRequestLogs()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        Assert.Contains("Information Program: Pinged.", Output());
    }

    // The sample's appsettings.json sets Microsoft.AspNetCore to Warning.
    [Fact]
    public async Task LogToTestOutput_ShouldFollowTheAppsLoggingSettings_WhenTheyRaiseACategorysLevel()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Request starting", Output());
    }

    [Fact]
    public async Task LogToTestOutput_ShouldKeepOutALogFromNoTest_WhenWorkRunsWithoutATestsContext()
    {
        var logger = api.Services.GetRequiredService<ILogger<LoggingTests>>();
        Task work;
        using (ExecutionContext.SuppressFlow())
        {
            work = Task.Run(() => logger.LogInformation("Logged by no test."), TestContext.Current.CancellationToken);
        }

        await work;

        Assert.DoesNotContain("Logged by no test.", Output());
    }

    private static string Output() => TestContext.Current.TestOutputHelper!.Output;
}

public class WarningLoggingTests(WarningLoggingFixture api) : IClassFixture<WarningLoggingFixture>
{
    [Fact]
    public async Task LogToTestOutput_ShouldLeaveOutLogsBelowTheMinimum_WhenTheMinimumIsWarning()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Pinged.", TestContext.Current.TestOutputHelper!.Output);
    }
}

// The test builds the host, so without care the background service would inherit this test's context.
public class BackgroundLoggingTests(BackgroundLoggingFixture api) : IClassFixture<BackgroundLoggingFixture>
{
    [Fact]
    public async Task LogToTestOutput_ShouldKeepBackgroundLogsOut_WhenTheTestBuiltTheHost()
    {
        var logged = api.Services.GetRequiredService<LateLog>();

        await logged.Done.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Background work ran.", TestContext.Current.TestOutputHelper!.Output);
    }
}

public class OwnLoggerFactoryTests(OwnLoggerFactoryFixture api) : IClassFixture<OwnLoggerFactoryFixture>
{
    [Fact]
    public void LogToTestOutput_ShouldExplainTheFix_WhenTheAppsLoggerFactoryIgnoresTheTestsProvider()
    {
        var error = Assert.Throws<InvalidOperationException>(() => api.Services);

        Assert.Contains("writeToProviders: true", error.Message);
    }
}

public class ForwardingLoggerFactoryTests(ForwardingLoggerFactoryFixture api) : IClassFixture<ForwardingLoggerFactoryFixture>
{
    [Fact]
    public async Task LogToTestOutput_ShouldWriteTheAppsLog_WhenTheAppsLoggerFactoryPassesLogsOn()
    {
        HttpClient client = await api.CreateClientAsync();

        await client.GetAsync("/ping", TestContext.Current.CancellationToken);

        Assert.Contains("Information Program: Pinged.", TestContext.Current.TestOutputHelper!.Output);
    }
}
