namespace Fixtura.Spike;

// Infrastructure a host needs, such as a database server, provisioned before the first test.
public interface ITestResource : IAsyncDisposable
{
    // Returns why the resource is unavailable, or null when it is ready. Throws when it is required and unavailable.
    Task<string?> ProvisionAsync(string namePrefix);
}
