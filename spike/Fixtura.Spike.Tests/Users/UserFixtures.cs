using System.Security.Claims;
using CookieIdentityApp;
using Fixtura.Spike.EntityFrameworkCore;
using Fixtura.Spike.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SampleApp.Auth;
using SampleApp.Data;

namespace Fixtura.Spike.Tests.Users;

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

public sealed class CookieIdentityFixture : TestHost<CookieIdentityApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services.ReplaceDbContext<AccountsDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Accounts")));
        host.Users.SignIn = UserSignIn.AppCookie;
        host.Users.SeedWith<IdentityUserSeeder<IdentityUser, IdentityRole>>();
    }
}

// A cookie sign-in asked of an app that only has a JWT scheme.
public sealed class MismatchedSignInFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.Users.SignIn = UserSignIn.AppCookie;
}

public sealed class StampCheckedJwtFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services.ReplaceDbContext<AppIdentityDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Identity")));
        host.Users.SeedWith<SampleAppUserSeeder>();
    }
}

// The sample app's token carries the Identity user's id and security stamp under its own claim names.
public sealed class SampleAppUserSeeder : ITestUserSeeder
{
    public async Task<IReadOnlyList<Claim>> SeedAsync(TestUser user, IServiceProvider services)
    {
        IReadOnlyList<Claim> identity = await new IdentityUserSeeder<AppUser, IdentityRole>().SeedAsync(user, services);
        string id = identity.First(claim => claim.Type == ClaimTypes.NameIdentifier).Value;
        string stamp = identity.First(claim => claim.Type == new IdentityOptions().ClaimsIdentity.SecurityStampClaimType).Value;
        return [new("sub", id), new("name", user.Name), new(SampleClaims.SecurityStamp, stamp), .. user.Roles.Select(role => new Claim("role", role))];
    }
}

public sealed record MeResponse(string? Name, string[] Roles, string? CheckedBy);
