using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura;

/// <summary>
/// Creates the ASP.NET Identity user, or gives an existing one the test user's roles and claims, and returns the principal
/// Identity signs in. Roles and claims an earlier sign-in added are removed when this one doesn't ask for them; what the
/// app gave the user stays. Override <see cref="NewUser"/> to set the app's own fields on a user it creates.
/// </summary>
public class IdentityUserSeeder<TUser, TRole> : ITestUserSeeder where TUser : class, new() where TRole : class, new()
{
    public async Task<IReadOnlyList<Claim>> SeedAsync(TestUser user, IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<TUser>>();
        // An id outlives a rename the app may have made, so it finds the user first when the test gave one.
        TUser? existing = user.Id is null ? await users.FindByNameAsync(user.Name) : await users.FindByIdAsync(user.Id);
        TUser identityUser = existing ?? await CreateAsync(users, user);

        // Outside a Fixtura host nothing tracks earlier sign-ins, so nothing is taken back.
        SeederAdditions additions = services.GetService<SeederAdditions>() ?? new SeederAdditions();
        string userId = await users.GetUserIdAsync(identityUser);
        await SyncRolesAsync(users, services, identityUser, user.Roles, additions.RolesOf(userId));
        await SyncClaimsAsync(users, identityUser, user.Claims, additions.ClaimsOf(userId));

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

    private static async Task SyncRolesAsync(UserManager<TUser> users, IServiceProvider services, TUser identityUser,
        IReadOnlyList<string> roles, HashSet<string> added)
    {
        if (!users.SupportsUserRole)
        {
            Require(roles.Count == 0,
                "The app's Identity store has no roles: add AddRoles<TRole>() or give the test user none.");
            return;
        }

        // Identity matches role names whatever their case, so these comparisons do too.
        IList<string> current = await users.GetRolesAsync(identityUser);
        List<string> stale = current
            .Where(role => added.Contains(role) && !roles.Contains(role, StringComparer.OrdinalIgnoreCase)).ToList();
        if (stale.Count > 0)
        {
            Check(await users.RemoveFromRolesAsync(identityUser, stale));
        }

        added.IntersectWith(roles);
        var roleManager = services.GetRequiredService<RoleManager<TRole>>();
        foreach (string role in roles.Except(current, StringComparer.OrdinalIgnoreCase))
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var identityRole = new TRole();
                await roleManager.SetRoleNameAsync(identityRole, role);
                Check(await roleManager.CreateAsync(identityRole));
            }

            Check(await users.AddToRoleAsync(identityUser, role));
            added.Add(role);
        }
    }

    private static async Task SyncClaimsAsync(UserManager<TUser> users, TUser identityUser, IReadOnlyList<Claim> claims,
        HashSet<(string Type, string Value)> added)
    {
        if (!users.SupportsUserClaim)
        {
            Require(claims.Count == 0, "The app's Identity store has no user claims: give the test user none.");
            return;
        }

        List<(string Type, string Value)> wanted = claims.Select(Key).ToList();
        IList<Claim> stored = await users.GetClaimsAsync(identityUser);
        List<Claim> stale = stored.Where(claim => added.Contains(Key(claim)) && !wanted.Contains(Key(claim))).ToList();
        if (stale.Count > 0)
        {
            Check(await users.RemoveClaimsAsync(identityUser, stale));
        }

        added.IntersectWith(wanted);
        List<Claim> missing = claims.Where(claim => !stored.Any(existing => Key(existing) == Key(claim))).ToList();
        if (missing.Count > 0)
        {
            Check(await users.AddClaimsAsync(identityUser, missing));
            added.UnionWith(missing.Select(Key));
        }
    }

    private static (string Type, string Value) Key(Claim claim) => (claim.Type, claim.Value);

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
