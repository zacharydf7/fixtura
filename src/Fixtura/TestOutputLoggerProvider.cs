using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Fixtura;

// Writes the app's logs to the output of the test whose request caused them. Logs that belong to no running test, such
// as a background service's, become xUnit diagnostic messages.
internal sealed class TestOutputLoggerProvider(LogLevel minimumLevel, Action<string> sendDiagnosticMessage) : ILoggerProvider
{
    public static void AddTo(IServiceCollection services, LogLevel minimumLevel, Action<string> sendDiagnosticMessage)
    {
        services.AddLogging(logging => logging.AddProvider(new TestOutputLoggerProvider(minimumLevel, sendDiagnosticMessage)));
        // Registered first, so its middleware runs ahead of every other filter's and no middleware sees the header.
        services.Insert(0, ServiceDescriptor.Singleton(TestRequests.StartupFilter));
    }

    // A category the provider recognizes but never writes, logged once to check that the app's logs reach it.
    private const string ProbeCategory = "Fixtura.LogToTestOutput";

    private bool _probed;

    // Apps can swap in a logger factory of their own, as Serilog does, which may ignore the providers a test adds. The
    // framework's factory always uses them, so only a replaced one gets the probe, which its other sinks see too.
    public static void EnsureReachable(IServiceProvider services)
    {
        ILoggerFactory factory = services.GetRequiredService<ILoggerFactory>();
        if (factory.GetType() == typeof(LoggerFactory))
        {
            return;
        }

        TestOutputLoggerProvider provider = services.GetServices<ILoggerProvider>().OfType<TestOutputLoggerProvider>().Last();
        factory.CreateLogger(ProbeCategory).Log(LogLevel.Critical, "Checking that the app's logs reach the test output.");
        if (!provider._probed)
        {
            throw new InvalidOperationException($"LogToTestOutput can't see the app's logs: its logger factory, "
                + $"{factory.GetType().FullName}, doesn't pass them to the providers a test adds. With Serilog, pass "
                + "writeToProviders: true to UseSerilog or AddSerilog.");
        }
    }

    private LogLevel MinimumLevel { get; } = minimumLevel;

    public ILogger CreateLogger(string categoryName) => new TestOutputLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        ITestContext context = TestRequests.Sender ?? TestContext.Current;
        ITestOutputHelper? output = context.TestStatus == TestEngineStatus.Running ? context.TestOutputHelper : null;
        try
        {
            if (output is not null)
            {
                output.WriteLine(line);
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // The test this work started in has finished.
        }

        sendDiagnosticMessage(line);
    }

    // The app's own logging settings filter first; the minimum level only raises the floor.
    private sealed class TestOutputLogger(string category, TestOutputLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider.MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (category == ProbeCategory)
            {
                provider._probed = true;
                return;
            }

            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line = $"{logLevel} {category}: {formatter(state, exception)}";
            provider.Write(exception is null ? line : line + Environment.NewLine + exception);
        }
    }
}
