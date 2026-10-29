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

public sealed class UnavailableFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.AddResource(new UnavailableResource());
}

public sealed class RequiredFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.AddResource(new RequiredResource());
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
