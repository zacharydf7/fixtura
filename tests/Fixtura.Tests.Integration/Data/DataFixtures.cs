using System.Security.Claims;
using CookieIdentityApp;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SampleApp.Auth;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Tests.Integration.Data;

// Every context shape in the sample app, plus a library's internal context, moved to in-memory databases.
public sealed class InMemoryDataFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services
            .ReplaceDbContext<AppIdentityDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Identity")))
            .ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")))
            .ReplaceDbContext<CatalogDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Catalog")))
            .ReplaceDbContext<ReportsDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Reports")))
            .ReplaceDbContext<AnalyticsDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Analytics")))
            .ReplaceDbContexts(typeof(IInventory).Assembly, db => db.UseInMemoryDatabase(host.UniqueName("Inventory")));
    }
}

// The app reads its Orders connection string inside the same lambda that adds its interceptor; a late setting supplies it.
public sealed class KeptOrdersOptionsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Settings(new { ConnectionStrings = new { Orders = "Server=unused" } });
        host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")), AppDbOptions.Keep);
    }
}

public sealed class CookieIdentityFixture : TestHost<CookieIdentityApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services.ReplaceDbContext<AccountsDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Accounts")));
        host.Users.SignIn = UserSignIn.AppCookie;
        host.Users.SeedWith<EmailingSeeder>();
    }
}

// Sets an app field on new users through the NewUser hook.
public sealed class EmailingSeeder : IdentityUserSeeder<IdentityUser, IdentityRole>
{
    protected override IdentityUser NewUser(TestUser user) => new() { Email = $"{user.Name}@sample.test" };
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
    public async Task<IReadOnlyList<Claim>> SeedAsync(TestUserSeedContext context)
    {
        TestUser user = context.User;
        IReadOnlyList<Claim> identity = await new IdentityUserSeeder<AppUser, IdentityRole>().SeedAsync(context);
        string id = identity.First(claim => claim.Type == ClaimTypes.NameIdentifier).Value;
        string stamp = identity.First(claim => claim.Type == new IdentityOptions().ClaimsIdentity.SecurityStampClaimType).Value;
        return [new("sub", id), new("name", user.Name), new(SampleClaims.SecurityStamp, stamp), .. user.Roles.Select(role => new Claim("role", role))];
    }
}

public sealed class OverridableDataFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")));
}
