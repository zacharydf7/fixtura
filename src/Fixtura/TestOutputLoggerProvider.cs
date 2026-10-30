using Microsoft.AspNetCore.TestHost;
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

        // A request then runs in its test's context, which is how a log finds the test's output.
        services.Configure<TestServerOptions>(options => options.PreserveExecutionContext = true);
    }

    private LogLevel MinimumLevel { get; } = minimumLevel;

    public ILogger CreateLogger(string categoryName) => new TestOutputLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        ITestContext context = TestContext.Current;
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
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line = $"{logLevel} {category}: {formatter(state, exception)}";
            provider.Write(exception is null ? line : line + Environment.NewLine + exception);
        }
    }
}
