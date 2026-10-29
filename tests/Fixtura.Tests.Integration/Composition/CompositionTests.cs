using System.Net.Http.Json;
using Fixtura.Tests.Integration.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleApp;

[assembly: AssemblyFixture(typeof(ApiWorkerPartnerFixture))]

namespace Fixtura.Tests.Integration.Composition;

public class ClassFixtureTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public void HostedServices_ShouldAllBeRemoved_WhenTheShapeIsApiOnly()
    {
        IEnumerable<IHostedService> hostedServices = api.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, service => service is OutboxWorker or ReportScheduler);
    }

    [Fact]
    public void Replace_ShouldKeepTheAppsScopedLifetime_WhenGivenOnlyTheImplementation()
    {
        using IServiceScope first = api.CreateScope();
        using IServiceScope second = api.CreateScope();

        var publisher = first.ServiceProvider.GetRequiredService<IOrderPublisher>();

        Assert.IsType<NullOrderPublisher>(publisher);
        Assert.Same(publisher, first.ServiceProvider.GetRequiredService<IOrderPublisher>());
        Assert.NotSame(publisher, second.ServiceProvider.GetRequiredService<IOrderPublisher>());
    }
}

[CollectionDefinition(Name)]
public class ApiWorkerCollection : ICollectionFixture<ApiWorkerFixture>
{
    public const string Name = "api-worker";
}

[Collection(ApiWorkerCollection.Name)]
public class CollectionFixtureTests(ApiWorkerFixture api)
{
    [Fact]
    public void Worker_ShouldStillRun_WhenTheShapeIncludesIt()
    {
        IEnumerable<IHostedService> hostedServices = api.Services.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service => service is OutboxWorker);
        Assert.DoesNotContain(hostedServices, service => service is ReportScheduler);
    }
}

public class AssemblyFixtureTests(ApiWorkerPartnerFixture api)
{
    [Fact]
    public async Task Carriers_ShouldIncludeThePartner_WhenSharedAsAnAssemblyFixture()
    {
        HttpClient client = await api.CreateClientAsync();

        string[]? carriers = await client.GetFromJsonAsync<string[]>("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(["Ground", "Air", "Freight", "Partner"], carriers!);
    }
}

public sealed class KeyedSwapFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Services.ReplaceKeyed<IPaymentGateway, TestCardGateway>(PaymentMethods.Card);
}

public class KeyedSwapTests(KeyedSwapFixture api) : IClassFixture<KeyedSwapFixture>
{
    [Fact]
    public async Task ReplaceKeyed_ShouldSwapOnlyThatKey_WhenOtherKeysShareTheService()
    {
        HttpClient client = await api.CreateClientAsync();
        CancellationToken cancel = TestContext.Current.CancellationToken;

        string card = await client.GetStringAsync("/payments/card", cancel);
        string invoice = await client.GetStringAsync("/payments/invoice", cancel);

        Assert.Equal(["Test card", "Invoicing"], [card, invoice]);
    }
}

public sealed class RenamedServiceFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Services.Remove<TestCardGateway>();
}

public class NothingMatchedTests(RenamedServiceFixture api) : IClassFixture<RenamedServiceFixture>
{
    [Fact]
    public void Services_ShouldFailLoudly_WhenASwapMatchesNothingInTheApp()
    {
        var error = Assert.ThrowsAny<Exception>(() => api.Services);

        Assert.IsType<NothingMatchedException>(error.GetBaseException());
    }
}
