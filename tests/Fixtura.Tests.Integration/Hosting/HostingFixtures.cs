using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura.Tests.Integration.Hosting;

public sealed class PlainFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
    }
}

public sealed class KestrelFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Server = ServerKind.Kestrel;
}

public sealed class KestrelJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.Server = ServerKind.Kestrel;
}

public sealed class RecordingResourceFixture : TestHost<Program>
{
    public RecordingResource Resource { get; } = new();

    protected override void Configure(TestHostBuilder host) => host.AddResource(Resource);
}

// The app fails to shut down, as a background service that throws while stopping makes it.
public sealed class FailingShutdownFixture : TestHost<Program>
{
    public RecordingResource Resource { get; } = new();

    protected override void Configure(TestHostBuilder host)
    {
        host.AddResource(Resource);
        host.Services.Configure(services => services.AddHostedService<BrokenShutdown>());
    }
}

public sealed class BrokenShutdown : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Shutdown failed.");
}

public sealed class TestingEnvironmentFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Environment = "Testing";
        host.WebHost(web => web.UseSetting("Probe:WebHost", "applied"));
    }
}

public sealed class UnavailableFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.AddResource(new UnavailableResource());
}

public sealed class RequiredFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.AddResource(new RequiredResource());
}

public sealed class OrderedResourcesFixture : TestHost<Program>
{
    public List<string> Provisioned { get; } = [];

    protected override void Configure(TestHostBuilder host)
    {
        host.AddResource(new OrderedResource("server", Provisioned));
        host.AddResource(new OrderedResource("migrations", Provisioned));
    }
}

public sealed class UnavailableFirstFixture : TestHost<Program>
{
    public List<string> Provisioned { get; } = [];

    protected override void Configure(TestHostBuilder host)
    {
        host.AddResource(new UnavailableResource());
        host.AddResource(new OrderedResource("migrations", Provisioned));
    }
}

// Records when it provisions, after a pause, so a parallel provisioner would let a later resource finish first.
public sealed class OrderedResource(string name, List<string> provisioned) : ITestResource
{
    public async Task<string?> ProvisionAsync(string namePrefix)
    {
        await Task.Delay(name == "server" ? 50 : 0);
        provisioned.Add(name);
        return null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class RecordingResource : ITestResource
{
    public bool Disposed { get; private set; }

    public Task<string?> ProvisionAsync(string namePrefix) => Task.FromResult<string?>(null);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

public sealed class UnavailableResource : ITestResource
{
    public Task<string?> ProvisionAsync(string namePrefix) => Task.FromResult<string?>("The test resource is unavailable.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// Stands for a resource whose "required" switch is on, such as a database server in CI.
public sealed class RequiredResource : ITestResource
{
    public Task<string?> ProvisionAsync(string namePrefix) =>
        throw new InvalidOperationException("The test resource is required and unavailable.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
