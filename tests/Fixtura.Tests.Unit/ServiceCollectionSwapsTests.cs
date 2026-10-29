using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura.Tests.Unit;

public class ServiceCollectionSwapsTests
{
    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void Replace_ShouldKeepTheOriginalLifetime_WhenGivenAnyLifetime(ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(IClock), typeof(SystemClock), lifetime));

        services.Replace<IClock, FixedClock>();

        ServiceDescriptor replacement = Assert.Single(services);
        Assert.Equal((typeof(FixedClock), lifetime), (replacement.ImplementationType, replacement.Lifetime));
    }

    [Fact]
    public void Replace_ShouldReplaceEveryRegistration_WhenTheServiceIsRegisteredTwice()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IClock, SystemClock>();

        services.Replace<IClock, FixedClock>();

        Assert.Equal(typeof(FixedClock), Assert.Single(services).ImplementationType);
    }

    [Fact]
    public void Replace_ShouldLeaveKeyedRegistrationsAlone_WhenReplacingTheUnkeyedService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddKeyedSingleton<IClock, SystemClock>("utc");

        services.Replace<IClock, FixedClock>();

        Assert.Single(services, descriptor => descriptor.IsKeyedService);
    }

    [Fact]
    public void Replace_ShouldThrow_WhenTheServiceIsNotRegistered()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<NothingMatchedException>(services.Replace<IClock, FixedClock>);

        Assert.Contains("Replace<IClock>", error.Message);
    }

    [Fact]
    public void ReplaceKeyed_ShouldReplaceOnlyThatKey_WhenOtherKeysShareTheService()
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<IClock, SystemClock>("utc");
        services.AddKeyedScoped<IClock, SystemClock>("local");

        services.ReplaceKeyed<IClock, FixedClock>("utc");

        ServiceDescriptor utc = services.Single(descriptor => Equals(descriptor.ServiceKey, "utc"));
        ServiceDescriptor local = services.Single(descriptor => Equals(descriptor.ServiceKey, "local"));
        Assert.Equal((typeof(FixedClock), ServiceLifetime.Scoped), (utc.KeyedImplementationType, utc.Lifetime));
        Assert.Equal(typeof(SystemClock), local.KeyedImplementationType);
    }

    [Fact]
    public void ReplaceKeyed_ShouldThrow_WhenTheKeyIsNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IClock, SystemClock>("utc");

        Assert.Throws<NothingMatchedException>(() => services.ReplaceKeyed<IClock, FixedClock>("local"));
    }

    [Fact]
    public void Remove_ShouldThrow_WhenNothingIsRegistered()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<NothingMatchedException>(services.Remove<IClock>);

        Assert.Contains("Remove<IClock>", error.Message);
    }

    [Fact]
    public void RemoveHostedService_ShouldRemoveOnlyThatService_WhenBothRegistrationFormsAreUsed()
    {
        var services = new ServiceCollection();
        services.AddHostedService<SyncWorker>();
        services.AddHostedService(_ => new CleanupWorker());

        services.RemoveHostedService<CleanupWorker>();

        ServiceDescriptor remaining = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Equal(typeof(SyncWorker), remaining.ImplementationType);
    }

    [Fact]
    public void RemoveHostedService_ShouldThrow_WhenTheServiceIsNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddHostedService<SyncWorker>();

        Assert.Throws<NothingMatchedException>(services.RemoveHostedService<CleanupWorker>);
    }

    public interface IClock
    {
        DateTimeOffset Now { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTimeOffset Now => DateTimeOffset.UtcNow;
    }

    public sealed class FixedClock : IClock
    {
        public DateTimeOffset Now => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public sealed class SyncWorker : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    public sealed class CleanupWorker : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
