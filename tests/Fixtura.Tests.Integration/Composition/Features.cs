using SampleApp;

namespace Fixtura.Tests.Integration.Composition;

public sealed class FakeBus : ITestHostFeature
{
    public void Configure(TestHostBuilder host) => host.Services.Replace<IOrderPublisher, NullOrderPublisher>();
}

public sealed class NoReportScheduler : ITestHostFeature
{
    public void Configure(TestHostBuilder host) => host.Services.RemoveHostedService<ReportScheduler>();
}

// Builds on NoReportScheduler; adding both to one host is safe because a feature runs once.
public sealed class QuietBackground : ITestHostFeature
{
    public void Configure(TestHostBuilder host)
    {
        host.Use<NoReportScheduler>();
        host.Services.RemoveHostedService<OutboxWorker>();
    }
}

// Stands for an external client the API talks to, configured as typed options rather than index keys.
public sealed class PartnerCarrier : ITestHostFeature
{
    public void Configure(TestHostBuilder host) =>
        host.Options<ShippingOptions>(shipping => shipping.Carriers.Add(new Carrier { Name = "Partner" }));
}

public sealed class NullOrderPublisher : IOrderPublisher
{
    public Task PublishAsync(OrderPlaced message) => Task.CompletedTask;
}

public sealed class TestCardGateway : IPaymentGateway
{
    public string Name => "Test card";
}

// The three host shapes, built from shared features with no inheritance between them.
public sealed class ApiFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Use<FakeBus>().Use<QuietBackground>();
}

public sealed class ApiWorkerFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Use<FakeBus>().Use<NoReportScheduler>();
}

public sealed class ApiWorkerPartnerFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use<FakeBus>().Use<NoReportScheduler>().Use<PartnerCarrier>();
}
