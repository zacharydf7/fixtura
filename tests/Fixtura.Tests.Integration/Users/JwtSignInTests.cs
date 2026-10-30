using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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

// The app pins its identity provider's public key, so it holds nothing a test token could be signed with.
public sealed class PublicKeyJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Options<JwtBearerOptions>(options => options.TokenValidationParameters.IssuerSigningKey =
            new RsaSecurityKey(RSA.Create(2048).ExportParameters(includePrivateParameters: false)));
}

// The usual way to pin a public key: an RSA object loaded with only the public part, as from a PEM file.
public sealed class PublicRsaObjectJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        RSA publicOnly = RSA.Create();
        publicOnly.ImportParameters(RSA.Create(2048).ExportParameters(includePrivateParameters: false));
        host.Options<JwtBearerOptions>(options => options.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(publicOnly));
    }
}

public sealed class PublicECDsaJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        ECParameters publicPart = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportParameters(includePrivateParameters: false);
        host.Options<JwtBearerOptions>(options =>
            options.TokenValidationParameters.IssuerSigningKey = new ECDsaSecurityKey(ECDsa.Create(publicPart)));
    }
}

// Pins a public key and names an Authority whose host never resolves, so only a sign-in without metadata succeeds.
public sealed class AuthorityPublicKeyJwtFixture : TestHost<AuthorityJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Options<JwtBearerOptions>(options => options.TokenValidationParameters.IssuerSigningKey =
            new RsaSecurityKey(RSA.Create(2048).ExportParameters(includePrivateParameters: false)));
}

public sealed class ECDsaJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Options<JwtBearerOptions>(options => options.TokenValidationParameters.IssuerSigningKey =
            new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP384)));
}

// The app's default scheme forwards to its JWT scheme, as in apps that accept more than one kind of credential.
public sealed class ForwardingSchemeFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.Services.Configure(AddForwardingDefault);

    public static void AddForwardingDefault(IServiceCollection services) =>
        services.AddAuthentication(options => options.DefaultScheme = "Smart")
            .AddPolicyScheme("Smart", "Smart", options => options.ForwardDefault = JwtBearerDefaults.AuthenticationScheme);
}

public sealed class NamedSchemeFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Users.Scheme = JwtBearerDefaults.AuthenticationScheme;
        host.Services.Configure(ForwardingSchemeFixture.AddForwardingDefault);
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

public class PublicKeyJwtTests(PublicKeyJwtFixture api) : IClassFixture<PublicKeyJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignIn_WhenTheAppHoldsOnlyAPublicKey()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "app"), (me!.Name, me.CheckedBy));
    }
}

public class PublicRsaObjectJwtTests(PublicRsaObjectJwtFixture api) : IClassFixture<PublicRsaObjectJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignIn_WhenTheAppHoldsOnlyAPublicRsaObject()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "app"), (me!.Name, me.CheckedBy));
    }
}

public class PublicECDsaJwtTests(PublicECDsaJwtFixture api) : IClassFixture<PublicECDsaJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignIn_WhenTheAppHoldsOnlyAPublicECDsaKey()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "app"), (me!.Name, me.CheckedBy));
    }
}

public class AuthorityPublicKeyJwtTests(AuthorityPublicKeyJwtFixture api) : IClassFixture<AuthorityPublicKeyJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignInWithoutTheNetwork_WhenTheAppPinsAPublicKeyAndTrustsAnAuthority()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "app"), (me!.Name, me.CheckedBy));
    }
}

public class ECDsaJwtTests(ECDsaJwtFixture api) : IClassFixture<ECDsaJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignIn_WhenTheAppSignsWithAnECDsaKey()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "app"), (me!.Name, me.CheckedBy));
    }
}

public class ForwardingSchemeTests(ForwardingSchemeFixture api) : IClassFixture<ForwardingSchemeFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldNameTheSchemeSetting_WhenTheDefaultSchemeForwards()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateClientAsync(TestUser.Named("ann")));

        Assert.Contains("host.Users.Scheme", error.Message);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldSignInThroughTheClientsScheme_WhenTheClientNamesOne()
    {
        var options = new TestClientOptions { Scheme = JwtBearerDefaults.AuthenticationScheme };
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"), options);

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "app"), (me!.Name, me.CheckedBy));
    }

    [Fact]
    public async Task CreateClientAsync_ShouldSayNoSchemeHasTheName_WhenTheClientNamesAnUnknownOne()
    {
        var options = new TestClientOptions { Scheme = "Missing" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateClientAsync(TestUser.Named("ann"), options));

        Assert.Contains("a JwtBearerHandler scheme named 'Missing', but the app has none", error.Message);
    }
}

public class NamedSchemeTests(NamedSchemeFixture api) : IClassFixture<NamedSchemeFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignInThroughTheNamedScheme_WhenTheDefaultSchemeForwards()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(("ann", "app"), (me!.Name, me.CheckedBy));
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
