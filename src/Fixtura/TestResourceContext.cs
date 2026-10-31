namespace Fixtura;

/// <summary>What a resource is provisioned with. New members can be added here without breaking resources.</summary>
public sealed class TestResourceContext(string namePrefix, CancellationToken cancellationToken = default)
{
    /// <summary>
    /// The fixture's unique prefix, such as <c>fixtura_1a2b3c4d</c>, for names the resource creates, such as databases.
    /// </summary>
    public string NamePrefix { get; } = namePrefix;

    /// <summary>Cancelled when the test run is.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>
    /// The type of the fixture being provisioned, for a resource that keeps something for the next fixture of the same
    /// shape, such as a pool of databases; null outside a fixture.
    /// </summary>
    public Type? FixtureType { get; init; }
}
