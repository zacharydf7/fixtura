using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Sdk;

namespace Fixtura;

// Keeps each log for the test it belongs to: the one that sent the request being handled, or the one running the work.
// Logs that belong to no test, such as start-up's or a background service's, aren't kept. An error logged while handling
// a tagged request is also kept with the request, for FailOnAppErrors.
internal sealed class TestLogsProvider(TestLogs logs, string setting)
    : ILoggerProvider, ISupportExternalScope, IProbedLoggerProvider
{
    // A provider per test, over the collector the test shares with an override host, reading this host's own scopes.
    private readonly ConditionalWeakTable<ITest, FakeLoggerProvider> _byTest = new();
    private IExternalScopeProvider? _scopes;

    public string Setting => setting;

    public bool Probed { get; private set; }

    public ILogger CreateLogger(string categoryName) => new TestLogsLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private ILogger LoggerFor(ITest test, string category) => _byTest.GetValue(test, _ =>
    {
        var provider = new FakeLoggerProvider(logs.CollectorFor(test));
        if (_scopes is not null)
        {
            provider.SetScopeProvider(_scopes);
        }

        return provider;
    }).CreateLogger(category);

    // Scopes come from the factory's scope provider, which every record reads.
    private sealed class TestLogsLogger(string category, TestLogsProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (category == LogProbe.Category)
            {
                provider.Probed = true;
                return;
            }

            if ((TestRequests.Sender ?? TestContext.Current).Test is { } test)
            {
                provider.LoggerFor(test, category).Log(logLevel, eventId, state, exception, formatter);
            }

            if (logLevel >= LogLevel.Error)
            {
                TestRequests.ErrorLogger(category, provider._scopes)?.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
