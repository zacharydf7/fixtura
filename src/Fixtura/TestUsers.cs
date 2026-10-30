using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fixtura;

/// <summary>How test users sign in to this host. Set in <c>Configure</c>; used by <c>CreateClientAsync(user)</c>.</summary>
public sealed class TestUsers
{
    private readonly SemaphoreSlim _seeding;
    private readonly SeederAdditions _additions;
    // The claim the cookie handler reads a session store's key from.
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    private readonly RsaSecurityKey _authorityKey = new(RSA.Create(2048));
    private Type? _seeder;

    internal TestUsers() : this(new SemaphoreSlim(1, 1), new SeederAdditions())
    {
    }

    private TestUsers(SemaphoreSlim seeding, SeederAdditions additions)
    {
        _seeding = seeding;
        _additions = additions;
    }

    /// <summary>How users reach the app; <see cref="UserSignIn.AppJwtBearer"/> unless set.</summary>
    public UserSignIn SignIn { get; set; } = UserSignIn.AppJwtBearer;

    /// <summary>Creates or updates each user in the app's store before it signs in. See <see cref="ITestUserSeeder"/>.</summary>
    public TestUsers SeedWith<TSeeder>() where TSeeder : ITestUserSeeder
    {
        _seeder = typeof(TSeeder);
        return this;
    }

    // Shares the seeding lock and what was seeded, because an override host seeds the same store as its parent.
    internal TestUsers Copy() => new(_seeding, _additions) { SignIn = SignIn, _seeder = _seeder };

    internal void Apply(IServiceCollection services)
    {
        services.AddSingleton(_additions);
        if (SignIn == UserSignIn.AppJwtBearer)
        {
            services.PostConfigureAll<JwtBearerOptions>(TrustTestKeyWhenAppCannotSign);
        }
    }

    internal async Task<SignedInUser> SignInAsync(TestUser user, IServiceProvider services)
    {
        string scheme = await ResolveSchemeAsync(services);
        IReadOnlyList<Claim>? seeded = _seeder is null ? null : await SeedAsync(user, services);
        return SignIn == UserSignIn.AppJwtBearer
            ? new SignedInUser(CreateToken(services, scheme, user, seeded), null)
            : new SignedInUser(null, await CreateCookieAsync(services, scheme, user, seeded));
    }

    private async Task<string> ResolveSchemeAsync(IServiceProvider services)
    {
        var schemes = services.GetRequiredService<IAuthenticationSchemeProvider>();
        AuthenticationScheme? scheme = await schemes.GetDefaultAuthenticateSchemeAsync();
        Type expected = SignIn == UserSignIn.AppJwtBearer ? typeof(JwtBearerHandler) : typeof(CookieAuthenticationHandler);
        if (scheme is null || !expected.IsAssignableFrom(scheme.HandlerType))
        {
            string found = scheme is null ? "the app has none" : $"'{scheme.Name}' uses {scheme.HandlerType.Name}";
            throw new InvalidOperationException(
                $"UserSignIn.{SignIn} needs a default {expected.Name} scheme, but {found}. Check host.Users.SignIn.");
        }

        return scheme.Name;
    }

