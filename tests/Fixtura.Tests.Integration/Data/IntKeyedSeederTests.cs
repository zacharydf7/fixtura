using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Tests.Integration.Data;

// An Identity store keyed by int, the case IdentityUserSeeder can't set an id on by itself.
public sealed class NumberedUser : IdentityUser<int>;

public sealed class NumberedRole : IdentityRole<int>;

public sealed class NumberedDbContext(DbContextOptions<NumberedDbContext> options)
    : IdentityDbContext<NumberedUser, NumberedRole, int>(options);

public sealed class NumberedUserSeeder : IdentityUserSeeder<NumberedUser, NumberedRole>
{
    protected override NumberedUser NewUser(TestUser user) => new() { Id = int.Parse(user.Id!) };
}

public class IntKeyedSeederTests
{
    [Fact]
    public async Task SeedAsync_ShouldUseTheIdNewUserSet_WhenTheUserIsNotStringKeyed()
    {
        await using ServiceProvider provider = NumberedStore();
        using IServiceScope scope = provider.CreateScope();

        await new NumberedUserSeeder().SeedAsync(TestUser.Named("jo").WithId("42"), scope.ServiceProvider);

        NumberedUser? stored = await scope.ServiceProvider.GetRequiredService<UserManager<NumberedUser>>().FindByIdAsync("42");
        Assert.Equal("jo", stored?.UserName);
    }

    [Fact]
    public async Task SeedAsync_ShouldExplainTheFix_WhenNewUserIgnoresTheIdOfANonStringKey()
    {
        await using ServiceProvider provider = NumberedStore();
        using IServiceScope scope = provider.CreateScope();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new IdentityUserSeeder<NumberedUser, NumberedRole>().SeedAsync(TestUser.Named("jo").WithId("42"), scope.ServiceProvider));

        Assert.Contains("set its id from TestUser.Id in NewUser", error.Message);
    }

    private static ServiceProvider NumberedStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NumberedDbContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<NumberedUser>().AddRoles<NumberedRole>().AddEntityFrameworkStores<NumberedDbContext>();
        return services.BuildServiceProvider();
    }
}
