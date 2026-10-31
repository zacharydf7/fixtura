using System.Net;

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
    public HttpFake Respond(HttpStatusCode status, object? json = null) =>
        Add(new Route(null, null, null, [FakeResponse.Status(status, json)]));

    /// <summary>Answers every request that no method-and-path response matches.</summary>
    public HttpFake Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        Add(new Route(null, null, null, [FakeResponse.From(respond)]));

    /// <summary>
    /// Answers requests with this method and path, with JSON when given. The path is matched against the request's path,
    /// or its path and query when it contains "?". The latest matching response wins.
    /// </summary>
    public HttpFake Respond(HttpMethod method, string path, HttpStatusCode status, object? json = null) =>
        Respond(method, path, FakeResponse.Status(status, json));

    /// <summary>
    /// Answers requests with this method and path with <paramref name="response"/>, then <paramref name="more"/> in
    /// order, and keeps repeating the last, as a test of retries needs, such as a 503 and then a 200.
    /// </summary>
    public HttpFake Respond(HttpMethod method, string path, FakeResponse response, params FakeResponse[] more) =>
        Add(new Route(method, CheckedPath(path), null, [response, .. more]));

    /// <summary>
    /// Answers requests with this method and path that <paramref name="when"/> also accepts, such as by a header or the
    /// body, with <paramref name="response"/>, then <paramref name="more"/> in order.
    /// </summary>
    public HttpFake Respond(HttpMethod method, string path, Func<FakeHttpRequest, bool> when, FakeResponse response,
        params FakeResponse[] more) =>
        Add(new Route(method, CheckedPath(path), when, [response, .. more]));

    /// <summary>
    /// Throws, listing them, when responses set since <c>Configure</c>, or since the last reset, weren't all sent: a
    /// response no request matched, or the later responses of a sequence no request reached.
    /// </summary>
    public void VerifyAllResponsesUsed()
    {
        List<string> unused;
        lock (_gate)
        {
            unused = [.. _routes.Where(route => _baseline?.Contains(route) != true && route.Unsent > 0)
                .Select(route => route.Describe())];
        }

        if (unused.Count > 0)
        {
            throw new InvalidOperationException($"The fake for HTTP client '{ClientName}' has responses no request "
                + $"used: {string.Join(", ", unused)}.");
        }
    }

    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Route? route;
        FakeResponse? next;
        List<string> answered;
        lock (_gate)
        {
            var recorded = new FakeHttpRequest(request.Method, request.RequestUri!, Headers(request), body);
            _requests.Add(recorded);
            route = _routes.LastOrDefault(candidate =>
                    candidate.Method is not null && candidate.Matches(request, recorded))
                ?? _routes.LastOrDefault(candidate => candidate.Method is null);
            next = route?.Next();
            answered = route is null ? _routes.Select(candidate => candidate.Name).Distinct().ToList() : [];
        }

        // Without a catch-all every response has a method and path, and listing them shows a typo such as /quote.
        if (route is null)
        {
            string responses = answered.Count == 0 ? "It has no responses" : $"It answers {string.Join(", ", answered)}";
            throw new InvalidOperationException($"The fake for HTTP client '{ClientName}' has no response for {request.Method} "
                + $"{request.RequestUri}. {responses}; add one with Respond.");
        }

        HttpResponseMessage response = await next!.SendAsync(request, cancellationToken);
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
                foreach (Route route in _baseline)
                {
                    route.Rewind();
                }
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

    // A request's path always starts with "/", so a path without one would never match and fall through silently.
    private static string CheckedPath(string path) => path.StartsWith('/')
        ? path
        : throw new ArgumentException($"Paths start with '/', such as \"/{path}\".", nameof(path));

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

    // Holds where its sequence is; the fake's lock guards it.
    private sealed class Route(HttpMethod? method, string? path, Func<FakeHttpRequest, bool>? when,
        FakeResponse[] responses)
    {
        private int _sent;

        public HttpMethod? Method => method;

        public string? Path => path;

        // A condition can turn away a request this route's method and path match, so the error says it has one.
        public string Name => when is null ? $"{method} {path}" : $"{method} {path} (when its condition holds)";

        // Responses of the sequence no request has reached yet.
        public int Unsent => Math.Max(responses.Length - _sent, 0);

        public bool Matches(HttpRequestMessage request, FakeHttpRequest recorded)
        {
            Uri uri = request.RequestUri!;
            string actual = path!.Contains('?') ? uri.PathAndQuery : uri.AbsolutePath;
            return request.Method == method && actual == path && when?.Invoke(recorded) != false;
        }

        public FakeResponse Next() => responses[Math.Min(_sent++, responses.Length - 1)];

        public void Rewind() => _sent = 0;

        public string Describe()
        {
            string route = method is null ? "the catch-all response" : $"{method} {path}";
            return responses.Length == 1 ? route : $"{route} ({Unsent} of {responses.Length} unsent)";
        }
    }
}
