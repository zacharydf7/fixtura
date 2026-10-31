using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Fixtura;

public static class PlaywrightTestHostExtensions
{
    /// <summary>
    /// Runs the app on Kestrel, where a browser can reach it, and adds a browser the fixture's tests open pages in with
    /// <c>OpenPageAsync</c>.
    /// </summary>
    public static PlaywrightResource AddPlaywright(this TestHostBuilder host, PlaywrightOptions? options = null)
    {
        host.Server = ServerKind.Kestrel;
        return host.AddResource(new PlaywrightResource(options ?? new PlaywrightOptions()));
    }

    extension<TProgram>(TestHost<TProgram> api) where TProgram : class
    {
        /// <summary>
        /// Opens the app's page at <paramref name="path"/> in the fixture's browser, signed in as the user through the
        /// app's own authentication, by cookie or bearer token, or anonymous when no user is given. Each page has a
        /// browser context of its own; <c>ResetDataAsync</c> closes them. Needs <c>host.AddPlaywright()</c>.
        /// </summary>
        public async Task<IPage> OpenPageAsync(string path, TestUser? user = null)
        {
            // Builds the host without blocking and checks it, as signing in does, before BaseAddress reads it.
            SignedInUser? signedIn = null;
            if (user is null)
            {
                await using AsyncServiceScope _ = await api.CreateScopeAsync();
            }
            else
            {
                signedIn = await api.SignInAsync(user);
            }

            return await api.Resource<PlaywrightResource>().OpenPageAsync(api.BaseAddress, signedIn, path);
        }
    }
}
