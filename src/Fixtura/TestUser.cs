using System.Security.Claims;
using System.Text;

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

    /// <summary>The id a seeder gives the user when it creates one, for tests that check ownership; null lets the store choose.</summary>
    public string? Id { get; private init; }

    /// <summary>Roles, issued as the app's role claims.</summary>
    public IReadOnlyList<string> Roles { get; private init; } = [];

    /// <summary>Any other claims, issued as given.</summary>
    public IReadOnlyList<Claim> Claims { get; private init; } = [];

    /// <summary>Starts a user with this name and no roles or claims.</summary>
    public static TestUser Named(string name) => new(name);

    /// <summary>A copy of this user with this id.</summary>
    public TestUser WithId(string id) => this with { Id = id };

    /// <summary>A copy of this user with the roles added.</summary>
    public TestUser WithRoles(params string[] roles) => this with { Roles = [.. Roles, .. roles] };

    /// <summary>A copy of this user with the claim added.</summary>
    public TestUser WithClaim(string type, string value) => this with { Claims = [.. Claims, new Claim(type, value)] };

    // Users built alike are equal, and print their roles and claims, as xUnit shows them in theory names.
    public bool Equals(TestUser? other) =>
        other is not null && Name == other.Name && Id == other.Id && Roles.SequenceEqual(other.Roles)
        && Claims.Select(Key).SequenceEqual(other.Claims.Select(Key));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Id);
        foreach (string role in Roles)
        {
            hash.Add(role);
        }

        foreach (Claim claim in Claims)
        {
            hash.Add(Key(claim));
        }

        return hash.ToHashCode();
    }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Name = {Name}");
        if (Id is not null)
        {
            builder.Append($", Id = {Id}");
        }

        if (Roles.Count > 0)
        {
            builder.Append($", Roles = [{string.Join(", ", Roles)}]");
        }

        if (Claims.Count > 0)
        {
            builder.Append($", Claims = [{string.Join(", ", Claims.Select(claim => $"{claim.Type}: {claim.Value}"))}]");
        }

        return true;
    }

    private static (string Type, string Value) Key(Claim claim) => (claim.Type, claim.Value);
}
