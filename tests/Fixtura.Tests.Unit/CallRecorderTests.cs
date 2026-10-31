using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Tests.Unit;

public interface IMailer<TMessage>
{
    Task SendAsync(TMessage message);
}

// Holds an interface that counts as generic through its parent but has no type parameters of its own.
public static class Feeds<TItem>
{
    public interface IFeed
    {
        Task<TItem?> NextAsync();
    }
}

public interface INamed
{
    string Name();
}

// Every return shape a recorded call answers.
public interface IRates : INamed
{
    Task<decimal> GetRateAsync(string currency);

    ValueTask<int> CountAsync();

    ValueTask<string?> TemplateAsync();

    ValueTask FlushAsync();

    Task NotifyAsync(string to);

    void Ping();
}

public class CallRecorderTests
{
    [Fact]
    public async Task Calls_ShouldRecordEachCallWithItsArguments_WhenTheAppCallsTheService()
    {
        var recorder = new CallRecorder<IRates>();

        await recorder.Service.NotifyAsync("ann@sample.test");
        recorder.Service.Ping();

        Assert.Equal(["NotifyAsync", "Ping"], recorder.Calls.Select(call => call.Method));
        Assert.Equal("ann@sample.test", recorder.Calls[0].Arg<string>("to"));
    }

    [Fact]
    public async Task Calls_ShouldAnswerWithCompletedDefaults_WhenNoResultIsSet()
    {
        var recorder = new CallRecorder<IRates>();

        decimal rate = await recorder.Service.GetRateAsync("EUR");
        int count = await recorder.Service.CountAsync();
        string? template = await recorder.Service.TemplateAsync();
        await recorder.Service.FlushAsync();

        Assert.Equal((0m, 0, null, null), (rate, count, template, recorder.Service.Name()));
    }

    [Fact]
    public async Task Returns_ShouldNameTheMethodAndTypes_WhenTheResultHasTheWrongType()
    {
        var recorder = new CallRecorder<IRates>().Returns(nameof(IRates.GetRateAsync), _ => 1);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.Service.GetRateAsync("EUR"));

        Assert.Equal("The result given for IRates.GetRateAsync is a Int32, but it returns a Decimal.", error.Message);
    }

    [Fact]
    public void Find_ShouldWriteAGenericServiceAsCSharp_WhenTheFixtureRecordsNoSuchService()
    {
        var recorders = new Recorders();

        var error = Assert.Throws<InvalidOperationException>(() => recorders.Find<IMailer<string>>());

        Assert.Contains("host.Services.Record<IMailer<String>>()", error.Message);
    }

    [Fact]
    public async Task Calls_ShouldBeRecorded_WhenTheServiceIsNestedInAGenericType()
    {
        var recorder = new CallRecorder<Feeds<string>.IFeed>();

        string? next = await recorder.Service.NextAsync();

        Assert.Equal((null, "NextAsync"), (next, Assert.Single(recorder.Calls).Method));
    }

    [Fact]
    public void Record_ShouldNameItself_WhenTheAppRegistersNoSuchService()
    {
        var steps = new List<Action<IServiceCollection>>();
        new TestServices(steps, new Recorders()).Record<IRates>();

        var error = Assert.Throws<NothingMatchedException>(() => steps[0](new ServiceCollection()));

        Assert.StartsWith("Record<IRates> matched no registration", error.Message);
    }

    [Fact]
    public async Task Returns_ShouldAnswerWithTheResult_WhenATaskReturningMethodIsGivenAPlainValue()
    {
        var recorder = new CallRecorder<IRates>()
            .Returns(nameof(IRates.GetRateAsync), call => call.Arg<string>("currency") == "EUR" ? 1.1m : 0m)
            .Returns(nameof(IRates.CountAsync), _ => 2)
            .Returns(nameof(INamed.Name), _ => "rates");

        decimal rate = await recorder.Service.GetRateAsync("EUR");
        int count = await recorder.Service.CountAsync();

        Assert.Equal((1.1m, 2, "rates"), (rate, count, recorder.Service.Name()));
    }

    [Fact]
    public void Returns_ShouldListTheMethods_WhenNoMethodHasTheName()
    {
        var recorder = new CallRecorder<IRates>();

        var error = Assert.Throws<ArgumentException>(() => recorder.Returns("GetRate", _ => 1m));

        Assert.Contains("GetRateAsync", error.Message);
    }

    [Fact]
    public void Arg_ShouldListTheParameters_WhenNoParameterHasTheName()
    {
        var recorder = new CallRecorder<IRates>();
        recorder.Service.GetRateAsync("EUR");

        var error = Assert.Throws<ArgumentException>(() => recorder.Calls[0].Arg<string>("code"));

        Assert.Contains("it has currency", error.Message);
    }

    [Fact]
    public void CallRecorder_ShouldExplainItNeedsAnInterface_WhenGivenAClass()
    {
        var error = Assert.Throws<ArgumentException>(() => new CallRecorder<List<string>>());

        Assert.Contains("isn't an interface", error.Message);
    }

    [Fact]
    public async Task WaitForCallAsync_ShouldReturnTheCall_WhenTheAppMakesItLater()
    {
        var recorder = new CallRecorder<IRates>();
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await recorder.Service.NotifyAsync("ann@sample.test");
        }, TestContext.Current.CancellationToken);

        RecordedCall call = await recorder.WaitForCallAsync(call => call.Method == nameof(IRates.NotifyAsync));

        Assert.Equal("ann@sample.test", call.Arg<string>("to"));
    }

    [Fact]
    public async Task Reset_ShouldBringBackTheConfiguredResults_WhenATestChangedThem()
    {
        var recorder = new CallRecorder<IRates>().Returns(nameof(IRates.GetRateAsync), _ => 1.1m);
        recorder.SaveBaseline();
        recorder.Returns(nameof(IRates.GetRateAsync), _ => 9m);
        await recorder.Service.GetRateAsync("EUR");

        recorder.Reset();
        decimal rate = await recorder.Service.GetRateAsync("EUR");

        Assert.Equal((1.1m, 1), (rate, recorder.Calls.Count));
    }
}
