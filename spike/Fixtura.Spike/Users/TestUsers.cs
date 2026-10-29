using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fixtura.Spike.Users;

// How test users sign in to this host. Configured in Configure; used by CreateClientAsync(user).
public sealed class TestUsers
{
    private readonly SemaphoreSlim _seeding;
    private readonly RsaSecurityKey _authorityKey = new(RSA.Create(2048));
    private Type? _seeder;

    public TestUsers() : this(new SemaphoreSlim(1, 1))
    {
    }

    private TestUsers(SemaphoreSlim seeding)
    {
        _seeding = seeding;
    }

    public UserSignIn SignIn { get; set; } = UserSignIn.AppJwtBearer;

    public TestUsers SeedWith<TSeeder>() where TSeeder : ITestUserSeeder
    {
        _seeder = typeof(TSeeder);
        return this;
    }

    // Shares the seeding lock, because an override host seeds the same store as its parent.
    internal TestUsers Copy() => new(_seeding) { SignIn = SignIn, _seeder = _seeder };

    internal void Apply(IServiceCollection services)
    {
        if (SignIn == UserSignIn.AppJwtBearer)
        {
            services.PostConfigureAll<JwtBearerOptions>(UseTestKeyForAuthority);
        }
    }

    internal async Task<SignedInUser> SignInAsync(TestUser user, IServiceProvider services)
    {
        string scheme = await ResolveSchemeAsync(services);
        IReadOnlyList<Claim>? seeded = _seeder is null ? null : await SeedAsync(user, services);
        return SignIn == UserSignIn.AppJwtBearer
            ? new SignedInUser(CreateToken(services, scheme, user, seeded), null)
            : new SignedInUser(null, CreateCookie(services, scheme, user, seeded));
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
        SecurityKey key = parameters.IssuerSigningKey ?? parameters.IssuerSigningKeys?.FirstOrDefault() ?? _authorityKey;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = parameters.ValidIssuer ?? parameters.ValidIssuers?.FirstOrDefault() ?? options.Authority,
            Audience = parameters.ValidAudience ?? parameters.ValidAudiences?.FirstOrDefault() ?? options.Audience,
            Subject = new ClaimsIdentity(seeded ?? DefaultClaims(user, parameters.NameClaimType, parameters.RoleClaimType)),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key,
                key is SymmetricSecurityKey ? SecurityAlgorithms.HmacSha256 : SecurityAlgorithms.RsaSha256)
        });
    }

    private static Cookie CreateCookie(IServiceProvider services, string scheme, TestUser user, IReadOnlyList<Claim>? seeded)
    {
        CookieAuthenticationOptions options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var identity = new ClaimsIdentity(seeded ?? DefaultClaims(user, ClaimTypes.Name, ClaimTypes.Role), scheme);

        // Issued a day ago, so the app's own cookie validation, such as a security-stamp check, runs on the first request.
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        string ticket = options.TicketDataFormat.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity), properties, scheme));
        return new Cookie(options.Cookie.Name!, ticket, "/");
    }

    // Claim types come from the app's own options, so the principal reads the same whether or not inbound mapping is on.
    private static List<Claim> DefaultClaims(TestUser user, string nameType, string roleType) =>
        [new(nameType, user.Name), .. user.Roles.Select(role => new Claim(roleType, role)), .. user.Claims];

    // Apps that trust an Authority hold no private key: give them a static configuration with a test key instead.
    private void UseTestKeyForAuthority(JwtBearerOptions options)
    {
        TokenValidationParameters parameters = options.TokenValidationParameters;
        if (parameters.IssuerSigningKey is not null || parameters.IssuerSigningKeys?.Any() == true)
        {
            return;
        }

        var configuration = new OpenIdConnectConfiguration { Issuer = parameters.ValidIssuer ?? options.Authority };
        configuration.SigningKeys.Add(_authorityKey);
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
    }
}

// What signs a client in: a bearer token, or a cookie the app can later renew or clear.
internal sealed record SignedInUser(string? BearerToken, Cookie? Cookie);
