namespace Fixtura;

/// <summary>How <c>CreateClientAsync</c> builds a client, for the few tests that need something other than the default.</summary>
public sealed class TestClientOptions
{
    /// <summary>Follows redirects, as a browser does; set false to assert a redirect itself. True unless set.</summary>
    public bool AllowAutoRedirect { get; init; } = true;

    /// <summary>Where relative request URIs resolve, such as a base path; the host's own address unless set.</summary>
    public Uri? BaseAddress { get; init; }

    /// <summary>
    /// Handlers each request passes through before the app, outermost first. A handler belongs to one client, so give each
    /// client new ones.
    /// </summary>
    public IReadOnlyList<DelegatingHandler> Handlers { get; init; } = [];

    /// <summary>The scheme this client's user signs in through, instead of <see cref="TestUsers.Scheme"/>.</summary>
    public string? Scheme { get; init; }
}
