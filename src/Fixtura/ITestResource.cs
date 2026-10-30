namespace Fixtura;

/// <summary>
/// Infrastructure a host needs, such as a database server, provisioned before the first test. It is disposed after the
/// last test even when an earlier resource was unavailable and it never provisioned.
/// </summary>
public interface ITestResource : IAsyncDisposable
{
    /// <summary>
    /// Returns why the resource is unavailable, or null when it is ready. Throws when it is required and unavailable, so
    /// the fixture fails instead of skipping.
    /// </summary>
    Task<string?> ProvisionAsync(string namePrefix);

    /// <summary>
    /// Puts the resource's data back to its starting state for <c>ResetDataAsync</c>, for example by emptying a database's
    /// tables. Resources with no data to reset keep this default, which does nothing.
    /// </summary>
    Task ResetAsync() => Task.CompletedTask;
}
