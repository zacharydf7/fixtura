using System.Net;
using System.Net.Http.Json;
using Fixtura.Tests.Integration.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Fixtura.Tests.Integration.Users;

// Years from now, so a token checked against the system clock instead of the fake one would fail.
public sealed class FakeClockJwtFixture : TestHost<SymmetricJwtApp.Program>
{
    public static readonly DateTimeOffset Start = new(2040, 6, 1, 9, 0, 0, TimeSpan.Zero);

    protected override void Configure(TestHostBuilder host) => host.UseFakeClock(Start);
}

// The app doesn't check token lifetimes, so an advanced clock mustn't turn the check on.
public sealed class NoLifetimeCheckFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.UseFakeClock();
        host.Options<JwtBearerOptions>(options => options.TokenValidationParameters.ValidateLifetime = false);
    }
}

// The app's own fixed clock, in the past, with no fake clock from the fixture: tokens follow the system clock.
public sealed class AppFixedClockFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) => host.Services.Configure(services =>
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero))));
}

public sealed class FakeClockCookieFixture : TestHost<CookieApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Users.SignIn = UserSignIn.AppCookie;
        host.UseFakeClock(new DateTimeOffset(2040, 6, 1, 9, 0, 0, TimeSpan.Zero));
    }
}

public class FakeClockJwtTests(FakeClockJwtFixture api) : IClassFixture<FakeClockJwtFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignIn_WhenTheFakeClockIsYearsAhead()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("ann", me!.Name);
    }

    [Fact]
    public async Task Clock_ShouldExpireTheToken_WhenAdvancedPastItsLifetime()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));
        api.Clock.Advance(TimeSpan.FromHours(2));

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResetDataAsync_ShouldSetTheClockBackToItsStart_WhenTheClockWasAdvanced()
    {
        api.Clock.Advance(TimeSpan.FromDays(3));

        await api.ResetDataAsync();

        Assert.Equal(FakeClockJwtFixture.Start, api.Clock.GetUtcNow());
    }

    [Fact]
    public void UseFakeClock_ShouldThrow_WhenTheHostAlreadyHasAClock()
    {
        var error = Assert.Throws<InvalidOperationException>(() => api.WithOverrides(host => host.UseFakeClock()));

        Assert.Contains("already has a fake clock", error.Message);
    }

    [Fact]
    public void UseFakeClock_ShouldGiveTheAppTheFixturesClock_WhenCalled()
    {
        TimeProvider appClock = api.Services.GetRequiredService<TimeProvider>();

        Assert.Same(api.Clock, appClock);
    }
}

public class FakeClockCookieTests(FakeClockCookieFixture api) : IClassFixture<FakeClockCookieFixture>
{
    [Fact]
    public async Task Clock_ShouldExpireTheCookie_WhenAdvancedPastItsLifetime()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));
        api.Clock.Advance(TimeSpan.FromHours(2));

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public class NoFakeClockTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    [Fact]
    public void Clock_ShouldNameTheSetup_WhenTheFixtureHasNoFakeClock()
    {
        var error = Assert.Throws<InvalidOperationException>(() => api.Clock);

        Assert.Contains("host.UseFakeClock()", error.Message);
    }
}

public class NoLifetimeCheckTests(NoLifetimeCheckFixture api) : IClassFixture<NoLifetimeCheckFixture>
{
    [Fact]
    public async Task Clock_ShouldKeepTheTokenValid_WhenTheAppDoesNotCheckLifetimes()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));
        api.Clock.Advance(TimeSpan.FromDays(2));

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class AppFixedClockTests(AppFixedClockFixture api) : IClassFixture<AppFixedClockFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldSignIn_WhenTheAppHasItsOwnFixedClock()
    {
        HttpClient client = await api.CreateClientAsync(TestUser.Named("ann").WithRoles("Auditor"));

        HttpResponseMessage response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
