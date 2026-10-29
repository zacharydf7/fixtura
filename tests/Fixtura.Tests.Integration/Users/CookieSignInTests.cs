using System.Net;
using System.Net.Http.Json;

namespace Fixtura.Tests.Integration.Users;

public sealed class CookieFixture : TestHost<CookieApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.Users.SignIn = UserSignIn.AppCookie;
}

public class CookieSignInTests(CookieFixture api) : IClassFixture<CookieFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignInWithTheAppsOwnCookie_WhenTheAppUsesCookies()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "Auditor"), (me!.Name, Assert.Single(me.Roles)));
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

    [Fact]
    public async Task CreateClientAsync_ShouldSignIn_WhenTheTicketIsLargerThanOneCookie()
    {
        TestUser user = TestUser.Named("erin").WithRoles("Auditor");
        for (int i = 0; i < 200; i++)
        {
            user = user.WithClaim($"note-{i}", "a value long enough to push the protected ticket past four kilobytes");
        }

        HttpClient client = await api.CreateClientAsync(user);

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
