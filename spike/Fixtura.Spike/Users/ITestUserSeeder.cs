using System.Security.Claims;

namespace Fixtura.Spike.Users;

// Before each sign-in: create or update the user in the app's store and return the claims the app's own sign-in issues.
public interface ITestUserSeeder
{
    Task<IReadOnlyList<Claim>> SeedAsync(TestUser user, IServiceProvider services);
}
