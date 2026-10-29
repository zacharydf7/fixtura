using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Fixtura.Spike.Tests.Composition;
using SampleApp;

namespace Fixtura.Spike.Tests.Swaps;

public class ServiceCollectionSwapsTests
{
    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void Replace_ShouldKeepTheOriginalLifetime_WhenGivenAnyLifetime(ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(IOrderPublisher), typeof(BusOrderPublisher), lifetime));

        services.Replace<IOrderPublisher, NullOrderPublisher>();

        ServiceDescriptor replacement = Assert.Single(services);
        Assert.Equal((typeof(NullOrderPublisher), lifetime), (replacement.ImplementationType, replacement.Lifetime));
    }

    [Fact]
    public void Replace_ShouldLeaveKeyedRegistrationsAlone_WhenReplacingTheUnkeyedService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPaymentGateway, CardGateway>();
        services.AddKeyedSingleton<IPaymentGateway, InvoiceGateway>(PaymentMethods.Invoice);

        services.Replace<IPaymentGateway, CardGateway>();

        Assert.Single(services, descriptor => descriptor.IsKeyedService);
    }

    [Fact]
    public void Replace_ShouldThrow_WhenTheServiceIsNotRegistered()
    {
        var services = new ServiceCollection();

        Assert.Throws<NothingMatchedException>(services.Replace<IOrderPublisher, NullOrderPublisher>);
    }

    [Fact]
    public void ReplaceKeyed_ShouldThrow_WhenTheKeyIsNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPaymentGateway, CardGateway>(PaymentMethods.Card);

        Assert.Throws<NothingMatchedException>(() => services.ReplaceKeyed<IPaymentGateway, InvoiceGateway>("wire"));
    }

    [Fact]
    public void Remove_ShouldThrow_WhenNothingIsRegistered()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<NothingMatchedException>(services.Remove<IOrderPublisher>);

        Assert.Contains("Remove<IOrderPublisher>", error.Message);
    }

    [Fact]
    public void RemoveHostedService_ShouldThrow_WhenTheServiceIsNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddHostedService<OutboxWorker>();

        Assert.Throws<NothingMatchedException>(services.RemoveHostedService<ReportScheduler>);
    }

    [Fact]
    public void RemoveHostedService_ShouldRemoveOnlyThatService_WhenBothFormsAreRegistered()
    {
        var services = new ServiceCollection();
        services.AddHostedService<OutboxWorker>();
        services.AddHostedService(_ => new ReportScheduler());

        services.RemoveHostedService<ReportScheduler>();

        ServiceDescriptor remaining = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Equal(typeof(OutboxWorker), remaining.ImplementationType);
    }
}
