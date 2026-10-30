using System.Net.Http.Json;
using CookieIdentityApp;
using Fixtura.Tests.Integration.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;

namespace Fixtura.Tests.Integration.Data;

public sealed class ResettableInMemoryFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services
            .ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")))
            .ReplaceDbContext<CatalogDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Catalog")));
        host.RecreateInMemoryDatabasesOnReset();
    }
}

public sealed class ResettableCookieIdentityFixture : TestHost<CookieIdentityApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services.ReplaceDbContext<AccountsDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Accounts")));
        host.Users.SignIn = UserSignIn.AppCookie;
        host.Users.SeedWith<EmailingSeeder>();
        host.RecreateInMemoryDatabasesOnReset();
    }
}

public class InMemoryResetTests(ResettableInMemoryFixture api) : IClassFixture<ResettableInMemoryFixture>
{
    [Fact]
    public async Task ResetDataAsync_ShouldEmptyEveryInMemoryDatabase_WhenRecreateOnResetIsSet()
    {
        using (IServiceScope scope = api.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.Add(new Order { Product = "Lamp", Quantity = 1 });
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Products.Add(new Product { Name = "Lamp" });
            await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await api.ResetDataAsync();

        using IServiceScope check = api.CreateScope();
        int orders = await check.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.CountAsync(TestContext.Current.CancellationToken);
        int products = await check.ServiceProvider.GetRequiredService<CatalogDbContext>().Products.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal((0, 0), (orders, products));
    }
}

public class SeededUserResetTests(ResettableCookieIdentityFixture api) : IClassFixture<ResettableCookieIdentityFixture>
{
    // A role the app granted survives later sign-ins, so only a reset that emptied the store can drop it.
    [Fact]
    public async Task CreateClientAsync_ShouldSeedAFreshUser_WhenTheDataWasReset()
    {
        await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));
        await Identity.AddToRoleAsync<IdentityUser>(api.Services, "ann", "Manager");
        await api.ResetDataAsync();

        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);
        Assert.Equal(("ann", "Auditor"), (me!.Name, Assert.Single(me.Roles)));
    }
}
