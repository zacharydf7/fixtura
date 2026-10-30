namespace Fixtura;

/// <summary>What a seeder seeds a user with. New members can be added here without breaking seeders.</summary>
public sealed class TestUserSeedContext(TestUser user, IServiceProvider services, CancellationToken cancellationToken = default)
{
    /// <summary>The user signing in.</summary>
    public TestUser User { get; } = user;

    /// <summary>The app's services, from a scope that lasts for this seeding.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>Cancelled when the test is.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;
}
