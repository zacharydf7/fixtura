using System.Net;
using System.Net.Http.Json;

namespace Fixtura;

/// <summary>
/// Answers one of the app's HTTP clients in-process and records what it sent. <c>ResetDataAsync</c> clears the recorded
/// requests and brings back the responses set in <c>Configure</c>, undoing any a test set.
/// </summary>
public sealed class HttpFake
{
    private readonly Lock _gate = new();
    private readonly List<Route> _routes = [];
    private readonly List<FakeHttpRequest> _requests = [];
    private List<Route>? _baseline;

    internal HttpFake(string clientName)
    {
        ClientName = clientName;
    }

    /// <summary>The name of the faked client; a typed client's name is its type name.</summary>
    public string ClientName { get; }

    /// <summary>What the app sent through this client, oldest first.</summary>
    public IReadOnlyList<FakeHttpRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    /// Waits until the app has sent a request through this client that <paramref name="match"/> accepts, any request
    /// unless given, and returns the first one. Fails after <paramref name="timeout"/>, five seconds unless given.
    /// </summary>
    public Task<FakeHttpRequest> WaitForRequestAsync(Func<FakeHttpRequest, bool>? match = null, TimeSpan? timeout = null) =>
        Waiting.UntilAsync(() => Task.FromResult(Requests.FirstOrDefault(match ?? (_ => true))
            ?? throw new InvalidOperationException($"The app sent no matching request through the '{ClientName}' client.")),
            timeout);

    /// <summary>Answers every request that no method-and-path response matches, with JSON when given.</summary>
    public HttpFake Respond(HttpStatusCode status, object? json = null) => Add(new Route(null, null, _ => Response(status, json)));

    /// <summary>Answers every request that no method-and-path response matches.</summary>
    public HttpFake Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) => Add(new Route(null, null, respond));

    /// <summary>
    /// Answers requests with this method and path, with JSON when given. The path is matched against the request's path,
    /// or its path and query when it contains "?". The latest matching response wins.
    /// </summary>
    public HttpFake Respond(HttpMethod method, string path, HttpStatusCode status, object? json = null)
    {
        // A request's path always starts with "/", so a path without one would never match and fall through silently.
        if (!path.StartsWith('/'))
        {
            throw new ArgumentException($"Paths start with '/', such as \"/{path}\".", nameof(path));
        }

        return Add(new Route(method, path, _ => Response(status, json)));
    }

    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Route? route;
        lock (_gate)
        {
            _requests.Add(new FakeHttpRequest(request.Method, request.RequestUri!, Headers(request), body));
            route = _routes.LastOrDefault(candidate => candidate.Method is not null && candidate.Matches(request))
                ?? _routes.LastOrDefault(candidate => candidate.Method is null);
        }

        if (route is null)
        {
            throw new InvalidOperationException(
                $"The fake for HTTP client '{ClientName}' has no response for {request.Method} {request.RequestUri}. Add one with Respond.");
        }

        HttpResponseMessage response = route.Respond(request);
        response.RequestMessage ??= request;
        return response;
    }

    // The first call wins, so an override host that shares this fake can't make a test's responses the baseline.
    internal void SaveBaseline()
    {
        lock (_gate)
        {
            _baseline ??= [.. _routes];
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _requests.Clear();
            if (_baseline is not null)
            {
                _routes.Clear();
                _routes.AddRange(_baseline);
            }
        }
    }

    private HttpFake Add(Route route)
    {
        lock (_gate)
        {
            _routes.Add(route);
        }

        return this;
    }

    private static HttpResponseMessage Response(HttpStatusCode status, object? json) =>
        new(status) { Content = json is null ? null : JsonContent.Create(json, json.GetType()) };

    private static Dictionary<string, string> Headers(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        if (request.Content is not null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        return headers;
    }

    private sealed record Route(HttpMethod? Method, string? Path, Func<HttpRequestMessage, HttpResponseMessage> Respond)
    {
        public bool Matches(HttpRequestMessage request)
        {
            Uri uri = request.RequestUri!;
            string actual = Path!.Contains('?') ? uri.PathAndQuery : uri.AbsolutePath;
            return request.Method == Method && actual == Path;
        }
    }
}
