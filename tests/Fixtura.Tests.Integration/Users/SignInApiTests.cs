using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fixtura.Tests.Integration.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Fixtura.Tests.Integration.Users;

public sealed class KestrelCookieFixture : TestHost<CookieApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Server = ServerKind.Kestrel;
        host.Users.SignIn = UserSignIn.AppCookie;
    }
}

public class JwtSignInApiTests(SymmetricJwtFixture api) : IClassFixture<SymmetricJwtFixture>
{
    [Fact]
    public async Task SignInAsync_ShouldReturnATokenTheAppAccepts_WhenTheAppUsesBearerTokens()
    {
        SignedInUser signedIn = await api.SignInAsync(TestUser.Named("ann").WithRoles("Auditor"));

        HttpClient client = await api.CreateClientAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.BearerToken);
        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);
        Assert.Equal(("ann", null), (me!.Name, signedIn.Cookie));
    }

    [Fact]
    public async Task CreateHandlerAsync_ShouldSendTheUsersToken_WhenGivenAUser()
    {
        using HttpMessageHandler handler = await api.CreateHandlerAsync(TestUser.Named("ann").WithRoles("Auditor"));

        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }

    [Fact]
    public async Task CreateHandlerAsync_ShouldBeAnonymous_WhenGivenNoUser()
    {
        using HttpMessageHandler handler = await api.CreateHandlerAsync();

        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public class CookieSignInApiTests(CookieFixture api) : IClassFixture<CookieFixture>
{
    [Fact]
    public async Task SignInAsync_ShouldReturnACookieTheAppAccepts_WhenTheAppUsesCookies()
    {
        SignedInUser signedIn = await api.SignInAsync(TestUser.Named("ann").WithRoles("Auditor"));

        HttpClient client = await api.CreateClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Add("Cookie", $"{signedIn.Cookie!.Name}={signedIn.Cookie.Value}");
        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        MeResponse? me = await response.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(("ann", null), (me!.Name, signedIn.BearerToken));
    }

    [Fact]
    public async Task CreateHandlerAsync_ShouldBeSignedOut_WhenTheAppClearsItsCookie()
    {
        using HttpMessageHandler handler = await api.CreateHandlerAsync(TestUser.Named("dave").WithRoles("Auditor"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        CancellationToken cancel = TestContext.Current.CancellationToken;
        HttpResponseMessage before = await client.GetAsync("/me", cancel);

        await client.PostAsync("/logout", null, cancel);
        HttpResponseMessage after = await client.GetAsync("/me", cancel);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Unauthorized), (before.StatusCode, after.StatusCode));
    }
}

// The app renews the backdated test cookie on the first request, and a renewed Secure cookie is only sent over HTTPS.
public class SecureCookieSignInApiTests(SecureCookieFixture api) : IClassFixture<SecureCookieFixture>
{
    [Fact]
    public async Task CreateHandlerAsync_ShouldStaySignedIn_WhenTheAppRenewsASecureCookie()
    {
        using HttpMessageHandler handler = await api.CreateHandlerAsync(TestUser.Named("ann").WithRoles("Auditor"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await client.GetAsync("/me", TestContext.Current.CancellationToken);

        HttpResponseMessage second = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}

public class KestrelSignInApiTests(KestrelCookieFixture cookie, KestrelJwtFixture jwt)
    : IClassFixture<KestrelCookieFixture>, IClassFixture<KestrelJwtFixture>
{
    [Fact]
    public async Task CreateHandlerAsync_ShouldSendTheUsersCookie_WhenTheAppRunsOnKestrel()
    {
        using HttpMessageHandler handler = await cookie.CreateHandlerAsync(TestUser.Named("ann").WithRoles("Auditor"));

        using var client = new HttpClient(handler) { BaseAddress = cookie.BaseAddress };
        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }

    [Fact]
    public async Task CreateHandlerAsync_ShouldSendTheUsersToken_WhenTheAppRunsOnKestrel()
    {
        using HttpMessageHandler handler = await jwt.CreateHandlerAsync(TestUser.Named("ann").WithRoles("Auditor"));

        using var client = new HttpClient(handler) { BaseAddress = jwt.BaseAddress };
        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }
}

// The app's default scheme forwards, so signing in needs the scheme named.
public class ForwardingSchemeSignInApiTests(ForwardingSchemeFixture api) : IClassFixture<ForwardingSchemeFixture>
{
    [Fact]
    public async Task CreateHandlerAsync_ShouldSignInThroughTheGivenScheme_WhenOneIsNamed()
    {
        using HttpMessageHandler handler = await api.CreateHandlerAsync(TestUser.Named("ann").WithRoles("Auditor"),
            JwtBearerDefaults.AuthenticationScheme);

        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }
}
