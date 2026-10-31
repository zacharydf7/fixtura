using Fixtura.Tests.Integration.Hosting;
using Microsoft.AspNetCore.Authorization;

namespace Fixtura.Tests.Integration.Users;

// Every endpoint without AllowAnonymous needs a signed-in user, as an app that locks everything down by default sets.
public sealed class FallbackPolicyFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Options<AuthorizationOptions>(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}

public class AnonymousEndpointsTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    [Fact]
    public async Task AllowingAnonymousAsync_ShouldListTheEndpointsWithoutAuthorization_WhenTheAppHasNoFallbackPolicy()
    {
        IReadOnlyList<string> anonymous = await api.Endpoints.AllowingAnonymousAsync();

        Assert.Equal(
        [
            "GET /carriers", "GET /culture", "GET /home", "GET /labels/{number}", "GET /parcels/{number}",
            "GET /payments/{method}", "GET /ping", "GET /rates/{currency}", "GET /swallowed", "POST /labels", "POST /parcels/{number}/track",
            "POST /returns"
        ], anonymous);
    }
}

public class FallbackPolicyEndpointsTests(FallbackPolicyFixture api) : IClassFixture<FallbackPolicyFixture>
{
    [Fact]
    public async Task AllowingAnonymousAsync_ShouldListOnlyAllowAnonymous_WhenTheAppHasAFallbackPolicy()
    {
        IReadOnlyList<string> anonymous = await api.Endpoints.AllowingAnonymousAsync();

        Assert.Equal(["GET /ping"], anonymous);
    }
}
