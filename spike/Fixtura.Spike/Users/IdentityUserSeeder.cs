using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Spike.Users;

// Brings the ASP.NET Identity user to exactly the test user's roles and claims and returns the principal Identity signs in.
public class IdentityUserSeeder<TUser, TRole> : ITestUserSeeder where TUser : class, new() where TRole : class, new()
{
    public async Task<IReadOnlyList<Claim>> SeedAsync(TestUser user, IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<TUser>>();
        TUser identityUser = await users.FindByNameAsync(user.Name) ?? await CreateAsync(users, user.Name);
        await SyncRolesAsync(users, services, identityUser, user.Roles);
        await SyncClaimsAsync(users, identityUser, user.Claims);

        ClaimsPrincipal principal = await services.GetRequiredService<IUserClaimsPrincipalFactory<TUser>>().CreateAsync(identityUser);
        return principal.Claims.ToList();
    }

    private static async Task<TUser> CreateAsync(UserManager<TUser> users, string name)
    {
        var identityUser = new TUser();
        await users.SetUserNameAsync(identityUser, name);
        Check(await users.CreateAsync(identityUser));
        return identityUser;
    }

    private static async Task SyncRolesAsync(UserManager<TUser> users, IServiceProvider services, TUser identityUser,
        IReadOnlyList<string> roles)
    {
        if (!users.SupportsUserRole)
        {
            Require(roles.Count == 0, "The app's Identity store has no roles: add AddRoles<TRole>() or give the test user none.");
            return;
        }

        IList<string> current = await users.GetRolesAsync(identityUser);
        Check(await users.RemoveFromRolesAsync(identityUser, current.Except(roles)));
        var roleManager = services.GetRequiredService<RoleManager<TRole>>();
        foreach (string role in roles.Except(current))
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var identityRole = new TRole();
                await roleManager.SetRoleNameAsync(identityRole, role);
                Check(await roleManager.CreateAsync(identityRole));
            }

            Check(await users.AddToRoleAsync(identityUser, role));
        }
    }

    private static async Task SyncClaimsAsync(UserManager<TUser> users, TUser identityUser, IReadOnlyList<Claim> claims)
    {
        if (!users.SupportsUserClaim)
        {
            Require(claims.Count == 0, "The app's Identity store has no user claims: give the test user none.");
            return;
        }

        IList<Claim> stored = await users.GetClaimsAsync(identityUser);
        List<Claim> extra = stored.Where(claim => !claims.Any(wanted => Same(claim, wanted))).ToList();
        List<Claim> missing = claims.Where(wanted => !stored.Any(claim => Same(claim, wanted))).ToList();
        if (extra.Count > 0)
        {
            Check(await users.RemoveClaimsAsync(identityUser, extra));
        }

        if (missing.Count > 0)
        {
            Check(await users.AddClaimsAsync(identityUser, missing));
        }
    }

    private static bool Same(Claim left, Claim right) => left.Type == right.Type && left.Value == right.Value;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Check(IdentityResult result) =>
        Require(result.Succeeded, "Seeding the test user failed: " + string.Join(" ", result.Errors.Select(error => error.Description)));
}
