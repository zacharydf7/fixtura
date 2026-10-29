using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Tests.Integration.Users;

// Counts seeder runs; registered in the app so the seeder can take it from the app's services.
public sealed class SeedLog
{
    public int Runs;
}

// Issues the claims an app's own sign-in would, which here differ from what the test user asked for.
public sealed class RenamingSeeder(SeedLog log) : ITestUserSeeder
{
    public Task<IReadOnlyList<Claim>> SeedAsync(TestUser user, IServiceProvider services)
    {
        Interlocked.Increment(ref log.Runs);
        IReadOnlyList<Claim> claims = [new(ClaimTypes.Name, "seeded " + user.Name), .. user.Roles.Select(role => new Claim(ClaimTypes.Role, role))];
        return Task.FromResult(claims);
    }
}

public sealed class SeededJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services => services.AddSingleton<SeedLog>());
        host.Users.SeedWith<RenamingSeeder>();
    }
}

public class SeederTests(SeededJwtFixture api) : IClassFixture<SeededJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignInWithTheSeedersClaims_WhenASeederIsSet()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("seeded ann", me!.Name);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldRunTheSeederBeforeEachSignIn_WhenTheSameUserSignsInTwice()
    {
        var log = api.Services.GetRequiredService<SeedLog>();
        int before = log.Runs;

        await api.CreateClientAsync(TestUser.Named("bob").WithRoles("Auditor"));
        await api.CreateClientAsync(TestUser.Named("bob").WithRoles("Auditor"));

        Assert.Equal(before + 2, log.Runs);
    }
}
