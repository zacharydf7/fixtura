using System.Security.Claims;

namespace Fixtura;

/// <summary>
/// Who a test signs in as. The same user works with every sign-in kind; a seeder adds what the app's store needs.
/// </summary>
public sealed record TestUser
{
    private TestUser(string name)
    {
        Name = name;
    }

    /// <summary>The user name, issued as the app's name claim.</summary>
    public string Name { get; }

    /// <summary>Roles, issued as the app's role claims.</summary>
    public IReadOnlyList<string> Roles { get; private init; } = [];

    /// <summary>Any other claims, issued as given.</summary>
    public IReadOnlyList<Claim> Claims { get; private init; } = [];

    /// <summary>Starts a user with this name and no roles or claims.</summary>
    public static TestUser Named(string name) => new(name);

    /// <summary>A copy of this user with the roles added.</summary>
    public TestUser WithRoles(params string[] roles) => this with { Roles = [.. Roles, .. roles] };

    /// <summary>A copy of this user with the claim added.</summary>
    public TestUser WithClaim(string type, string value) => this with { Claims = [.. Claims, new Claim(type, value)] };
}
