using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Sdk;

namespace Fixtura;

/// <summary>
/// What the app logged for each test, for a test to assert on, as the app's own logging settings filter it. Turned on
/// with <c>host.CaptureLogs()</c> or <c>host.FailOnAppErrors()</c>.
/// </summary>
public sealed class TestLogs
{
    private readonly ConditionalWeakTable<ITest, FakeLogCollector> _byTest = new();

    internal TestLogs()
    {
    }

    /// <summary>
    /// What the app logged for the running test, oldest first: while handling the requests the test sent through the
    /// fixture's clients, and in work the test ran itself, such as a service it called.
    /// </summary>
    public IReadOnlyList<FakeLogRecord> Current =>
        TestContext.Current.Test is { } test && _byTest.TryGetValue(test, out FakeLogCollector? logs)
            ? logs.GetSnapshot()
            : [];

    internal FakeLogCollector CollectorFor(ITest test) => _byTest.GetValue(test, _ => new FakeLogCollector());
}
