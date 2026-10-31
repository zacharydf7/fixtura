using Microsoft.Playwright;

namespace Fixtura.Tests.Integration.Playwright;

// The browser comes from FIXTURA_PLAYWRIGHT_REQUIRED's CI install; without one, these tests skip.
public sealed class CookieBrowserFixture : TestHost<CookieApp.Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.AddPlaywright(new PlaywrightOptions { RequiredWhenVariableIsTrue = "FIXTURA_PLAYWRIGHT_REQUIRED" });
        host.Users.SignIn = UserSignIn.AppCookie;
    }
}

public sealed class JwtBrowserFixture : TestHost<SymmetricJwtApp.Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.AddPlaywright(new PlaywrightOptions { RequiredWhenVariableIsTrue = "FIXTURA_PLAYWRIGHT_REQUIRED" });
}

public class CookieBrowserTests(CookieBrowserFixture api) : IClassFixture<CookieBrowserFixture>
{
    [Fact]
    public async Task OpenPageAsync_ShouldSignTheUserIn_WhenTheAppUsesACookie()
    {
        IPage page = await api.OpenPageAsync("/me", TestUser.Named("ann").WithRoles("Auditor"));

        string body = await page.InnerTextAsync("body");

        Assert.Contains("\"name\":\"ann\"", body);
    }

    // A browser takes cookies of up to 4 KB, so a larger ticket has to arrive split, as the app splits it.
    [Fact]
    public async Task OpenPageAsync_ShouldSignTheUserIn_WhenTheTicketIsLargerThanOneCookie()
    {
        TestUser user = TestUser.Named("erin").WithRoles("Auditor");
        for (int i = 0; i < 200; i++)
        {
            user = user.WithClaim($"note-{i}", "a value long enough to push the protected ticket past four kilobytes");
        }

        IPage page = await api.OpenPageAsync("/me", user);

        Assert.Contains("\"name\":\"erin\"", await page.InnerTextAsync("body"));
    }

    [Fact]
    public async Task OpenPageAsync_ShouldOpenThePageAnonymously_WhenGivenNoUser()
    {
        IPage page = await api.OpenPageAsync("/me");

        IReadOnlyList<BrowserContextCookiesResult> cookies = await page.Context.CookiesAsync();

        Assert.Empty(cookies);
    }

    [Fact]
    public async Task ResetDataAsync_ShouldCloseTheTestsPages_WhenCalled()
    {
        IPage page = await api.OpenPageAsync("/me", TestUser.Named("ann").WithRoles("Auditor"));

        await api.ResetDataAsync();

        Assert.True(page.IsClosed);
    }
}

public class JwtBrowserTests(JwtBrowserFixture api) : IClassFixture<JwtBrowserFixture>
{
    [Fact]
    public async Task OpenPageAsync_ShouldSignTheUserIn_WhenTheAppUsesABearerToken()
    {
        IPage page = await api.OpenPageAsync("/me", TestUser.Named("sam").WithRoles("Auditor"));

        string body = await page.InnerTextAsync("body");

        Assert.Contains("\"name\":\"sam\"", body);
    }

    // A page that loads from another site, such as a CDN, mustn't hand it the user's token.
    [Fact]
    public async Task OpenPageAsync_ShouldSendTheTokenOnlyToTheApp_WhenThePageCallsAnotherSite()
    {
        IPage page = await api.OpenPageAsync("/me", TestUser.Named("sam").WithRoles("Auditor"));
        Task<IRequest> request = page.WaitForRequestAsync("http://cdn.sample.invalid/script.js");

        await page.EvaluateAsync("() => fetch('http://cdn.sample.invalid/script.js').catch(() => null)");

        Dictionary<string, string> sent = await (await request).AllHeadersAsync();
        Assert.DoesNotContain("authorization", sent.Keys);
    }
}
