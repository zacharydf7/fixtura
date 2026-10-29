namespace Fixtura;

/// <summary>Infrastructure a host needs, such as a database server, provisioned before the first test.</summary>
public interface ITestResource : IAsyncDisposable
{
    /// <summary>
    /// Returns why the resource is unavailable, or null when it is ready. Throws when it is required and unavailable, so
    /// the fixture fails instead of skipping.
    /// </summary>
    Task<string?> ProvisionAsync(string namePrefix);
}
