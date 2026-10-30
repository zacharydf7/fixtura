using System.Net;

namespace Fixtura;

/// <summary>
/// What a test user signed in with: a bearer token or the app's authentication cookie, whichever
/// <see cref="TestUsers.SignIn"/> picks. Use it with a client the fixture doesn't build, such as a SignalR connection, a
/// WebSocket or a browser.
/// </summary>
public sealed class SignedInUser
{
    internal SignedInUser(string? bearerToken, Cookie? cookie)
    {
        BearerToken = bearerToken;
        Cookie = cookie;
    }

    /// <summary>The token to send as <c>Authorization: Bearer</c>; null when the user signed in with a cookie.</summary>
    public string? BearerToken { get; }

    /// <summary>The app's authentication cookie, with no domain set; null when the user signed in with a token.</summary>
    public Cookie? Cookie { get; }
}
