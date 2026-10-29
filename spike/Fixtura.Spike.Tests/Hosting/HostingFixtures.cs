using CookieIdentityApp;
using Fixtura.Spike.EntityFrameworkCore;
using Fixtura.Spike.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SampleApp.Data;

namespace Fixtura.Spike.Tests.Hosting;

public sealed class KestrelFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.Server = ServerKind.Kestrel;
}

public sealed class KestrelJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.Server = ServerKind.Kestrel;
}

public sealed class KestrelCookieFixture : TestHost<CookieIdentityApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Server = ServerKind.Kestrel;
        host.Services.ReplaceDbContext<AccountsDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Accounts")));
        host.Users.SignIn = UserSignIn.AppCookie;
        host.Users.SeedWith<IdentityUserSeeder<IdentityUser, IdentityRole>>();
    }
}

public sealed class OverridableFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseInMemoryDatabase(host.UniqueName("Orders")));
}

public sealed class RecordingResourceFixture : TestHost<Program>
{
    public RecordingResource Resource { get; } = new();

    protected override void Configure(TestHostBuilder host) => host.AddResource(Resource);
}

public sealed class RecordingResource : ITestResource
{
    public bool Disposed { get; private set; }

    public Task<string?> ProvisionAsync(string namePrefix) => Task.FromResult<string?>(null);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

public sealed class UnavailableFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.AddResource(new UnavailableResource());
}

public sealed class UnavailableResource : ITestResource
{
    public Task<string?> ProvisionAsync(string namePrefix) => Task.FromResult<string?>("The test resource is unavailable.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
