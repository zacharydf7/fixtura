using System.Net;
using System.Net.Http.Json;

namespace Fixtura.Tests.Integration.Users;

public sealed class SymmetricJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
    }
}

public sealed class AuthorityJwtFixture : TestHost<AuthorityJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
    }
}

// A cookie sign-in asked of an app that only has a JWT scheme.
public sealed class MismatchedSignInFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.Users.SignIn = UserSignIn.AppCookie;
}

public sealed record MeResponse(string? Name, string[] Roles, string? CheckedBy);

public class SymmetricJwtTests(SymmetricJwtFixture api) : IClassFixture<SymmetricJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldPassTheAppsTokenEvents_WhenTheAppSignsWithASymmetricKey()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "Auditor", "app"), (me!.Name, Assert.Single(me.Roles), me.CheckedBy));
    }

    [Fact]
    public async Task CreateClientAsync_ShouldBeAnonymous_WhenGivenNoUser()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldBeForbidden_WhenTheUserLacksTheRole()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("bob"));

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

public class AuthorityJwtTests(AuthorityJwtFixture api) : IClassFixture<AuthorityJwtFixture>
{
    // The authority's host never resolves, so this passes only if no metadata request was made.
    [Fact]
    public async Task CreateClientAsync_ShouldSignInWithoutTheNetwork_WhenTheAppTrustsAnAuthority()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "Auditor", "app"), (me!.Name, Assert.Single(me.Roles), me.CheckedBy));
    }
}

public class MismatchedSignInTests(MismatchedSignInFixture api) : IClassFixture<MismatchedSignInFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldNameTheMismatch_WhenTheSignInKindDoesNotMatchTheAppsScheme()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateClientAsync(TestUser.Named("ann")));

        Assert.Contains("UserSignIn.AppCookie needs a default CookieAuthenticationHandler scheme", error.Message);
    }
}
