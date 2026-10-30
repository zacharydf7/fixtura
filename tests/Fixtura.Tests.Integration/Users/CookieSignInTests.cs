using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Tests.Integration.Users;

public sealed class CookieFixture : TestHost<CookieApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.Users.SignIn = UserSignIn.AppCookie;
}

// The app renews the backdated test cookie on the first request, and a renewed Secure cookie is only sent over HTTPS.
public sealed class SecureCookieFixture : TestHost<CookieApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Users.SignIn = UserSignIn.AppCookie;
        host.Options<CookieAuthenticationOptions>(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
    }
}

// The app keeps tickets in a session store, so its cookie carries only a session id.
public sealed class SessionStoreCookieFixture : TestHost<CookieApp.Program>
{
    public MemoryTicketStore Sessions { get; } = new();

    protected override void Configure(TestHostBuilder host)
    {
        host.Users.SignIn = UserSignIn.AppCookie;
        host.Options<CookieAuthenticationOptions>(options => options.SessionStore = Sessions);
    }
}

// Keeps tickets in memory, as an app's store might keep them in a distributed cache. Like a store that resolves its
// services from the request, it only stores through the overload the cookie handler calls.
public sealed class MemoryTicketStore : ITicketStore
{
    private readonly ConcurrentDictionary<string, AuthenticationTicket> _tickets = new();

    public int Count => _tickets.Count;

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        throw new NotSupportedException("The cookie handler stores tickets with the request.");

    public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        string key = Guid.NewGuid().ToString("N");
        _tickets[key] = ticket;
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        _tickets[key] = ticket;
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(_tickets.GetValueOrDefault(key));

    public Task RemoveAsync(string key)
    {
        _tickets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

public class SessionStoreCookieSignInTests(SessionStoreCookieFixture api) : IClassFixture<SessionStoreCookieFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignIn_WhenTheAppKeepsTicketsInASessionStore()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", 1), (me!.Name, api.Sessions.Count));
    }
}

public class SecureCookieSignInTests(SecureCookieFixture api) : IClassFixture<SecureCookieFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldStaySignedIn_WhenTheAppRenewsASecureCookie()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));
        await client.GetAsync("/me", TestContext.Current.CancellationToken);

        HttpResponseMessage second = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
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
