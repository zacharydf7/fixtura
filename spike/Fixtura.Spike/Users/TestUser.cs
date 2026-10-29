using System.Security.Claims;

namespace Fixtura.Spike.Users;

// Who a test signs in as. The same user works with every sign-in kind; a seeder adds what the app's store needs.
public sealed record TestUser
{
    private TestUser(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IReadOnlyList<string> Roles { get; private init; } = [];

    public IReadOnlyList<Claim> Claims { get; private init; } = [];

    public static TestUser Named(string name) => new(name);

    public TestUser WithRoles(params string[] roles) => this with { Roles = [.. Roles, .. roles] };

    public TestUser WithClaim(string type, string value) => this with { Claims = [.. Claims, new Claim(type, value)] };
}
