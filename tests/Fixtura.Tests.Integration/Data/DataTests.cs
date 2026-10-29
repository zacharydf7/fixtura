using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Fixtura.Tests.Integration.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Tests.Integration.Data;

public class DbContextSwapTests(InMemoryDataFixture api) : IClassFixture<InMemoryDataFixture>
{
    [Fact]
    public void EveryContext_ShouldHaveOnlyTheReplacementProvider_WhenSwapped()
    {
        using IServiceScope scope = api.CreateScope();

        List<DbContextOptions> allOptions = scope.ServiceProvider.GetServices<DbContextOptions>().ToList();

        Assert.Equal(6, allOptions.Count);
        Assert.All(allOptions, options => Assert.Equal(["InMemoryOptionsExtension"],
            options.Extensions.Where(extension => extension.Info.IsDatabaseProvider).Select(extension => extension.GetType().Name)));
    }

    [Fact]
    public async Task EveryShape_ShouldWorkAgainstTheReplacement_WhenSwapped()
    {
        using IServiceScope scope = api.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        CancellationToken cancel = TestContext.Current.CancellationToken;

        int identity = await services.GetRequiredService<AppIdentityDbContext>().Users.CountAsync(cancel);
        int orders = await services.GetRequiredService<OrdersDbContext>().Orders.CountAsync(cancel);
        int catalog = await services.GetRequiredService<CatalogDbContext>().Products.CountAsync(cancel);
        int reports = await services.GetRequiredService<IDbContextFactory<ReportsDbContext>>().CreateDbContext().Runs.CountAsync(cancel);
        int analytics = await services.GetRequiredService<IDbContextFactory<AnalyticsDbContext>>().CreateDbContext().PageViews.CountAsync(cancel);
        int inventory = await services.GetRequiredService<IInventory>().CountAsync();

        Assert.Equal([0, 0, 0, 0, 0, 0], [identity, orders, catalog, reports, analytics, inventory]);
    }
}

public class KeptOrdersOptionsTests(KeptOrdersOptionsFixture api) : IClassFixture<KeptOrdersOptionsFixture>
{
    [Fact]
    public async Task KeptAppOptions_ShouldStillRunTheAppsInterceptor_WhenItsConnectionStringIsSupplied()
    {
        using IServiceScope scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var order = new Order { Product = "Desk", Quantity = 1 };

        db.Orders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(db.Database.IsInMemory());
        Assert.NotNull(order.AuditedAt);
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

    // .NET 10 answers API endpoints with 401 instead of a login redirect.
    [Fact]
    public async Task CreateClientAsync_ShouldBeRejectedByTheAppsStampValidator_WhenTheSecurityStampChangedAfterSignIn()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("stale").WithRoles("Auditor"));
        await Identity.ChangeSecurityStampAsync<IdentityUser>(api.Services, "stale");

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldKeepRolesTheAppGranted_WhenTheSameUserSignsInAgain()
    {
        await api.CreateClientAsync(TestUser.Named("carol"));
        await Identity.AddToRoleAsync<IdentityUser>(api.Services, "carol", "Auditor");
        HttpClient client = await api.CreateClientAsync(TestUser.Named("carol"));

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldKeepClaimsTheAppAdded_WhenTheSameUserSignsInAgain()
    {
        await api.CreateClientAsync(TestUser.Named("ivy").WithRoles("Auditor"));
        await Identity.AddClaimAsync<IdentityUser>(api.Services, "ivy", new Claim("department", "audit"));

        await api.CreateClientAsync(TestUser.Named("ivy").WithRoles("Auditor"));

        IList<Claim> claims = await Identity.GetClaimsAsync<IdentityUser>(api.Services, "ivy");
        Assert.Contains(claims, claim => claim is { Type: "department", Value: "audit" });
    }

    [Fact]
    public async Task WithId_ShouldFindTheUser_WhenTheAppRenamedItAfterItsFirstSignIn()
    {
        TestUser user = TestUser.Named("hank").WithId("7c0ffee0-0000-0000-0000-000000000002").WithRoles("Auditor");
        await api.CreateClientAsync(user);
        await Identity.RenameAsync<IdentityUser>(api.Services, "hank", "henry");

        HttpClient client = await api.CreateClientAsync(user);

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);
        Assert.Equal("henry", me!.Name);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldBeSignedOut_WhenTheAppSignsTheUserOut()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("dave").WithRoles("Auditor"));
        CancellationToken cancel = TestContext.Current.CancellationToken;