    // Seeds before every sign-in, so claims such as the security stamp are never stale; one seeder runs at a time.
    private async Task<IReadOnlyList<Claim>> SeedAsync(TestUser user, IServiceProvider services)
    {
        await _seeding.WaitAsync();
        try
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            var seeder = (ITestUserSeeder)ActivatorUtilities.CreateInstance(scope.ServiceProvider, _seeder!);
            return await seeder.SeedAsync(user, scope.ServiceProvider);
        }
        finally
        {
            _seeding.Release();
        }
    }

    private string CreateToken(IServiceProvider services, string scheme, TestUser user, IReadOnlyList<Claim>? seeded)
    {
        JwtBearerOptions options = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(scheme);
        TokenValidationParameters parameters = options.TokenValidationParameters;
        SecurityKey key = AppKeys(parameters).FirstOrDefault(CanSign) ?? _authorityKey;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = parameters.ValidIssuer ?? parameters.ValidIssuers?.FirstOrDefault() ?? options.Authority,
            Audience = parameters.ValidAudience ?? parameters.ValidAudiences?.FirstOrDefault() ?? options.Audience,
            Subject = new ClaimsIdentity(seeded ?? DefaultClaims(user, parameters.NameClaimType, parameters.RoleClaimType)),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, Algorithm(key))
        });
    }

    private static List<SecurityKey> AppKeys(TokenValidationParameters parameters) =>
        [.. new[] { parameters.IssuerSigningKey }.OfType<SecurityKey>(), .. parameters.IssuerSigningKeys ?? []];

    // An app that pins its identity provider's public key, or a certificate without its private key, can't sign. Given
    // an RSA or ECDsa object, IdentityModel can't tell whether it holds a private key, so a signature decides.
    private static bool CanSign(SecurityKey key) => key switch
    {
        SymmetricSecurityKey => true,
        RsaSecurityKey { Rsa: { } rsa } =>
            Signs(() => rsa.SignHash(new byte[32], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
        ECDsaSecurityKey { ECDsa: var ecdsa } => Signs(() => ecdsa.SignHash(new byte[32])),
        AsymmetricSecurityKey asymmetric => asymmetric.PrivateKeyStatus == PrivateKeyStatus.Exists,
        _ => false
    };

    private static bool Signs(Action sign)
    {
        try
        {
            sign();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string Algorithm(SecurityKey key) => key switch
    {
        SymmetricSecurityKey => SecurityAlgorithms.HmacSha256,
        ECDsaSecurityKey => key.KeySize switch
        {
            384 => SecurityAlgorithms.EcdsaSha384,
            521 => SecurityAlgorithms.EcdsaSha512,
            _ => SecurityAlgorithms.EcdsaSha256
        },
        _ => SecurityAlgorithms.RsaSha256
    };

    private static async Task<Cookie> CreateCookieAsync(IServiceProvider services, string scheme, TestUser user,
        IReadOnlyList<Claim>? seeded)
    {
        CookieAuthenticationOptions options =
            services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var identity = new ClaimsIdentity(seeded ?? DefaultClaims(user, ClaimTypes.Name, ClaimTypes.Role), scheme);

        // Issued a day ago, so the app's own cookie validation, such as a security-stamp check, runs on the first request.
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), properties, scheme);

        // With a session store, the ticket stays in the store and the cookie carries only its session id, as the app's own
        // sign-in does.
        if (options.SessionStore is { } store)
        {
            // The handler always passes the request, and a store may resolve its services from it.
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            var request = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            string issuer = options.ClaimsIssuer ?? scheme;
            string sessionId = await store.StoreAsync(ticket, request, CancellationToken.None);
            var session = new Claim(SessionIdClaim, sessionId, ClaimValueTypes.String, issuer);
            ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([session], issuer)), scheme);
        }

        string protectedTicket = options.TicketDataFormat.Protect(ticket);
        return new Cookie(options.Cookie.Name!, protectedTicket, "/");
    }

    // Claim types come from the app's own options, so the principal reads the same whether or not inbound mapping is on.
    private static List<Claim> DefaultClaims(TestUser user, string nameType, string roleType) =>
        [new(nameType, user.Name), .. user.Roles.Select(role => new Claim(roleType, role)), .. user.Claims];

    // Apps that trust an Authority, or pin only public keys, hold no key a test can sign with: they trust a test key too.
    private void TrustTestKeyWhenAppCannotSign(JwtBearerOptions options)
    {
        TokenValidationParameters parameters = options.TokenValidationParameters;
        List<SecurityKey> keys = AppKeys(parameters);
        if (keys.Any(CanSign))
        {
            return;
        }

        if (keys.Count > 0)
        {
            parameters.IssuerSigningKeys = [.. parameters.IssuerSigningKeys ?? [], _authorityKey];
        }

        // The app's configuration manager would fetch the Authority's metadata, so a static one replaces it.
        if (keys.Count == 0 || options.Authority is not null || options.MetadataAddress is not null)
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = parameters.ValidIssuer ?? options.Authority };
            configuration.SigningKeys.Add(_authorityKey);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        }
    }
}

// What signs a client in: a bearer token, or a cookie the app can later renew or clear.
internal sealed record SignedInUser(string? BearerToken, Cookie? Cookie);
