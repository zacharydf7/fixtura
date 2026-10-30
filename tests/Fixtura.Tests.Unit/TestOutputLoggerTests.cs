using Microsoft.Extensions.Logging;

namespace Fixtura.Tests.Unit;

public class TestOutputLoggerTests
{
    [Fact]
    public async Task Log_ShouldSendADiagnosticMessage_WhenNoTestIsRunning()
    {
        var messages = new List<string>();
        ILogger logger = new TestOutputLoggerProvider(LogLevel.Information, messages.Add).CreateLogger("Worker");
        Task work;
        using (ExecutionContext.SuppressFlow())
        {
            work = Task.Run(() => logger.LogWarning("Queue is empty."), TestContext.Current.CancellationToken);
        }

        await work;

        Assert.Equal("Warning Worker: Queue is empty.", Assert.Single(messages));
    }

    [Fact]
    public void Log_ShouldWriteToTheTestsOutput_WhenATestIsRunning()
    {
        var messages = new List<string>();
        ILogger logger = new TestOutputLoggerProvider(LogLevel.Information, messages.Add).CreateLogger("Worker");

        logger.LogInformation("Order placed.");

        Assert.Contains("Information Worker: Order placed.", TestContext.Current.TestOutputHelper!.Output);
        Assert.Empty(messages);
    }

    [Fact]
    public void Log_ShouldSkipTheLog_WhenItIsBelowTheMinimum()
    {
        var messages = new List<string>();
        ILogger logger = new TestOutputLoggerProvider(LogLevel.Warning, messages.Add).CreateLogger("Worker");

        logger.LogInformation("Order placed.");

        Assert.DoesNotContain("Order placed.", TestContext.Current.TestOutputHelper!.Output);
    }
}
