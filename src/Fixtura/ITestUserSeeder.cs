using System.Security.Claims;

namespace Fixtura;

/// <summary>
/// Runs before each sign-in: creates or updates the user in the app's store and returns the claims the app's own sign-in
/// would issue.
/// </summary>
public interface ITestUserSeeder
{
    Task<IReadOnlyList<Claim>> SeedAsync(TestUser user, IServiceProvider services);
}