        await client.PostAsync("/logout", null, cancel);
        HttpResponseMessage response = await client.GetAsync("/me", cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithId_ShouldGiveTheStoredUserThatId_WhenTheSeederCreatesTheUser()
    {
        await api.CreateClientAsync(TestUser.Named("gina").WithId("7c0ffee0-0000-0000-0000-000000000001").WithRoles("Auditor"));

        IdentityUser? stored = await Identity.FindAsync<IdentityUser>(api.Services, "gina");

        Assert.Equal("7c0ffee0-0000-0000-0000-000000000001", stored?.Id);
    }

    [Fact]
    public async Task NewUser_ShouldSetTheAppsFields_WhenTheSeederCreatesTheUser()
    {
        await api.CreateClientAsync(TestUser.Named("frank").WithRoles("Auditor"));

        IdentityUser? stored = await Identity.FindAsync<IdentityUser>(api.Services, "frank");

        Assert.Equal("frank@sample.test", stored?.Email);
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
        await Identity.ChangeSecurityStampAsync<AppUser>(api.Services, "stale");

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public class SharedDatabaseOverrideTests(OverridableDataFixture api) : IClassFixture<OverridableDataFixture>
{
    [Fact]
    public async Task WithOverrides_ShouldShareTheParentsDatabases_WhenTheyAreNamedWithUniqueName()
    {
        await using TestHost<Program> overridden = api.WithOverrides(host =>
            host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders"))));
        using (IServiceScope scope = api.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Orders.Add(new Order { Product = "Shared", Quantity = 1 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using IServiceScope other = overridden.CreateScope();
        bool found = await other.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders
            .AnyAsync(order => order.Product == "Shared", TestContext.Current.CancellationToken);

        Assert.True(found);
    }
}

internal static class Identity
{
    public static async Task ChangeSecurityStampAsync<TUser>(IServiceProvider services, string name) where TUser : class
    {
        using IServiceScope scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        await users.UpdateSecurityStampAsync((await users.FindByNameAsync(name))!);
    }

    public static async Task AddToRoleAsync<TUser>(IServiceProvider services, string name, string role) where TUser : class
    {
        using IServiceScope scope = services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await roles.CreateAsync(new IdentityRole(role));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        await users.AddToRoleAsync((await users.FindByNameAsync(name))!, role);
    }

    public static async Task AddClaimAsync<TUser>(IServiceProvider services, string name, Claim claim) where TUser : class
    {
        using IServiceScope scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        await users.AddClaimAsync((await users.FindByNameAsync(name))!, claim);
    }

    public static async Task<IList<Claim>> GetClaimsAsync<TUser>(IServiceProvider services, string name) where TUser : class
    {
        using IServiceScope scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        return await users.GetClaimsAsync((await users.FindByNameAsync(name))!);
    }

    public static async Task RenameAsync<TUser>(IServiceProvider services, string name, string newName) where TUser : class
    {
        using IServiceScope scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        TUser user = (await users.FindByNameAsync(name))!;
        await users.SetUserNameAsync(user, newName);
        await users.UpdateAsync(user);
    }

    public static async Task<TUser?> FindAsync<TUser>(IServiceProvider services, string name) where TUser : class
    {
        using IServiceScope scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<TUser>>().FindByNameAsync(name);
    }
}
