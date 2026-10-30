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
    public void Replace_ShouldUseTheGivenLifetime_WhenOneIsSet()
    {
        var services = new ServiceCollection();
        services.AddScoped<IClock, SystemClock>();

        services.Replace<IClock, FixedClock>(ServiceLifetime.Singleton);

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services).Lifetime);
    }

    [Fact]
    public void Replace_ShouldRegisterTheInstance_WhenGivenOne()
    {
        var services = new ServiceCollection();
        services.AddScoped<IClock, SystemClock>();
        var clock = new FixedClock();

        services.Replace<IClock>(clock);

        ServiceDescriptor replacement = Assert.Single(services);
        Assert.Same(clock, replacement.ImplementationInstance);
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

        var error = Assert.Throws<NothingMatchedException>(() => services.Replace<IClock, FixedClock>());

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

    [Fact]
    public void DeferHostedService_ShouldStartTheAppsOwnInstance_WhenTheAppForwardsToASingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SyncWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<SyncWorker>());

        services.DeferHostedService<SyncWorker>();

        using ServiceProvider provider = services.BuildServiceProvider();
        var deferred = provider.GetRequiredService<DeferredHostedService<SyncWorker>>();
        Assert.Same(provider.GetRequiredService<SyncWorker>(), deferred.Service);
    }

    [Fact]
    public void DeferHostedService_ShouldStartTheInstance_WhenTheAppRegisteredOne()
    {
        var services = new ServiceCollection();
        var worker = new CleanupWorker();
        services.AddSingleton<IHostedService>(worker);

        services.DeferHostedService<CleanupWorker>();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(worker, provider.GetRequiredService<DeferredHostedService<CleanupWorker>>().Service);
    }

    [Fact]
    public void DeferHostedService_ShouldDisposeTheService_WhenTheProviderIsDisposed()
    {
        var services = new ServiceCollection();
        services.AddHostedService<DisposableWorker>();
        services.DeferHostedService<DisposableWorker>();
        ServiceProvider provider = services.BuildServiceProvider();
        DisposableWorker worker = provider.GetRequiredService<DeferredHostedService<DisposableWorker>>().Service;

        provider.Dispose();

        Assert.True(worker.Disposed);
    }

    [Fact]
    public void DeferHostedService_ShouldThrow_WhenTheServiceIsNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddHostedService<SyncWorker>();

        Assert.Throws<NothingMatchedException>(services.DeferHostedService<CleanupWorker>);
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

    // Records its disposal, as a timer-based hosted service releases its timer.
    public sealed class DisposableWorker : IHostedService, IDisposable
    {
        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() => Disposed = true;
    }
}
