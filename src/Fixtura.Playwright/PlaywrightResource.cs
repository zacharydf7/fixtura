using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;

namespace Fixtura;

/// <summary>
/// A browser for one fixture, launched when the host is built. <c>ResetDataAsync</c> closes the pages the tests opened,
/// and dispose closes the browser.
/// </summary>
public sealed class PlaywrightResource(PlaywrightOptions options) : ITestResource
{
    private readonly Lock _gate = new();
    private readonly List<IBrowserContext> _contexts = [];
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<TestResourceStatus> ProvisionAsync(TestResourceContext context)
    {
        _playwright = await Playwright.CreateAsync();
        IBrowserType browserType = options.Browser switch
        {
            PlaywrightBrowser.Firefox => _playwright.Firefox,
            PlaywrightBrowser.WebKit => _playwright.Webkit,
            _ => _playwright.Chromium
        };
        try
        {
            _browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = options.Headless });
            return TestResourceStatus.Ready;
        }
        catch (PlaywrightException error) when (error.Message.Contains("Executable doesn't exist"))
        {
            string reason = $"Playwright's {options.Browser} isn't installed: run the playwright.ps1 the test project's "
                + $"build output holds, with `install {options.Browser.ToString().ToLowerInvariant()}`.";
            if (IsRequired())
            {
                throw new InvalidOperationException($"{reason} {options.RequiredWhenVariableIsTrue} is true, so the "
                    + "browser is required.", error);
            }

            return TestResourceStatus.Unavailable(reason);
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        foreach (IBrowserContext context in TakeContexts())
        {
            await context.CloseAsync();
        }
    }

    // Closing the browser closes its contexts and pages too.
    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    // Each page gets a context of its own, so one test's cookies and storage never reach another's.
    internal async Task<IPage> OpenPageAsync(Uri baseAddress, SignedInUser? user, string path)
    {
        IBrowserContext browserContext =
            await _browser!.NewContextAsync(new BrowserNewContextOptions { BaseURL = baseAddress.ToString() });
        lock (_gate)
        {
            _contexts.Add(browserContext);
        }

        // Only the app's own requests carry the token, never a CDN's or another site's.
        if (user?.BearerToken is string token)
        {
            await browserContext.RouteAsync($"{baseAddress.GetLeftPart(UriPartial.Authority)}/**", route =>
            {
                var headers = new Dictionary<string, string>(route.Request.Headers);
                headers["Authorization"] = $"Bearer {token}";
                return route.ContinueAsync(new RouteContinueOptions { Headers = headers });
            });
        }

        if (user?.Cookie is System.Net.Cookie cookie)
        {
            await browserContext.AddCookiesAsync(BrowserCookies(cookie, baseAddress));
        }

        IPage page = await browserContext.NewPageAsync();
        await page.GotoAsync(path);
        return page;
    }

    // A browser takes cookies of up to 4 KB, so the ticket is split as the app's cookie handler splits and reads it.
    private static List<Cookie> BrowserCookies(System.Net.Cookie cookie, Uri baseAddress)
    {
        var response = new DefaultHttpContext();
        new ChunkingCookieManager().AppendResponseCookie(response, cookie.Name, cookie.Value, new CookieOptions());
        List<Cookie> cookies = [];
        foreach (string header in response.Response.Headers.SetCookie.OfType<string>())
        {
            string pair = header[..header.IndexOf(';')];
            int equals = pair.IndexOf('=');
            cookies.Add(new Cookie { Name = pair[..equals], Value = pair[(equals + 1)..], Url = baseAddress.ToString() });
        }

        return cookies;
    }

    private List<IBrowserContext> TakeContexts()
    {
        lock (_gate)
        {
            List<IBrowserContext> contexts = [.. _contexts];
            _contexts.Clear();
            return contexts;
        }
    }

    private bool IsRequired()
    {
        string? variable = options.RequiredWhenVariableIsTrue;
        string? value = variable is null ? null : Environment.GetEnvironmentVariable(variable);
        return value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
