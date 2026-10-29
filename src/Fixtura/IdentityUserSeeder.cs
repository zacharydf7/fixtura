using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura;

/// <summary>
/// Creates the ASP.NET Identity user, or adds the test user's missing roles and claims to an existing one, and returns the
/// principal Identity signs in. Nothing is removed, because a test may have changed the user through the app; give a user
/// that needs fewer roles its own name. Override <see cref="NewUser"/> to set the app's own fields on a user it creates.
/// </summary>
public class IdentityUserSeeder<TUser, TRole> : ITestUserSeeder where TUser : class, new() where TRole : class, new()
{
    public async Task<IReadOnlyList<Claim>> SeedAsync(TestUser user, IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<TUser>>();
        // An id outlives a rename the app may have made, so it finds the user first when the test gave one.
        TUser? existing = user.Id is null ? await users.FindByNameAsync(user.Name) : await users.FindByIdAsync(user.Id);
        TUser identityUser = existing ?? await CreateAsync(users, user);
        await AddMissingRolesAsync(users, services, identityUser, user.Roles);
        await AddMissingClaimsAsync(users, identityUser, user.Claims);

        var principals = services.GetRequiredService<IUserClaimsPrincipalFactory<TUser>>();
        ClaimsPrincipal principal = await principals.CreateAsync(identityUser);
        return principal.Claims.ToList();
    }

    /// <summary>The user to create when the store has none yet; set fields the app checks, such as a status.</summary>
    protected virtual TUser NewUser(TestUser user) => new();

    private async Task<TUser> CreateAsync(UserManager<TUser> users, TestUser user)
    {
        TUser identityUser = NewUser(user);

        // String-keyed Identity users, the default, take the id here; other key types set it in NewUser.
        if (user.Id is not null && identityUser is IdentityUser<string> keyed)
        {
            keyed.Id = user.Id;
        }

        Require(user.Id is null || await users.GetUserIdAsync(identityUser) == user.Id,
            $"{typeof(TUser).Name} isn't string-keyed; set its id from TestUser.Id in NewUser.");

        await users.SetUserNameAsync(identityUser, user.Name);
        Check(await users.CreateAsync(identityUser));
        return identityUser;
    }

    private static async Task AddMissingRolesAsync(UserManager<TUser> users, IServiceProvider services, TUser identityUser,
        IReadOnlyList<string> roles)
    {
        if (!users.SupportsUserRole)
        {
            Require(roles.Count == 0,
                "The app's Identity store has no roles: add AddRoles<TRole>() or give the test user none.");
            return;
        }

        IList<string> current = await users.GetRolesAsync(identityUser);
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

    private static async Task AddMissingClaimsAsync(UserManager<TUser> users, TUser identityUser, IReadOnlyList<Claim> claims)
    {
        if (!users.SupportsUserClaim)
        {
            Require(claims.Count == 0, "The app's Identity store has no user claims: give the test user none.");
            return;
        }

        IList<Claim> stored = await users.GetClaimsAsync(identityUser);
        List<Claim> missing = claims.Where(wanted => !stored.Any(claim => Same(claim, wanted))).ToList();
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
        Require(result.Succeeded,
            "Seeding the test user failed: " + string.Join(" ", result.Errors.Select(error => error.Description)));
}
