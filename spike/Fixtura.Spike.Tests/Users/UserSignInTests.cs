using System.Net;
using System.Net.Http.Json;
using Fixtura.Spike.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;

namespace Fixtura.Spike.Tests.Users;

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

public class CookieIdentityTests(CookieIdentityFixture api) : IClassFixture<CookieIdentityFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignInWithTheAppsOwnCookie_WhenTheUserIsInTheStore()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "Auditor"), (me!.Name, Assert.Single(me.Roles)));
    }

    [Fact]
    public async Task CreateClientAsync_ShouldDropTheStoredRoles_WhenTheSameNameReturnsWithFewer()
    {
        await api.CreateClientAsync(TestUser.Named("carol").WithRoles("Auditor"));
        HttpClient client = await api.CreateClientAsync(TestUser.Named("carol"));

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldBeSignedOut_WhenTheAppClearsItsCookie()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("dave").WithRoles("Auditor"));
        CancellationToken cancel = TestContext.Current.CancellationToken;

        await client.PostAsync("/logout", null, cancel);
        HttpResponseMessage response = await client.GetAsync("/me", cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // .NET 10 answers API endpoints with 401 instead of a login redirect.
    [Fact]
    public async Task CreateClientAsync_ShouldBeRejectedByTheAppsStampValidator_WhenTheSecurityStampChangedAfterSignIn()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("stale").WithRoles("Auditor"));
        await ChangeSecurityStampAsync<IdentityUser>(api.Services, "stale");

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static async Task ChangeSecurityStampAsync<TUser>(IServiceProvider services, string name) where TUser : class
    {
        using IServiceScope scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        await users.UpdateSecurityStampAsync((await users.FindByNameAsync(name))!);
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

public class StampCheckedJwtTests(StampCheckedJwtFixture api) : IClassFixture<StampCheckedJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldPassTheAppsAccountCheck_WhenSeededIntoItsStore()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Manager"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "Manager"), (me!.Name, Assert.Single(me.Roles)));
    }

    [Fact]
    public async Task CreateClientAsync_ShouldBeRejectedByTheAppsAccountCheck_WhenTheSecurityStampChanged()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("stale").WithRoles("Manager"));
        await CookieIdentityTests.ChangeSecurityStampAsync<AppUser>(api.Services, "stale");

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
